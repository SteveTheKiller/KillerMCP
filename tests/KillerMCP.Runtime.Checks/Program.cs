using KillerMCP.Runtime;
using System.Text.Json;

if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_BENCH") == "1")
{
    var bench = Environment.GetCommandLineArgs().Skip(1).ToArray();
    var execute = bench.Contains("--execute");
    if (bench.SequenceEqual(["help"])) { Console.WriteLine("killerbench-cli full-scan [--recipes ID[,ID]] [--json]"); return; }
    if (bench.SequenceEqual(["vitals-check"])) { Console.Write(JsonSerializer.Serialize(new { name = "Vitals", state = "Clear" })); return; }
    if (bench.Length == 2 && bench[0] == "update-code") { Console.Write(JsonSerializer.Serialize(new { code = bench[1], meaning = "Fixture" })); return; }
    if (bench.SequenceEqual(["recipes"]))
    {
        Console.Write(JsonSerializer.Serialize(new object[]
        {
            new { id = "printing.spooler", title = "Print Spooler availability", dependencies = Array.Empty<string>(), canAutoExecute = true, requiresExplicitOptions = false, diagnosticOnly = false, recoveryContract = "ExactServiceState", effects = "Starts the spooler." },
            new { id = "windows.component-store", title = "Windows component store", dependencies = Array.Empty<string>(), canAutoExecute = true, requiresExplicitOptions = false, diagnosticOnly = false, recoveryContract = "ServicingHealth", effects = "DISM may replace content." },
        }));
        return;
    }
    if (bench.Length > 0 && bench[0] == "full-scan")
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_BENCH_SLOW") == "1") Thread.Sleep(1500);
        Console.Write(JsonSerializer.Serialize(new { outcome = execute || bench.Contains("--auto-repair") ? "Completed" : "Preview", results = new[] { new { title = "Print Spooler availability", state = "Attention", detail = "Fixture finding." } } }));
        return;
    }
    if (bench.Length > 0 && bench[0] == "dhcp-renew") { Console.WriteLine("Preview only."); Console.Write(JsonSerializer.Serialize(new { name = "DHCP leases", state = "Clear" })); return; }
    if (bench.Length > 0 && bench[0] is "dns-cache" or "repair" or "privacy-setting")
    {
        Console.WriteLine("Fixture preamble line.");
        if (execute && int.TryParse(Environment.GetEnvironmentVariable("KILLERMCP_FAKE_BENCH_EXIT"), out var exit) && exit != 0)
        {
            Console.Write(JsonSerializer.Serialize(new { outcome = "Failed" }));
            Environment.ExitCode = exit;
            return;
        }
        Console.Write(JsonSerializer.Serialize(new { outcome = execute ? "Completed" : "Preview", args = bench }));
        return;
    }
    if (bench.SequenceEqual(["dns-cache-recover"])) { Console.Write(JsonSerializer.Serialize(new { outcome = "Recovered" })); return; }
}

