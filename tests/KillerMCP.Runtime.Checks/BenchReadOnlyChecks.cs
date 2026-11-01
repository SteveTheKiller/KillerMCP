using KillerMCP;
using System.Text.Json;

internal static class BenchReadOnlyChecks
{
    private static readonly string[] Commands = ["workflows", "workflow-options", "recipes", "pack", "validation-plan", "validation-coverage", "msi-code", "investigate", "investigate-batch", "workflow-journal", "task-journal", "open", "knowledge-info", "knowledge", "knowledge-search", "knowledge-log", "knowledge-compare", "knowledge-active", "mac-vendor", "pci-vendor", "usb-vendor", "uefi-vendor", "bluetooth-company", "driver-reference", "duplicate-files", "large-files", "installed-apps", "installed-apps-compare", "browser-extensions", "profiles", "task-plan", "task-definition", "service-check", "msi-product", "time-plan", "task-inventory"];

    public static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_BENCH_READONLY_FIXTURE") != "1") return false;
        if (args.SequenceEqual(["help"])) { Console.Write("full-scan"); return true; }
        if (args.SequenceEqual(["capabilities"]))
        {
            string? mode = Environment.GetEnvironmentVariable("KILLERMCP_BENCH_CAPABILITIES");
            if (mode == "legacy") { Environment.ExitCode = 2; return true; }
            if (mode == "malformed") { Console.Write("{\"schemaVersion\":1,\"version\":\"fixture\",\"commands\":[null]}"); return true; }
            Console.Write(JsonSerializer.Serialize(new { schemaVersion = 1, version = "fixture", commands = mode == "limited" ? new[] { "workflows" } : [.. Commands, "device-check"] }));
            return true;
        }
        if (Environment.GetEnvironmentVariable("KILLERMCP_BENCH_CALL_LOG") is { } log) File.AppendAllText(log, JsonSerializer.Serialize(args) + "\n");
        if (args.Contains("truncated-fixture")) { Console.Write(new string('X', 4 * 1024 * 1024 + 1000)); return true; }
        if (args.Contains("partial-fixture")) { Environment.ExitCode = 3; Console.Write("{\"partial\":true,\"state\":\"Unknown\"}"); return true; }
        if (args[0] == "investigate-batch") { foreach (var path in args.Skip(1)) Console.WriteLine(JsonSerializer.Serialize(new { path, error = "Fixture incomplete evidence" })); Environment.ExitCode = 3; return true; }
        Console.Write(JsonSerializer.Serialize(new { forwarded = args }));
        return true;
    }

    public static async Task RunAsync()
    {
        string scratch = Environment.GetEnvironmentVariable("KILLERMCP_BENCH_CHECK_ROOT") ?? throw new InvalidOperationException("Set KILLERMCP_BENCH_CHECK_ROOT to a fresh owned scratch folder.");
        if (Directory.Exists(scratch)) throw new InvalidOperationException("Check folder already exists.");
        Directory.CreateDirectory(scratch);
        string log = Path.Combine(scratch, "calls.jsonl");
        string file = Path.Combine(scratch, "evidence.json");
        File.WriteAllText(file, "{}");
        File.WriteAllText(log, "");
        Environment.SetEnvironmentVariable("KILLERMCP_BENCH_READONLY_FIXTURE", "1");
        Environment.SetEnvironmentVariable("KILLERMCP_BENCH_CALL_LOG", log);
        Environment.SetEnvironmentVariable("KILLERMCP_BENCH_CAPABILITIES", null);
        var tools = (await KillerBenchAdapter.CreateAsync(Environment.ProcessPath, null)).ToDictionary(item => item.Tool.Name);
        if (!tools.ContainsKey("killerbench_catalog")) throw new InvalidOperationException("Missing read-only catalog tool.");
        int assertions = 0;
        void Require(bool condition) { assertions++; if (!condition) throw new InvalidOperationException("Read-only check failed: " + assertions); }
        async Task<JsonElement> Call(string tool, object input, bool rejected = false)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(input));
            int before = File.ReadAllLines(log).Length;
            var response = await tools[tool].CallAsync(document.RootElement, CancellationToken.None);
            Require(response.IsError == rejected);
            if (rejected) { Require(File.ReadAllLines(log).Length == before); return default; }
            var root = JsonDocument.Parse(response.Text).RootElement.Clone();
            if (root.TryGetProperty("result", out var result)) return result.Clone();
            return root;
        }
        async Task Forward(string tool, object input, params string[] expected)
        {
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(input));
            using var schema = JsonDocument.Parse(tools[tool].Tool.InputSchema.ToJsonString());
            var schemaError = KillerMCP.Services.Mcp.ToolSchema.Validate(arguments.RootElement, schema.RootElement);
            if (schemaError is not null) throw new InvalidOperationException("Published schema rejected " + JsonSerializer.Serialize(input) + ": " + schemaError);
            Require(schemaError is null);
            var result = await Call(tool, input);
            Require(result.GetProperty("data").GetProperty("forwarded").EnumerateArray().Select(value => value.GetString()).SequenceEqual(expected));
        }
        void SchemaRefuses(string tool, object input)
        {
            using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(input));
            using var schema = JsonDocument.Parse(tools[tool].Tool.InputSchema.ToJsonString());
            Require(KillerMCP.Services.Mcp.ToolSchema.Validate(arguments.RootElement, schema.RootElement) is not null);
        }
        SchemaRefuses("killerbench_knowledge", new { operation = "bluetooth", pack = file, identity = "76" });
        SchemaRefuses("killerbench_knowledge", new { operation = "bluetooth", pack = file, identity = 65536 });
        SchemaRefuses("killerbench_knowledge", new { operation = "driver", pack = file, identity = 76, architecture = "x64", platform = "client", build = 26100 });
        SchemaRefuses("killerbench_knowledge", new { operation = "uefi", pack = file, registry = "PNP", identity = new string('A', 17) });
        SchemaRefuses("killerbench_knowledge", new { operation = "mac", pack = file, identity = "AA:BB:CC:00:11:22", build = 26100 });
        await Forward("killerbench_catalog", new { operation = "workflows" }, "workflows");
        await Forward("killerbench_catalog", new { operation = "workflow_options", workflow = "storage" }, "workflow-options", "storage");
        await Forward("killerbench_catalog", new { operation = "packs" }, "pack", "list");
        await Forward("killerbench_catalog", new { operation = "pack_validate", path = file }, "pack", "validate", file);
        await Forward("killerbench_catalog", new { operation = "validation_plan", recipe = "windows.component-store" }, "validation-plan", "--recipe", "windows.component-store");
        await Forward("killerbench_evidence", new { operation = "investigate", path = file }, "investigate", file, "--json");
        await Forward("killerbench_evidence", new { operation = "workflow_journal", path = file }, "workflow-journal", file);
        await Forward("killerbench_evidence", new { operation = "task_journal", path = file, completion = file }, "task-journal", file, "--completion", file);
        await Forward("killerbench_evidence", new { operation = "capture", path = file }, "open", file, "--json");
        await Forward("killerbench_knowledge", new { operation = "info", pack = file }, "knowledge-info", file);
        await Forward("killerbench_knowledge", new { operation = "lookup", pack = file, context = "fixture", query = "001" }, "knowledge", file, "fixture", "001");
        await Forward("killerbench_knowledge", new { operation = "log", pack = file, context = "fixture", path = file }, "knowledge-log", file, "fixture", file);
        await Forward("killerbench_knowledge", new { operation = "search", pack = file, context = "fixture", query = "synthetic" }, "knowledge-search", file, "fixture", "synthetic");
        await Forward("killerbench_knowledge", new { operation = "compare", before = file, after = file }, "knowledge-compare", file, file);
        await Forward("killerbench_knowledge", new { operation = "driver", pack = file, identity = "PCI\\VEN_1234", architecture = "x64", platform = "client", build = 26100 }, "driver-reference", file, "PCI\\VEN_1234", "x64", "client", "26100");
        await Forward("killerbench_knowledge", new { operation = "mac", pack = file, identity = "AA:BB:CC:00:11:22" }, "mac-vendor", file, "AA:BB:CC:00:11:22");
        await Forward("killerbench_knowledge", new { operation = "pci", pack = file, identity = "PCI\\VEN_1234" }, "pci-vendor", file, "PCI\\VEN_1234");
        await Forward("killerbench_knowledge", new { operation = "usb", pack = file, identity = "USB\\VID_1234" }, "usb-vendor", file, "USB\\VID_1234");
        await Forward("killerbench_knowledge", new { operation = "bluetooth", pack = file, identity = 76 }, "bluetooth-company", file, "76");
        await Forward("killerbench_knowledge", new { operation = "uefi", pack = file, registry = "PNP", identity = "ABC" }, "uefi-vendor", file, "PNP", "ABC");
        await Forward("killerbench_review", new { operation = "duplicates", root = scratch, maxFiles = 12, minBytes = 1 }, "duplicate-files", scratch, "--min-bytes", "1", "--max-files", "12");
        await Forward("killerbench_review", new { operation = "large_files", root = scratch, limit = 4 }, "large-files", scratch, "--limit", "4");
        await Forward("killerbench_review", new { operation = "applications" }, "installed-apps");
        await Forward("killerbench_review", new { operation = "applications_compare", before = file, after = file }, "installed-apps-compare", file, file);
        await Forward("killerbench_review", new { operation = "browser_extensions" }, "browser-extensions");
        await Forward("killerbench_review", new { operation = "device", category = "problems" }, "device-check", "problems");
        await Call("killerbench_review", new { operation = "device", category = "invalid" }, true);
        await Forward("killerbench_review", new { operation = "profiles", days = 90, dependencies = true }, "profiles", "--days", "90", "--dependencies");
        await Forward("killerbench_review", new { operation = "profiles", dependencies = false }, "profiles");
        await Forward("killerbench_review", new { operation = "task_plan", path = file }, "task-plan", file);
        await Forward("killerbench_review", new { operation = "task_definition", folder = "\\Fixture", name = "Example" }, "task-definition", "\\Fixture", "Example");
        await Forward("killerbench_review", new { operation = "services", names = new[] { "Spooler", "wuauserv" } }, "service-check", "Spooler", "wuauserv");
        await Forward("killerbench_review", new { operation = "msi_product", productCode = "{00000000-0000-0000-0000-000000000001}" }, "msi-product", "{00000000-0000-0000-0000-000000000001}");
        await Forward("killerbench_review", new { operation = "time_plan" }, "time-plan");
        await Forward("killerbench_check", new { check = "task_inventory" }, "task-inventory");
        await Forward("killerbench_code_lookup", new { kind = "msi", code = "1603" }, "msi-code", "1603");

        await Call("killerbench_review", new { operation = "profiles", execute = true }, true);
        await Call("killerbench_review", new { operation = "profiles", backupTo = scratch }, true);
        await Call("killerbench_review", new { operation = "profiles", days = 0 }, true);
        await Call("killerbench_review", new { operation = "profiles", dependencies = "true" }, true);
        await Call("killerbench_review", new { operation = "large_files", root = scratch, maxFiles = 10001 }, true);
        await Call("killerbench_review", new { operation = "task_plan", path = file, inspectExecutable = true }, true);
        await Call("killerbench_review", new { operation = "services", names = new[] { "--execute" } }, true);
        await Call("killerbench_review", new { operation = "msi_product", productCode = "foo;calc" }, true);
        await Call("killerbench_check", new { check = "task_inventory", drive = "C:" }, true);
        await Call("killerbench_check", new { check = "vitals", host = "unused.example" }, true);
        await Call("killerbench_catalog", new { operation = "workflows", workflow = "unused" }, true);
        await Call("killerbench_catalog", new { operation = "pack_export", path = file }, true);
        await Call("killerbench_knowledge", new { operation = "activate", pack = file }, true);
        await Call("killerbench_knowledge", new { operation = "search", pack = file, context = "fixture", query = "--execute" }, true);
        await Call("killerbench_knowledge", new { operation = "driver", pack = file, identity = "PCI\\VEN_1234", architecture = "invalid", platform = "client", build = 1 }, true);
        await Call("killerbench_evidence", new { operation = "batch", paths = Array.Empty<string>() }, true);
        await Call("killerbench_evidence", new { operation = "batch", paths = Enumerable.Repeat(file, 13).ToArray() }, true);
        await Call("killerbench_evidence", new { operation = "investigate", path = "relative.json" }, true);
        await Call("killerbench_evidence", new { operation = "investigate", path = @"\\.\PhysicalDrive0" }, true);
        await Call("killerbench_evidence", new { operation = "investigate", path = @"\\203.0.113.9\share\private.log" }, true);
        int duplicateBefore = File.ReadAllLines(log).Length;
        using (var duplicate = JsonDocument.Parse("{\"operation\":\"workflows\",\"operation\":\"packs\"}"))
            Require((await tools["killerbench_catalog"].CallAsync(duplicate.RootElement, CancellationToken.None)).IsError);
        Require(File.ReadAllLines(log).Length == duplicateBefore);
        var batch = await Call("killerbench_evidence", new { operation = "batch", paths = new[] { file, file } });
        Require(batch.GetProperty("exitCode").GetInt32() == 3 && batch.GetProperty("records").GetArrayLength() == 2);
        var partial = await Call("killerbench_knowledge", new { operation = "search", pack = file, context = "fixture", query = "partial-fixture" });
        Require(partial.GetProperty("exitCode").GetInt32() == 3 && partial.GetProperty("data").GetProperty("partial").GetBoolean());
        var truncated = await Call("killerbench_knowledge", new { operation = "search", pack = file, context = "fixture", query = "truncated-fixture" });
        Require(truncated.GetProperty("truncated").GetBoolean() && !truncated.TryGetProperty("data", out _));

        string record = Path.Combine(scratch, "record.json");
        File.WriteAllText(record, "{\"artifacts\":[{\"name\":\"evidence.json\"}]}");
        await Forward("killerbench_catalog", new { operation = "validation_coverage", paths = new[] { record } }, "validation-coverage", record);
        File.WriteAllText(record, "{\"artifactDirectory\":\"..\\\\outside\",\"artifacts\":[{\"name\":\"private\"}]}");
        await Call("killerbench_catalog", new { operation = "validation_coverage", paths = new[] { record } }, true);
        File.WriteAllText(Path.Combine(scratch, "active.json"), "{\"FileName\":\"pack-example.kbkp\"}");
        await Forward("killerbench_knowledge", new { operation = "active_status", root = scratch, publicKey = file, packId = "example", minimumRevision = 1 }, "knowledge-active", scratch, file, "example", "1");
        File.WriteAllText(Path.Combine(scratch, "active.json"), "{\"FileName\":\"\\\\\\\\203.0.113.9\\\\private\"}");
        await Call("killerbench_knowledge", new { operation = "active_status", root = scratch, publicKey = file, packId = "example", minimumRevision = 1 }, true);
        File.WriteAllText(Path.Combine(scratch, "active.json"), new string('X', 16385));
        await Call("killerbench_knowledge", new { operation = "active_status", root = scratch, publicKey = file, packId = "example", minimumRevision = 1 }, true);
        File.WriteAllText(record, new string('X', 1048577));
        await Call("killerbench_catalog", new { operation = "validation_coverage", paths = new[] { record } }, true);

        foreach (var mode in new[] { "legacy", "malformed", "limited" })
        {
            Environment.SetEnvironmentVariable("KILLERMCP_BENCH_CAPABILITIES", mode);
            tools = (await KillerBenchAdapter.CreateAsync(Environment.ProcessPath, null)).ToDictionary(item => item.Tool.Name);
            Require(tools.ContainsKey("killerbench_repair_preview"));
            Require(tools.ContainsKey("killerbench_catalog") == (mode == "limited"));
            Require(!tools.ContainsKey("killerbench_evidence"));
            await Call("killerbench_code_lookup", new { kind = "msi", code = "1603" }, true);
            if (mode == "limited") await Call("killerbench_catalog", new { operation = "packs" }, true);
        }
        Environment.SetEnvironmentVariable("KILLERMCP_BENCH_CAPABILITIES", null);
        Environment.SetEnvironmentVariable("KILLERMCP_BENCH_READONLY_FIXTURE", null);
        Environment.SetEnvironmentVariable("KILLERMCP_BENCH_CALL_LOG", null);
        Console.WriteLine($"PASS: {assertions} read-only KillerBench checks, exact forwarding, mutation refusal, bounds, capability compatibility, nested references, partial and truncated evidence.");
    }

    public static async Task RunRealAsync()
    {
        string cli = Environment.GetEnvironmentVariable("KILLERMCP_BENCH_REAL_CLI") ?? throw new InvalidOperationException("Missing CLI path.");
        string root = Environment.GetEnvironmentVariable("KILLERMCP_BENCH_CHECK_ROOT") ?? throw new InvalidOperationException("Missing owned root.");
        if (Directory.Exists(root)) throw new InvalidOperationException("Fresh root already exists.");
        Directory.CreateDirectory(root);
        string xml = Path.Combine(root, "events.xml");
        File.WriteAllText(xml, "<Events><Event xmlns=\"http://schemas.microsoft.com/win/2004/08/events/event\"><System><Provider Name=\"Microsoft-Windows-PrintService\"/><EventID>372</EventID><EventRecordID>1</EventRecordID><Level>2</Level><TimeCreated SystemTime=\"2026-09-12T12:00:00Z\"/></System></Event></Events>");
        string pack = Path.Combine(root, "pack.json");
        File.WriteAllText(pack, JsonSerializer.Serialize(new { SchemaVersion = 1, Id = "fixture", Revision = 1, PublishedOn = "2026-09-13", License = "Synthetic", Entries = new[] { new { Context = "fixture", Code = "001", Title = "Synthetic symptom", Explanation = "Test reference", SourceUrl = "https://example.com/reference", VerifiedOn = "2026-09-13", AppliesTo = "Fixture product" } } }));
        var tools = (await KillerBenchAdapter.CreateAsync(cli, null)).ToDictionary(item => item.Tool.Name);
        int count = 0;
        async Task<JsonElement> Read(string name, object arguments)
        {
            using var input = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            var response = await tools[name].CallAsync(input.RootElement, CancellationToken.None);
            if (response.IsError) throw new InvalidOperationException(response.Text);
            using var parsed = JsonDocument.Parse(response.Text);
            var value = parsed.RootElement;
            if (value.TryGetProperty("result", out var result)) value = result;
            if (value.GetProperty("exitCode").GetInt32() is not (0 or 3)) throw new InvalidOperationException(response.Text);
            count++;
            return value.Clone();
        }
        await Read("killerbench_catalog", new { operation = "workflows" });
        await Read("killerbench_catalog", new { operation = "workflow_options", workflow = "storage" });
        await Read("killerbench_catalog", new { operation = "validation_plan" });
        await Read("killerbench_code_lookup", new { kind = "msi", code = "1603" });
        var evidence = await Read("killerbench_evidence", new { operation = "investigate", path = xml });
        if (evidence.GetProperty("data").GetProperty("findings").GetArrayLength() != 1) throw new InvalidOperationException("XML finding missing.");
        var batch = await Read("killerbench_evidence", new { operation = "batch", paths = new[] { xml, xml } });
        if (batch.GetProperty("records").GetArrayLength() != 2) throw new InvalidOperationException("Batch order/count missing.");
        await Read("killerbench_knowledge", new { operation = "info", pack });
        var search = await Read("killerbench_knowledge", new { operation = "search", pack, context = "fixture", query = "synthetic" });
        if (search.GetProperty("data").GetProperty("totalMatches").GetInt32() != 1) throw new InvalidOperationException("Knowledge search missing.");
        await Read("killerbench_knowledge", new { operation = "compare", before = pack, after = pack });
        Console.WriteLine($"PASS: {count} real CLI adapter reads, catalog discovery, MSI lookup, XML investigation/batch and knowledge info/search/compare. No native diagnostics or repairs.");
    }
}
