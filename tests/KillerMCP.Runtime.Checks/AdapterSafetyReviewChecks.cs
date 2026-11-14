using KillerMCP;
using System.Text.Json;

internal static class AdapterSafetyReviewChecks
{
    private const string FixtureVariable = "KILLERMCP_ADAPTER_REVIEW_FIXTURE";

    public static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable(FixtureVariable) != "1") return false;
        if (args.SequenceEqual(["--help"]))
        {
            Console.Write("search <folder> [--max-seconds 1..3600] list <folder> info <path> read <file> processes [ services [ --detail --filter events < registry < hash <file> drives storage < --max-seconds\nKillerShot command line\nmonitors\nwindows [--limit\ncapture [options]\nrecord --seconds\nrecent [options]\nopen <image>\nsettings\ntext --languages\nqr\nrecording\nedit\n"); return true;
        }
        if (args[0] == "version") { Console.Write("1.1.1"); return true; }
        if (args[0] == "processes")
        {
            var filter = Array.IndexOf(args, "--filter");
            var rows = new[]
            {
                new { pid = 42, name = "fixture", path = @"C:\fixture.exe", commandLine = "fixture --password hunter-secret --mode status" },
                new { pid = 43, name = "long-fixture", path = @"C:\fixture.exe", commandLine = new string('x', 2000) + " match-beyond-cut --password hunter-secret" }
            };
            Console.Write(JsonSerializer.Serialize(new { processes = rows.Where(row => filter < 0 || row.commandLine.Contains(args[filter + 1], StringComparison.OrdinalIgnoreCase) || row.name.Contains(args[filter + 1], StringComparison.OrdinalIgnoreCase)).ToArray() })); return true;
        }
        if (args[0] == "events") { Console.Write("{\"events\":[{\"message\":\"Password=events-secret; ordinary\"}]}"); return true; }
        if (args[0] == "registry") { Console.Write("{\"values\":[{\"name\":\"ApiToken\",\"data\":\"registry-secret\"},{\"name\":\"Connection\",\"data\":\"Password=connection-secret; ordinary\"}]}"); return true; }
        if (args[0] == "capture") { Console.Write(JsonSerializer.Serialize(new { path = @"C:\fixture.png", arguments = args })); return true; }
        if (args[0] == "hash") { Thread.Sleep(11000); Console.Write("{\"hash\":\"fixture-sha256\"}"); return true; }
        if (args[0] == "search") { Console.Write(JsonSerializer.Serialize(new { results = new[] { new { path = args[1] + @"\match.txt" } }, stoppedEarly = args.Contains("--max-seconds") })); return true; }
        if (args.SequenceEqual(["/help"]))
        {
            Console.Write("killerscan network killerscan locations /scan [targets] /probe <IPv4> /vendor <MAC> /ping <target> /trace <target> /diagnose <target> /watch <targets> /location <name|id> /max-seconds <n>");
            return true;
        }
        if (args[0] is "/ping" or "/trace" or "/diagnose" or "/watch")
        {
            Console.Write(JsonSerializer.Serialize(new { reachable = false, samples = Array.Empty<object>(), stoppedEarly = args.Contains("/max-seconds"), arguments = args })); Environment.ExitCode = 3; return true;
        }
        if (args[0] == "/vendor") { Console.Write("Unknown"); Environment.ExitCode = 3; return true; }
        if (args[0] == "/locations") { Console.Write("[{\"id\":\"saved-public-id\",\"name\":\"Saved Public\",\"lastTargets\":\"203.0.113.0/24\"},{\"id\":\"saved-range-id\",\"name\":\"Saved Range\",\"lastTargets\":\"203.0.114.10-50\"}]"); return true; }
        if (args[0] is "/scan" or "/probe") { Console.Write("[]"); return true; }
        if (args[0] == "/network") { Console.Write("SUBNET  192.168.1.0/24"); return true; }
        Console.Error.Write("Unsupported adapter review fixture command"); Environment.ExitCode = 2; return true;
    }

    private static async Task<AppCallResult> Call(IReadOnlyList<AppAdapter> tools, string name, object input)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(input));
        return await tools.Single(item => item.Tool.Name == name).CallAsync(document.RootElement, CancellationToken.None);
    }

    public static async Task RunAsync()
    {
        var previous = Environment.GetEnvironmentVariable(FixtureVariable);
        Environment.SetEnvironmentVariable(FixtureVariable, "1");
        try
        {
            var tools = await KillerScanAdapter.CreateAsync(Environment.ProcessPath!);
            foreach (var name in new[] { "killerscan_ping", "killerscan_trace_route", "killerscan_diagnose_host" })
            {
                var result = await Call(tools, name, new { target = "8.8.8.8" });
                if (result.IsError || !result.Text.Contains("false")) throw new InvalidOperationException(name + " discarded the down-target JSON.");
            }
            var watch = await Call(tools, "killerscan_watch_hosts", new { targets = new[] { "8.8.8.8" } });
            if (watch.IsError || !watch.Text.Contains("samples")) throw new InvalidOperationException("Watch discarded down-target samples.");
            Console.WriteLine("PASS down-target JSON for ping, trace, diagnose and watch");
            var vendor = await Call(tools, "killerscan_mac_vendor", new { mac = "00:11:22:33:44:55" });
            if (vendor.IsError || !vendor.Text.Contains("Unknown")) throw new InvalidOperationException("An unknown MAC vendor was reported as failure.");
            Console.WriteLine("PASS unknown MAC vendor");
            foreach (var name in new[] { "killerscan_scan_network", "killerscan_probe_host" })
            {
                if (!(await Call(tools, name, new { target = "8.8.8.8" })).IsError) throw new InvalidOperationException(name + " accepted an unsaved public target.");
                if (!(await Call(tools, name, new { target = "8.8.8.8", location = "invented" })).IsError) throw new InvalidOperationException(name + " accepted an invented Location bypass.");
                if (!(await Call(tools, name, new { target = "8.8.8.8", location = "Saved Public" })).IsError) throw new InvalidOperationException(name + " accepted a public target outside its saved Location.");
                if ((await Call(tools, name, new { target = "203.0.113.7", location = "Saved Public" })).IsError) throw new InvalidOperationException(name + " refused a verified saved target.");
                if ((await Call(tools, name, new { target = "203.0.114.11", location = "Saved Range" })).IsError) throw new InvalidOperationException(name + " refused the native short-range syntax in a saved Location.");
                foreach (var target in new[] { "10.0.0.1", "172.16.0.1", "192.168.1.1", "169.254.1.1", "100.64.0.1" })
                    if ((await Call(tools, name, new { target })).IsError) throw new InvalidOperationException(name + " refused an approved private target.");
            }
            Console.WriteLine("PASS private and verified saved-Location scan boundaries");
            var shell = await KillerShellAdapter.CreateAsync(Environment.ProcessPath!);
            var hidden = await Call(shell, "killershell_list_processes", new { detail = true, filter = "hunter-secret" });
            if (hidden.IsError || JsonDocument.Parse(hidden.Text).RootElement.GetProperty("processes").GetArrayLength() != 0)
                throw new InvalidOperationException("The process filter disclosed a masked secret.");
            var visible = await Call(shell, "killershell_list_processes", new { detail = true, filter = "status" });
            if (visible.IsError || visible.Text.Contains("hunter-secret") || JsonDocument.Parse(visible.Text).RootElement.GetProperty("processes").GetArrayLength() != 1)
                throw new InvalidOperationException("Masked process filtering lost a visible match.");
            var longMatch = await Call(shell, "killershell_list_processes", new { detail = true, filter = "match-beyond-cut" });
            if (longMatch.IsError || JsonDocument.Parse(longMatch.Text).RootElement.GetProperty("processes").GetArrayLength() != 1)
                throw new InvalidOperationException("Process filtering lost a nonsecret match beyond the display truncation.");
            Console.WriteLine("PASS process filters run after masking");
            var events = await Call(shell, "killershell_read_event_log", new { log = "Application" });
            var registry = await Call(shell, "killershell_read_registry_key", new { path = @"HKEY_CURRENT_USER\Software\Fixture" });
            if (events.IsError || registry.IsError || events.Text.Contains("events-secret") || registry.Text.Contains("registry-secret") || registry.Text.Contains("connection-secret"))
                throw new InvalidOperationException("Registry or event output disclosed likely secrets.");
            if (!events.Text.Contains("ordinary") || !registry.Text.Contains("ordinary")) throw new InvalidOperationException("Masking removed ordinary result text.");
            Console.WriteLine("PASS registry and event secret masking");
            var shot = await KillerShotAdapter.CreateAsync(Environment.ProcessPath!);
            var capture = await Call(shot, "killershot_capture_monitor", new { });
            if (capture.IsError || capture.Text.Contains("--no-shutter")) throw new InvalidOperationException("Agent capture forced the shutter off.");
            if (!shot.Single(item => item.Tool.Name == "killershot_record").Tool.Description.Contains("room", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Recording does not explain room audio.");
            Console.WriteLine("PASS capture shutter preference and recording audio description");
            foreach (var name in new[] { "killershot_read_text", "killershot_text_languages", "killershot_read_qr" })
                if (!JsonSerializer.SerializeToElement(ToolSafety.For(name)).GetProperty("readOnlyHint").GetBoolean()) throw new InvalidOperationException(name + " is not read-only.");
            foreach (var name in new[] { "killerpdf_set_metadata", "killerpdf_set_bookmarks", "killerpdf_add_comment", "killerpdf_delete_comments", "killerpdf_crop" })
                if (JsonSerializer.SerializeToElement(ToolSafety.For(name)).GetProperty("destructiveHint").GetBoolean()) throw new InvalidOperationException(name + " is incorrectly destructive.");
            if (!JsonSerializer.SerializeToElement(ToolSafety.For("killershot_recording_control")).GetProperty("destructiveHint").GetBoolean()) throw new InvalidOperationException("Recording discard is not marked destructive.");
            var rejected = false;
            try { ToolSafety.For("unregistered_review_tool"); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("An unknown tool fell through the annotation policy.");
            KillerMCP.Services.Mcp.ToolRegistry.SetAdapters([.. tools, .. shell, .. shot]);
            if (KillerMCP.Services.Mcp.ToolRegistry.Tools.Length < 100) throw new InvalidOperationException("Annotation coverage selected no meaningful tool set.");
            Console.WriteLine("PASS explicit annotation coverage and nine annotation corrections");
            using var registryInput = JsonDocument.Parse(JsonSerializer.Serialize(new { name = "killershell_read_registry_key", arguments = new { path = @"HKEY_CURRENT_USER\Software\Fixture" } }));
            var registryCall = JsonSerializer.SerializeToElement(await KillerMCP.Services.Mcp.ToolRegistry.CallAsync(registryInput.RootElement, new JsonSerializerOptions()));
            if (registryCall.GetProperty("isError").GetBoolean()) throw new InvalidOperationException("The host treated a registry key as a filesystem path.");
            Console.WriteLine("PASS registry key exemption through the host call path");
            var root = Environment.GetEnvironmentVariable("KILLERMCP_ADAPTER_CHECK_ROOT") ?? throw new InvalidOperationException("Set KILLERMCP_ADAPTER_CHECK_ROOT to an owned scratch folder.");
            Directory.CreateDirectory(root);
            var largeFile = Path.Combine(root, "hash-fixture.bin");
            using (var file = new FileStream(largeFile, FileMode.CreateNew)) file.SetLength(64L * 1024 * 1024);
            try
            {
                var hash = await Call(shell, "killershell_hash_file", new { path = largeFile });
                if (hash.IsError || !hash.Text.Contains("fixture-sha256")) throw new InvalidOperationException("Hashing kept the directory-list timeout for a large file.");
            }
            finally { File.Delete(largeFile); }
            Console.WriteLine("PASS file-size-aware hashing deadline");
            var search = await Call(shell, "killershell_search_files", new { root, content = "needle" });
            if (search.IsError || !JsonDocument.Parse(search.Text).RootElement.GetProperty("stoppedEarly").GetBoolean()) throw new InvalidOperationException("The search adapter did not request or preserve bounded partial results.");
            var maximalWatch = await Call(tools, "killerscan_watch_hosts", new { targets = Enumerable.Range(1, 16).Select(i => "192.168.1." + i).ToArray(), count = 20, interval = 10 });
            using var watchResult = JsonDocument.Parse(maximalWatch.Text);
            var arguments = watchResult.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
            var at = Array.IndexOf(arguments, "/max-seconds");
            if (maximalWatch.IsError || at < 0 || arguments[at + 1] != "579" || !watchResult.RootElement.GetProperty("stoppedEarly").GetBoolean()) throw new InvalidOperationException("The watch adapter did not budget for all targets and retain partial samples.");
            Console.WriteLine("PASS search and maximal watch bounded partial-result contracts");
        }
        finally { Environment.SetEnvironmentVariable(FixtureVariable, previous); }
    }
}