if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_BENCH") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHELL") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SCAN") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF") == "1")
{
    var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
    if (arguments.SequenceEqual(["/help"]) && Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SCAN") == "1")
    {
        Console.WriteLine("/network /scan [targets] /probe <IPv4> /vendor <MAC> /ping <target> /trace <target> /diagnose <target> /watch <targets> /speedtest");
        return;
    }
    if (arguments.SequenceEqual(["/network"]))
    {
        Console.WriteLine("INTERFACE  Ethernet\nLOCAL IP  192.0.2.10\nSUBNET  192.0.2.0/24\nGATEWAY  192.0.2.1\nDNS  192.0.2.53");
        return;
    }
    if (arguments.Length >= 2 && arguments[0] == "/vendor")
    {
        Console.Write("Cisco Systems");
        return;
    }
    if (arguments.Length > 0 && arguments[0] == "/scan")
    {
        Console.Write("[{\"IpAddress\":\"192.0.2.10\",\"Hostname\":\"workstation.example\",\"MacAddress\":\"00:11:22:33:44:55\",\"Vendor\":\"Example Devices\",\"DeviceType\":\"Computer\",\"OpenPortsDisplay\":\"80, 443\"},{\"IpAddress\":\"192.0.2.1\",\"Hostname\":\"gateway.example\",\"MacAddress\":\"00:11:22:33:44:01\",\"Vendor\":\"Example Networks\",\"DeviceType\":\"Router\",\"OpenPortsDisplay\":\"443\"}]");
        return;
    }
    if (arguments.Length > 0 && arguments[0] == "/probe")
    {
        Console.Write("[{\"IpAddress\":\"192.0.2.20\",\"Hostname\":\"probe.example\"}]");
        return;
    }
    if (arguments.Length > 0 && arguments[0] is "/ping" or "/trace" or "/diagnose" or "/watch" or "/speedtest")
    {
        Console.Write(JsonSerializer.Serialize(new { command = arguments[0], ok = true }));
        return;
    }
    if (arguments.SequenceEqual(["--help"]))
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF") == "1") Console.WriteLine("  --merge <out.pdf>\n--extract-pages <in.pdf>\n--split <in.pdf>\n--decrypt <in.pdf>\n--to-image <in.pdf>\n--flatten <in.pdf>\n--print <in.pdf>\n--ocr <in.pdf>\n--batch-resave <in>\n--batch-render <in>\n--rotate-pages <in.pdf>\n--delete-pages <in.pdf>\n--move-pages <in.pdf>\n--insert-blank <in.pdf>\n--duplicate-page <in.pdf>\n--document-info <in.pdf>\n--search-text <in.pdf>\n--preflight <in.pdf>\n--accessibility <in.pdf>");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES") == "1") Console.WriteLine("search <query>\ncreate --title");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR") == "1") Console.WriteLine("agenda <yyyy-MM-dd>\ncreate <json>");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHELL") == "1") Console.WriteLine("search <folder>\nlist <folder>\ninfo <path>\nread <file>\nprocesses [--limit]\nservices [--limit]\nevents <log>\nregistry <key>\ndrives\nhash <file>");
        return;
    }
    if (arguments.Length > 0 && arguments[0] is "--preflight" or "--accessibility")
    {
        Console.Write(JsonSerializer.Serialize(new { valid = true, command = arguments[0] }));
        return;
    }
    if (arguments.Length > 0 && arguments[0] is "--print" or "--document-info" or "--search-text")
    {
        Console.Write("Fixture action completed");
        return;
    }
    if (arguments.Length > 0 && arguments[0].StartsWith("--", StringComparison.Ordinal))
    {
        var outputIndex = arguments[0] switch
        {
            "--merge" => 1,
            "--extract-pages" or "--delete-pages" or "--insert-blank" or "--duplicate-page" => 3,
            "--rotate-pages" or "--move-pages" => 4,
            _ => 2,
        };
        if (arguments.Length > outputIndex)
        {
            var output = arguments[outputIndex];
            if (arguments[0] is "--split" or "--to-image" or "--batch-render")
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "page-1.png"), "fixture");
            }
            else
            {
                File.WriteAllText(output, "fixture pdf");
            }
            return;
        }
    }
    if (arguments.Length == 4 && arguments[0] == "search" && arguments[2] == "--limit")
    {
        Console.Write(JsonSerializer.Serialize(new[] { new { title = "Subnet plans", snippet = arguments[1] } }));
        return;
    }
    if (arguments.Length == 5 && arguments[0] == "agenda" && arguments[3] == "--limit")
    {
        Console.Write(JsonSerializer.Serialize(new[] { new { title = "Field visit", date = arguments[1], days = arguments[2] } }));
        return;
    }
    if (arguments.Length >= 5 && arguments[0] == "search" && Path.IsPathFullyQualified(arguments[1]))
    {
        Console.Write(JsonSerializer.Serialize(new { results = new[] { new { path = Path.Combine(arguments[1], "notes.txt") } }, truncated = false }));
        return;
    }
    if (arguments.Length == 2 && arguments[0] == "create" && Environment.GetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR") == "1")
    {
        using var appointment = JsonDocument.Parse(arguments[1]);
        Console.Write(JsonSerializer.Serialize(new { id = Guid.Empty, title = appointment.RootElement.GetProperty("title").GetString() }));
        return;
    }
    if (arguments.Length >= 5 && arguments[0] == "create" && arguments[1] == "--title")
    {
        Console.Write(JsonSerializer.Serialize(new { id = 42, title = arguments[2] }));
        return;
    }
    if (arguments.Length == 5 && arguments[0] == "export" && arguments[1] == "--id" && arguments[3] == "--output")
    {
        File.WriteAllText(arguments[4], "<!doctype html><html><body><h1>Fixture note</h1></body></html>");
        Console.Write(JsonSerializer.Serialize(new { output = arguments[4] }));
        return;
    }
    if (arguments.Length >= 3 && arguments[0] == "import-image" && arguments[1] == "--path")
    {
        Console.Write(JsonSerializer.Serialize(new { id = 43, path = arguments[2] }));
        return;
    }
    if (arguments.Length == 4 && arguments[0] == "list" && Path.IsPathFullyQualified(arguments[1]))
    {
        Console.Write(JsonSerializer.Serialize(new { path = arguments[1], entries = new[] { new { name = "notes.txt", path = Path.Combine(arguments[1], "notes.txt"), isDirectory = false } }, limitReached = false }));
        return;
    }
    if (arguments.Length == 2 && arguments[0] == "info" && Path.IsPathFullyQualified(arguments[1]))
    {
        Console.Write(JsonSerializer.Serialize(new { path = arguments[1], isDirectory = false, sizeBytes = 12 }));
        return;
    }
    if (arguments.Length == 4 && arguments[0] == "read" && Path.IsPathFullyQualified(arguments[1]))
    {
        Console.Write(JsonSerializer.Serialize(new { path = arguments[1], text = "fixture text", truncated = false }));
        return;
    }
    if (arguments.Length == 3 && arguments[0] == "processes") { Console.Write(JsonSerializer.Serialize(new { processes = new[] { new { name = "fixture", pid = 42 } } })); return; }
    if (arguments.Length == 3 && arguments[0] == "services") { Console.Write(JsonSerializer.Serialize(new { services = new[] { new { name = "Fixture", status = "Running" } } })); return; }
    if (arguments.Length == 4 && arguments[0] == "events") { Console.Write(JsonSerializer.Serialize(new { events = new[] { new { id = 1000, provider = "Fixture" } } })); return; }
    if (arguments.Length == 4 && arguments[0] == "registry") { Console.Write(JsonSerializer.Serialize(new { subkeys = new[] { "Software" }, values = new[] { new { name = "Fixture", data = "Value" } } })); return; }
    if (arguments.Length == 1 && arguments[0] == "drives") { Console.Write(JsonSerializer.Serialize(new { drives = new[] { new { name = "C:\\", ready = true } } })); return; }
    if (arguments.Length == 2 && arguments[0] == "hash") { Console.Write(JsonSerializer.Serialize(new { hash = new string('a', 64) })); return; }
    Environment.ExitCode = 2;
    Console.Error.Write("Unknown fixture command");
    return;
}

var root = Path.Combine(Path.GetTempPath(), "killermcp-discovery-" + Guid.NewGuid().ToString("N"));
try
{
    var local = Path.Combine(root, "Local");
    var machine = Path.Combine(root, "Machine");
    var expected = new Dictionary<string, string>
    {
        ["killerpdf"] = Path.Combine(local, "Programs", "KillerPDF", "KillerPDF.App.exe"),
        ["killerscan"] = Path.Combine(machine, "KillerScan", "KillerScan.exe"),
        ["killershell"] = Path.Combine(local, "Programs", "KillerShell", "KillerShell.exe"),
        ["killernotes"] = Path.Combine(machine, "KillerNotes", "KillerNotes.exe"),
        ["killendar"] = Path.Combine(local, "Programs", "Killendar", "Killendar.exe"),
        ["killerbench"] = Path.Combine(local, "Programs", "KillerBench", "killerbench-cli.exe"),
    };
    foreach (var path in expected.Values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
    }

    var environment = new Dictionary<string, string?> { ["LOCALAPPDATA"] = local, ["ProgramFiles"] = machine };
    foreach (var item in expected)
    {
        Equal(item.Value, AppDiscovery.Discover(item.Key, environment));
    }

    Equal(expected.Count, AppDiscovery.DiscoverAll(environment).Count);
    environment["KILLERPDF_CLI"] = expected["killerpdf"];
    Equal(expected["killerpdf"], AppDiscovery.Discover("killerpdf", environment));
    environment["KILLERPDF_CLI"] = Path.Combine(root, "missing.exe");
    Equal<string?>(null, AppDiscovery.Discover("killerpdf", environment));
    Throws<ArgumentException>(() => AppDiscovery.Discover("unknown", environment));
    Console.WriteLine("PASS native KillerMCP app discovery");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", "1");
    var notesAdapters = await KillerNotesAdapter.CreateAsync(Environment.ProcessPath);
    var adapter = notesAdapters.FirstOrDefault(item => item.Tool.Name == "killernotes_search");
    if (adapter is null)
    {
        throw new InvalidOperationException("The KillerNotes fixture adapter was not discovered.");
    }
    Equal("killernotes_search", adapter.Tool.Name);
    Equal(16, notesAdapters.Count);
    using var valid = JsonDocument.Parse("{\"query\":\"subnet\",\"limit\":1}");
    var result = await adapter.CallAsync(valid.RootElement, CancellationToken.None);
    Equal(false, result.IsError);
    Equal("Subnet plans", JsonDocument.Parse(result.Text).RootElement[0].GetProperty("title").GetString());
    using var invalid = JsonDocument.Parse("{\"query\":\"\"}");
    Equal(true, (await adapter.CallAsync(invalid.RootElement, CancellationToken.None)).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", null);
    Console.WriteLine("PASS native KillerNotes adapter");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", "1");
    var calendarAdapter = await KillendarAdapter.CreateAsync(Environment.ProcessPath);
    if (calendarAdapter is null)
    {
        throw new InvalidOperationException("The Killendar fixture adapter was not discovered.");
    }
    Equal("killendar_agenda", calendarAdapter.Tool.Name);
    using var agenda = JsonDocument.Parse("{\"date\":\"2026-09-28\",\"days\":7,\"limit\":5}");
    var agendaResult = await calendarAdapter.CallAsync(agenda.RootElement, CancellationToken.None);
    Equal(false, agendaResult.IsError);
    Equal("Field visit", JsonDocument.Parse(agendaResult.Text).RootElement[0].GetProperty("title").GetString());
    using var badDate = JsonDocument.Parse("{\"date\":\"2026-02-30\"}");
    Equal(true, (await calendarAdapter.CallAsync(badDate.RootElement, CancellationToken.None)).IsError);
    var createAdapter = await KillendarAdapter.CreateAppointmentAsync(Environment.ProcessPath);
    if (createAdapter is null) throw new InvalidOperationException("The Killendar create tool was not discovered.");
    Equal("killendar_create_appointment", createAdapter.Tool.Name);
    using var appointment = JsonDocument.Parse("{\"title\":\"Site visit\",\"start\":\"2026-10-01T09:00\",\"end\":\"2026-10-01T10:00\"}");
    var created = await createAdapter.CallAsync(appointment.RootElement, CancellationToken.None);
    Equal(false, created.IsError);
    Equal("Site visit", JsonDocument.Parse(created.Text).RootElement.GetProperty("title").GetString());
    using var invalidAppointment = JsonDocument.Parse("{\"title\":\"Site visit\",\"start\":\"2026-10-01T10:00\",\"end\":\"2026-10-01T09:00\"}");
    Equal(true, (await createAdapter.CallAsync(invalidAppointment.RootElement, CancellationToken.None)).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", null);
    Console.WriteLine("PASS native Killendar adapter");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", "1");
    var shellAdapters = await KillerShellAdapter.CreateAsync(Environment.ProcessPath);
    Equal(10, shellAdapters.Count);
    var shellByName = shellAdapters.ToDictionary(item => item.Tool.Name);
    using var search = JsonDocument.Parse(JsonSerializer.Serialize(new { root, name = "*.txt", limit = 2 }));
    var searchResult = await shellByName["killershell_search_files"].CallAsync(search.RootElement, CancellationToken.None);
    Equal(false, searchResult.IsError);
    Equal("notes.txt", Path.GetFileName(JsonDocument.Parse(searchResult.Text).RootElement.GetProperty("results")[0].GetProperty("path").GetString()));
    using var missingSearch = JsonDocument.Parse(JsonSerializer.Serialize(new { root }));
    Equal(true, (await shellByName["killershell_search_files"].CallAsync(missingSearch.RootElement, CancellationToken.None)).IsError);
    using var shellPath = JsonDocument.Parse(JsonSerializer.Serialize(new { path = root }));
    Equal("notes.txt", JsonDocument.Parse((await shellByName["killershell_list_directory"].CallAsync(shellPath.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("entries")[0].GetProperty("name").GetString());
    Equal(false, JsonDocument.Parse((await shellByName["killershell_file_info"].CallAsync(shellPath.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("isDirectory").GetBoolean());
    Equal("fixture text", JsonDocument.Parse((await shellByName["killershell_read_text_file"].CallAsync(shellPath.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("text").GetString());
    using var emptyShell = JsonDocument.Parse("{}");
    Equal("fixture", JsonDocument.Parse((await shellByName["killershell_list_processes"].CallAsync(emptyShell.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("processes")[0].GetProperty("name").GetString());
    Equal("Fixture", JsonDocument.Parse((await shellByName["killershell_list_services"].CallAsync(emptyShell.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("services")[0].GetProperty("name").GetString());
    using var eventArgs = JsonDocument.Parse("{\"log\":\"System\",\"limit\":1}");
    Equal(1000, JsonDocument.Parse((await shellByName["killershell_read_event_log"].CallAsync(eventArgs.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("events")[0].GetProperty("id").GetInt32());
    using var registryArgs = JsonDocument.Parse("{\"path\":\"HKEY_CURRENT_USER\\\\Software\",\"limit\":1}");
    Equal("Fixture", JsonDocument.Parse((await shellByName["killershell_read_registry_key"].CallAsync(registryArgs.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("values")[0].GetProperty("name").GetString());
    Equal("C:\\", JsonDocument.Parse((await shellByName["killershell_list_drives"].CallAsync(emptyShell.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("drives")[0].GetProperty("name").GetString());
    Equal(64, JsonDocument.Parse((await shellByName["killershell_hash_file"].CallAsync(shellPath.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("hash").GetString()!.Length);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", null);
    Console.WriteLine("PASS native KillerShell adapter");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", "1");
    var scanAdapters = await KillerScanAdapter.CreateAsync(Environment.ProcessPath);
    Equal(9, scanAdapters.Count);
    var scanByName = scanAdapters.ToDictionary(item => item.Tool.Name);
    using var empty = JsonDocument.Parse("{}");
    var networkResult = await scanByName["killerscan_local_network"].CallAsync(empty.RootElement, CancellationToken.None);
    Equal("192.0.2.10", JsonDocument.Parse(networkResult.Text).RootElement.GetProperty("localIp").GetString());
    var scanResult = await scanByName["killerscan_scan_network"].CallAsync(empty.RootElement, CancellationToken.None);
    Equal(2, JsonDocument.Parse(scanResult.Text).RootElement.GetArrayLength());
    using var probe = JsonDocument.Parse("{\"target\":\"192.0.2.20\"}");
    Equal(false, (await scanByName["killerscan_probe_host"].CallAsync(probe.RootElement, CancellationToken.None)).IsError);
    using var vendor = JsonDocument.Parse("{\"mac\":\"00:00:0C:00:00:00\"}");
    Equal("Cisco Systems", JsonDocument.Parse((await scanByName["killerscan_mac_vendor"].CallAsync(vendor.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("vendor").GetString());
    using var target = JsonDocument.Parse("{\"target\":\"example.com\"}");
    Equal(false, (await scanByName["killerscan_ping"].CallAsync(target.RootElement, CancellationToken.None)).IsError);
    Equal(false, (await scanByName["killerscan_trace_route"].CallAsync(target.RootElement, CancellationToken.None)).IsError);
    Equal(false, (await scanByName["killerscan_diagnose_host"].CallAsync(target.RootElement, CancellationToken.None)).IsError);
    using var targets = JsonDocument.Parse("{\"targets\":[\"192.0.2.20\"]}");
    Equal(false, (await scanByName["killerscan_watch_hosts"].CallAsync(targets.RootElement, CancellationToken.None)).IsError);
    Equal(false, (await scanByName["killerscan_speed_test"].CallAsync(empty.RootElement, CancellationToken.None)).IsError);
    using var badTarget = JsonDocument.Parse("{\"target\":\"example.com\"}");
    Equal(true, (await scanByName["killerscan_probe_host"].CallAsync(badTarget.RootElement, CancellationToken.None)).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", null);
    Console.WriteLine("PASS native KillerScan adapters");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", "1");
    var pdfAdapters = await KillerPdfAdapter.CreateAsync(Environment.ProcessPath);
    Equal(19, pdfAdapters.Count);
    var pdfByName = pdfAdapters.ToDictionary(item => item.Tool.Name);
    var sourceOne = Path.Combine(root, "source-one.pdf");
    var sourceTwo = Path.Combine(root, "source-two.pdf");
    File.WriteAllText(sourceOne, "fixture one");
    File.WriteAllText(sourceTwo, "fixture two");
    async Task<AppCallResult> Pdf(string name, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return await pdfByName[name].CallAsync(document.RootElement, CancellationToken.None);
    }
    Equal(false, (await Pdf("killerpdf_preflight", new { path = sourceOne, profile = "print" })).IsError);
    Equal(false, (await Pdf("killerpdf_accessibility", new { path = sourceOne })).IsError);
    Equal(false, (await Pdf("killerpdf_merge", new { inputs = new[] { sourceOne, sourceTwo }, output = Path.Combine(root, "merged.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_extract_pages", new { path = sourceOne, pages = "1", output = Path.Combine(root, "extract.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_split", new { path = sourceOne, outputFolder = Path.Combine(root, "split") })).IsError);
    Equal(false, (await Pdf("killerpdf_decrypt", new { path = sourceOne, output = Path.Combine(root, "decrypted.pdf"), password = "secret" })).IsError);
    Equal(false, (await Pdf("killerpdf_render_pages", new { path = sourceOne, outputFolder = Path.Combine(root, "rendered"), pages = "1", dpi = 150, format = "png", transparent = true })).IsError);
    Equal(false, (await Pdf("killerpdf_flatten", new { path = sourceOne, output = Path.Combine(root, "flattened.pdf"), dpi = 150 })).IsError);
    Equal(false, (await Pdf("killerpdf_print", new { path = sourceOne, printer = "Fixture", pages = "1", copies = 1 })).IsError);
    Equal(false, (await Pdf("killerpdf_ocr", new { path = sourceOne, output = Path.Combine(root, "ocr.pdf"), language = "eng" })).IsError);
    Equal(false, (await Pdf("killerpdf_resave", new { path = sourceOne, output = Path.Combine(root, "resaved.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_benchmark_render", new { path = sourceOne, outputFolder = Path.Combine(root, "benchmark"), size = 1024, pageLimit = 1 })).IsError);
    Equal(false, (await Pdf("killerpdf_rotate_pages", new { path = sourceOne, pages = "1", degrees = 90, output = Path.Combine(root, "rotated.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_delete_pages", new { path = sourceOne, pages = "1", output = Path.Combine(root, "deleted.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_move_pages", new { path = sourceOne, pages = "1", position = 1, output = Path.Combine(root, "moved.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_insert_blank_page", new { path = sourceOne, position = 1, width = 612, height = 792, output = Path.Combine(root, "blank.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_duplicate_page", new { path = sourceOne, page = 1, output = Path.Combine(root, "duplicate.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_document_info", new { path = sourceOne })).IsError);
    Equal(false, (await Pdf("killerpdf_search_text", new { path = sourceOne, query = "fixture" })).IsError);
    Equal(true, (await Pdf("killerpdf_extract_pages", new { path = sourceOne, pages = "bad", output = Path.Combine(root, "bad.pdf") })).IsError);
    Equal(true, (await Pdf("killerpdf_merge", new { inputs = new[] { sourceOne, sourceTwo }, output = Path.Combine(root, "merged.pdf") })).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", null);
    Console.WriteLine("PASS native KillerPDF adapters");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", "1");
    var integrationAdapters = IntegrationAdapter.Create(Environment.ProcessPath, Environment.ProcessPath, Environment.ProcessPath, Environment.ProcessPath, Environment.ProcessPath);
    Equal(9, integrationAdapters.Count);
    var integrationByName = integrationAdapters.ToDictionary(item => item.Tool.Name);
    async Task<AppCallResult> Integration(string name, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return await integrationByName[name].CallAsync(document.RootElement, CancellationToken.None);
    }
    Equal(false, (await Integration("killerscan_save_report_note", new { limit = 5 })).IsError);
    Equal(false, (await Integration("killendar_save_agenda_note", new { date = "2026-09-28", days = 7, limit = 5 })).IsError);
    Equal(false, (await Integration("killershell_save_directory_note", new { path = root, limit = 5 })).IsError);
    Equal(false, (await Integration("killerpdf_save_pages_as_notes", new { path = sourceOne, pages = "1", dpi = 150 })).IsError);
    Equal(true, (await Integration("killer_create_pdf", new { content = "report", output = "relative.pdf" })).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", null);
    Console.WriteLine("PASS cross-app integration adapters");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", "1");
    var stateDir = Path.Combine(root, "state");
    Environment.SetEnvironmentVariable("KILLERMCP_STATE_DIR", stateDir);
    var clock = DateTimeOffset.UtcNow;
    var confirmations = new List<KillerBenchConfirmation>();
    var approve = false;
    var elevated = new List<string>();
    var benchOptions = new KillerBenchOptions
    {
        Now = () => clock,
        Wait = TimeSpan.FromSeconds(20),
        Confirm = (request, _) => { confirmations.Add(request); return Task.FromResult(approve); },
        RunElevated = async (exe, args, timeout, token) =>
        {
            elevated.Add(string.Join(" ", args));
            var info = new System.Diagnostics.ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in args) info.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(info)!;
            var stdout = await process.StandardOutput.ReadToEndAsync(token);
            var stderr = await process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            return new KillerBenchRun(process.ExitCode, stdout, stderr, false);
        },
    };
    var benchAdapters = await KillerBenchAdapter.CreateAsync(Environment.ProcessPath, Environment.ProcessPath, benchOptions);
    Equal(10, benchAdapters.Count);
    var benchByName = benchAdapters.ToDictionary(item => item.Tool.Name);
    async Task<JsonElement> Bench(string name, object arguments, bool expectError = false)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        var response = await benchByName[name].CallAsync(document.RootElement, CancellationToken.None);
        Equal(expectError, response.IsError);
        return expectError ? default : JsonDocument.Parse(response.Text).RootElement.Clone();
    }
    JsonElement Finished(JsonElement job) { Equal("completed", job.GetProperty("status").GetString()); return job.GetProperty("result"); }

    Equal("Clear", Finished(await Bench("killerbench_check", new { check = "vitals" })).GetProperty("data").GetProperty("state").GetString());
    await Bench("killerbench_check", new { check = "format_disk" }, true);
    await Bench("killerbench_check", new { check = "dns" }, true);
    Equal("Fixture", (await Bench("killerbench_code_lookup", new { kind = "update", code = "0x80070005" })).GetProperty("data").GetProperty("meaning").GetString());
    await Bench("killerbench_code_lookup", new { kind = "update", code = "0x8007&calc" }, true);

    Equal("off", (await Bench("killerbench_repair_actions", new { })).GetProperty("repairLevel").GetString());
    var offPreview = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    Equal(false, offPreview.GetProperty("canExecute").GetBoolean());
    Equal(false, offPreview.TryGetProperty("token", out _));
    Equal("Fixture preamble line.", offPreview.GetProperty("preview").GetProperty("notes").GetString());
    await Bench("killerbench_repair_preview", new { action = "bitlocker_decrypt" }, true);
    await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "bad host;" } }, true);
    await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "ok.example", extra = 1 } }, true);

    KillerBenchSettings.WriteLevel(KillerBenchRepairLevel.Safe);
    Equal(KillerBenchRepairLevel.Safe, KillerBenchSettings.ReadLevel());
    Equal(false, Finished(await Bench("killerbench_repair_preview", new { action = "dhcp_renew" })).GetProperty("canExecute").GetBoolean());
    var safePreview = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    var safeToken = safePreview.GetProperty("token").GetString()!;
    var safeRun = Finished(await Bench("killerbench_repair_execute", new { token = safeToken }));
    Equal("Completed", safeRun.GetProperty("outcome").GetString());
    Equal("dns-cache missing.example --execute", elevated.Single());
    Equal(0, confirmations.Count);
    await Bench("killerbench_repair_execute", new { token = safeToken }, true);

    var privacy = Finished(await Bench("killerbench_repair_preview", new { action = "privacy_setting", options = new { key = "camera-access", state = "off" } }));
    Equal("Completed", Finished(await Bench("killerbench_repair_execute", new { token = privacy.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Equal(1, elevated.Count);

    var expiring = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    clock = clock.AddMinutes(11);
    await Bench("killerbench_repair_execute", new { token = expiring.GetProperty("token").GetString() }, true);

    Equal(false, Finished(await Bench("killerbench_repair_preview", new { action = "system_image_repair", options = new { tool = "dism" } })).GetProperty("canExecute").GetBoolean());
    Equal("safe", Finished(await Bench("killerbench_repair_preview", new { action = "recipe_repair", options = new { recipes = new[] { "printing.spooler" } } })).GetProperty("tier").GetString());
    Equal("disruptive", Finished(await Bench("killerbench_repair_preview", new { action = "recipe_repair", options = new { recipes = new[] { "windows.component-store" } } })).GetProperty("tier").GetString());
    await Bench("killerbench_repair_preview", new { action = "recipe_repair", options = new { recipes = new[] { "unknown.recipe" } } }, true);

    KillerBenchSettings.WriteLevel(KillerBenchRepairLevel.All);
    var dism = Finished(await Bench("killerbench_repair_preview", new { action = "system_image_repair", options = new { tool = "dism" } }));
    Equal("NotApproved", Finished(await Bench("killerbench_repair_execute", new { token = dism.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Equal(1, confirmations.Count);
    Equal(1, elevated.Count);
    approve = true;
    dism = Finished(await Bench("killerbench_repair_preview", new { action = "system_image_repair", options = new { tool = "dism" } }));
    Equal("Completed", Finished(await Bench("killerbench_repair_execute", new { token = dism.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Equal("repair dism --execute", elevated[^1]);

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_EXIT", "3");
    var failing = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    Equal("Uncertain", Finished(await Bench("killerbench_repair_execute", new { token = failing.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_EXIT", null);
    Equal("dns_cache_flush", KillerBenchSettings.ReadLock()!.Action);
    Equal(false, Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } })).GetProperty("canExecute").GetBoolean());
    Equal("Recovered", Finished(await Bench("killerbench_repair_recover", new { action = "dns_cache_flush", options = new { host = "missing.example" } })).GetProperty("data").GetProperty("outcome").GetString());
    Equal(true, KillerBenchSettings.ClearLock());
    Equal(3, File.ReadAllLines(KillerBenchSettings.AuditPath).Count(line => line.Contains("\"completed\"")));

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_SLOW", "1");
    var slowAdapters = (await KillerBenchAdapter.CreateAsync(Environment.ProcessPath, Environment.ProcessPath, new KillerBenchOptions { Wait = TimeSpan.FromMilliseconds(100) })).ToDictionary(item => item.Tool.Name);
    using var scopes = JsonDocument.Parse("{\"scopes\":[\"PrintingReadiness\"]}");
    var started = JsonDocument.Parse((await slowAdapters["killerbench_scan"].CallAsync(scopes.RootElement, CancellationToken.None)).Text).RootElement.Clone();
    Equal("running", started.GetProperty("status").GetString());
    var jobId = started.GetProperty("jobId").GetString()!;
    using var poll = JsonDocument.Parse(JsonSerializer.Serialize(new { jobId }));
    using var note = JsonDocument.Parse(JsonSerializer.Serialize(new { jobId, group = "Bench" }));
    Equal(true, (await slowAdapters["killerbench_save_scan_note"].CallAsync(note.RootElement, CancellationToken.None)).IsError);
    JsonElement polled = default;
    for (var attempt = 0; attempt < 50; attempt++)
    {
        polled = JsonDocument.Parse((await slowAdapters["killerbench_job"].CallAsync(poll.RootElement, CancellationToken.None)).Text).RootElement.Clone();
        if (polled.GetProperty("status").GetString() == "completed") break;
        await Task.Delay(100);
    }
    Equal("completed", polled.GetProperty("status").GetString());
    Equal(42, JsonDocument.Parse((await slowAdapters["killerbench_save_scan_note"].CallAsync(note.RootElement, CancellationToken.None)).Text).RootElement.GetProperty("id").GetInt32());
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_SLOW", null);
    Environment.SetEnvironmentVariable("KILLERMCP_STATE_DIR", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", null);
    Console.WriteLine("PASS native KillerBench adapters and repair guardrails");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, true);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
