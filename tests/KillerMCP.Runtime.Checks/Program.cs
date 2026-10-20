using KillerMCP.Runtime;
using System.Text.Json;

// Stands in for an app that never answers, so the host checks can show it is left out.
if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_HANG") == "1") Thread.Sleep(Timeout.Infinite);

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

if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT") == "1")
{
    var shot = Environment.GetCommandLineArgs().Skip(1).ToArray();
    string? Option(string name) { int i = Array.IndexOf(shot, name); return i >= 0 && i + 1 < shot.Length ? shot[i + 1] : null; }
    if (shot.SequenceEqual(["monitors", "--json"])) { Console.Write(JsonSerializer.Serialize(new { monitors = new[] { new { index = 1, primary = true, left = 0, top = 0, width = 1920, height = 1080 } } })); return; }
    if (shot.Length == 4 && shot[0] == "windows") { Console.Write(JsonSerializer.Serialize(new { windows = new[] { new { handle = "0x10", title = "Fixture", process = "fixture", limit = shot[2] } }, limitReached = false })); return; }
    if (shot.Length > 0 && shot[0] == "capture" && int.TryParse(Option("--delay"), out var delaySeconds) && delaySeconds > 0)
    {
        // Lets the host checks hold a call open, then shows whether cancelling it ended this process.
        Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT_MARK") is { Length: > 0 } mark) File.WriteAllText(mark, "finished");
    }
    if (shot.Length > 0 && shot[0] == "capture" && Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT_ORPHAN") is { Length: > 0 } orphanPid)
    {
        // Leaves a process behind that inherits this one's output and never exits.
        var orphanInfo = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        orphanInfo.Environment["KILLERMCP_FAKE_HANG"] = "1";
        orphanInfo.Environment.Remove("KILLERMCP_FAKE_SHOT_ORPHAN");
        using var orphan = System.Diagnostics.Process.Start(orphanInfo)!;
        File.WriteAllText(orphanPid, orphan.Id.ToString());
    }
    if (shot.Length > 0 && shot[0] == "capture") { Console.Write(JsonSerializer.Serialize(new { path = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT_NULL_PATH") == "1" ? null : "C:\\fixture.png", mode = Option("--mode"), args = shot })); return; }
    if (shot.Length > 0 && shot[0] == "record") { Console.Write(JsonSerializer.Serialize(new { path = "C:\\fixture.mp4", mode = Option("--mode"), args = shot })); return; }
    if (shot.Length == 4 && shot[0] == "recent") { Console.Write(JsonSerializer.Serialize(new { folder = "C:\\Recent", items = Array.Empty<object>(), limit = shot[2] })); return; }
    if (shot.Length == 3 && shot[0] == "open") { Console.Write(JsonSerializer.Serialize(new { opened = shot[1] })); return; }
    if (shot.SequenceEqual(["settings", "--json"])) { Console.Write(JsonSerializer.Serialize(new { capture = new { format = "png" } })); return; }
}

if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_BENCH") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHELL") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SCAN") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF") == "1")
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
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SCAN_EXIT") is { Length: > 0 } scanExit) Environment.ExitCode = int.Parse(scanExit);
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
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF_HELP_STYLE") == "modern")
        {
            Console.WriteLine("KillerPDF command line usage\nExisting --command forms stay compatible.\n  killerpdf merge <out.pdf> <in1> <in2>\n  killerpdf optimize <in.pdf> <out.pdf>\n  killerpdf split <in.pdf> <outDir>\n  killerpdf export <in.pdf> <out.docx|.md>\n  killerpdf signatures <in.pdf> [--json]\n  killerpdf preflight <in.pdf> [--json]\n  killerpdf verify | /verify");
            return;
        }
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF") == "1") Console.WriteLine("  --merge <out.pdf>\n--extract-pages <in.pdf>\n--split <in.pdf>\n--decrypt <in.pdf>\n--to-image <in.pdf>\n--flatten <in.pdf>\n--print <in.pdf>\n--ocr <in.pdf>\n--batch-resave <in>\n--batch-render <in>\n--rotate-pages <in.pdf>\n--delete-pages <in.pdf>\n--move-pages <in.pdf>\n--insert-blank <in.pdf>\n--duplicate-page <in.pdf>\n--document-info <in.pdf>\n--search-text <in.pdf>\n--preflight <in.pdf>\n--accessibility <in.pdf>\n--optimize <in.pdf>\n--number-pages <in.pdf>\n--bates <outDir>\n--booklet <in.pdf>\n--nup <in.pdf>\n--auto-bookmarks <in.pdf>\n--scan-to-text <in.pdf>\n--reverse-pages <in.pdf>\n--fix-orientation <in.pdf>\n--straighten <in.pdf>\n--divide-spreads <in.pdf>\n--rasterize-region <in.pdf>\n--print-production <in.pdf>\n--signatures <in.pdf>\n--layers <in.pdf>\n--portfolio <in.pdf>\n--comments <in.pdf>\n--navigation-audit <in.pdf>\n--measurements <in.pdf>\n--separations <in.pdf>\n--compare-structure <original.pdf>\n--attachments <in.pdf>\n--recognize-fields <in.pdf>\n--set-layer <in.pdf>\n--portfolio-view <in.pdf>\n--add-attachment <in.pdf>\n--edit-attachment <in.pdf>\n--repair-links <in.pdf>\n--toc <in.pdf>\n--page-labels <in.pdf>\n--initial-view <in.pdf>\n--redact <in.pdf>\n--protect <in.pdf>\n--export <in.pdf>\n--convert-office <in.docx\n--data-merge <template.pdf>\n--replace-text <in.pdf>\n--export-form-data <in.pdf>\n--import-form-data <in.pdf>\n--xfa-to-acroform <in.pdf>\n--flatten-forms <in.pdf>\n--macro <in.pdf>\n--sign <in.pdf>\n--scan <out.pdf>\n  KillerPDF.exe --verify | /verify   verify every installed payload file");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF_SECRETS") == "1") Console.WriteLine("  --secrets-stdin    read passwords from standard input as one JSON object");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES") == "1") Console.WriteLine("search <query>\ncreate --title title (--content markdown | --content-file absolute.md)");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR") == "1") Console.WriteLine("agenda <yyyy-MM-dd>\ncreate <json>");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHELL") == "1") Console.WriteLine("search <folder>\nlist <folder>\ninfo <path>\nread <file>\nprocesses [--limit]\nservices [--limit]\nevents <log>\nregistry <key>\ndrives\nhash <file>");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT") == "1") Console.WriteLine("KillerShot command line\n  capture [options]\n  record --seconds <n> [options]\n  recent [options]\n  open <image>\n  monitors\n  windows [--limit <n>]\n  settings");
        return;
    }
    if (arguments.SequenceEqual(["--verify"]))
    {
        Console.WriteLine("PASS: verified 3 payload files.");
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
    if (arguments.Length == 2 && arguments[0].StartsWith("--", StringComparison.Ordinal) && File.Exists(arguments[1]) && Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF") == "1")
    {
        Console.Write("Signatures: 0");
        Environment.ExitCode = 3;
        return;
    }
    if (arguments.Length > 0 && arguments[0].StartsWith("--", StringComparison.Ordinal) && arguments.Contains("--json"))
    {
        Console.Write(JsonSerializer.Serialize(new { valid = true, command = arguments[0] }));
        return;
    }
    if (arguments.Length == 2 && arguments[0] is "--macro" or "--scan" && arguments[1] == "--list")
    {
        Console.Write("Fixture list");
        return;
    }
    // Records what an operation received on its command line and on standard input.
    if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF_ARGS_LOG") is { Length: > 0 } argumentsLog)
        File.WriteAllText(argumentsLog, JsonSerializer.Serialize(new { args = arguments, input = arguments.Contains("--secrets-stdin") ? new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false)).ReadToEnd() : null }));
    var stagedOutput = arguments.FirstOrDefault(argument => argument.Contains("killermcp-pdf-", StringComparison.Ordinal) && Path.GetFileName(argument).StartsWith("output", StringComparison.Ordinal));
    if (stagedOutput is not null && arguments[0] == "--macro" && Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF_RENAME") == "1")
        stagedOutput = Path.Combine(Path.GetDirectoryName(stagedOutput)!, "renamed.pdf");
    if (arguments.Length > 0 && arguments[0].StartsWith("--", StringComparison.Ordinal) && stagedOutput is not null)
    {
        foreach (var listed in new[] { "--terms", "--keep-scanned" })
        {
            var listIndex = Array.IndexOf(arguments, listed);
            if (listIndex >= 0 && (listIndex + 1 >= arguments.Length || !File.Exists(arguments[listIndex + 1]))) { Environment.ExitCode = 1; return; }
        }
        if (Path.HasExtension(stagedOutput)) File.WriteAllText(stagedOutput, "fixture output");
        else
        {
            Directory.CreateDirectory(stagedOutput);
            File.WriteAllText(Path.Combine(stagedOutput, "item-1.pdf"), "fixture");
        }
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
                if (arguments.Contains("1-2")) File.WriteAllText(Path.Combine(output, "page-2.png"), "fixture");
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
        var fileIndex = Array.IndexOf(arguments, "--content-file");
        if (fileIndex >= 0 && (fileIndex + 1 >= arguments.Length || !File.Exists(arguments[fileIndex + 1]))) { Environment.ExitCode = 1; return; }
        Console.Write(JsonSerializer.Serialize(new { id = 42, title = arguments[2], characters = fileIndex >= 0 ? File.ReadAllText(arguments[fileIndex + 1]).Length : arguments[4].Length }));
        return;
    }
    if (arguments.Length == 5 && arguments[0] == "export" && arguments[1] == "--id" && arguments[3] == "--output")
    {
        var noteHtml = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES_HTML");
        File.WriteAllText(arguments[4], noteHtml is { Length: > 0 } ? File.ReadAllText(noteHtml) : "<!doctype html><html><body><h1>Fixture note</h1></body></html>");
        Console.Write(JsonSerializer.Serialize(new { output = arguments[4] }));
        return;
    }
    if (arguments.Length >= 3 && arguments[0] == "import-image" && arguments[1] == "--path")
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES_IMPORT_FAIL") is { Length: > 0 } failing && arguments[2].Contains(failing, StringComparison.Ordinal))
        {
            Console.Error.Write("Fixture import failure");
            Environment.ExitCode = 1;
            return;
        }
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
    if (args.Contains("--pdf-review-only", StringComparer.Ordinal))
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", "1");
        await PdfReviewChecks.RunAsync(Environment.ProcessPath!, root);
        return;
    }
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
        ["killershot"] = Path.Combine(local, "Programs", "KillerShot", "KillerShot.exe"),
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

    // Servers on the local network are allowed for network paths; everything else needs the user's list.
    foreach (var localAddress in new[] { "10.1.2.3", "172.16.0.1", "172.31.255.254", "192.168.1.20", "169.254.10.10", "127.0.0.1", "::1", "fe80::1", "fd12:3456::1", "::ffff:192.168.0.5" })
        Equal(true, AgentPaths.IsLocalNetwork(System.Net.IPAddress.Parse(localAddress)));
    foreach (var remoteAddress in new[] { "203.0.113.5", "8.8.8.8", "172.32.0.1", "100.64.0.1", "2001:db8::1", "::ffff:8.8.8.8" })
        Equal(false, AgentPaths.IsLocalNetwork(System.Net.IPAddress.Parse(remoteAddress)));
    Equal<string?>(null, AgentPaths.AccessRefusal(@"\\192.168.1.20\share\a.pdf"));
    Equal<string?>(null, AgentPaths.AccessRefusal(@"\\[fe80::1]\share\a.pdf"));
    Equal(AgentPaths.NetworkMessage, AgentPaths.AccessRefusal(@"\\8.8.8.8\share\a.pdf"));
    Equal(AgentPaths.DeviceMessage, AgentPaths.AccessRefusal(@"\\?\GLOBALROOT\Device\HarddiskVolume1\a.pdf"));
    Equal<string?>(null, AgentPaths.AccessRefusal(Path.Combine(root, "a.pdf")));
    Console.WriteLine("PASS network path rule");

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
    using var longNote = JsonDocument.Parse(JsonSerializer.Serialize(new { title = "Long", content = new string('x', 40000) }));
    var longCreated = await notesAdapters.First(item => item.Tool.Name == "killernotes_create").CallAsync(longNote.RootElement, CancellationToken.None);
    Equal(false, longCreated.IsError);
    Equal(40000, JsonDocument.Parse(longCreated.Text).RootElement.GetProperty("characters").GetInt32());
    using var quotedNote = JsonDocument.Parse(JsonSerializer.Serialize(new { title = "Quoted", content = new string('"', 20000) }));
    var quotedCreated = await notesAdapters.First(item => item.Tool.Name == "killernotes_create").CallAsync(quotedNote.RootElement, CancellationToken.None);
    Equal(false, quotedCreated.IsError);
    Equal(20000, JsonDocument.Parse(quotedCreated.Text).RootElement.GetProperty("characters").GetInt32());
    using var flagTitle = JsonDocument.Parse(JsonSerializer.Serialize(new { title = "--content", content = new string('x', 40000) }));
    var flagTitleCreated = await notesAdapters.First(item => item.Tool.Name == "killernotes_create").CallAsync(flagTitle.RootElement, CancellationToken.None);
    Equal(false, flagTitleCreated.IsError);
    Equal(40000, JsonDocument.Parse(flagTitleCreated.Text).RootElement.GetProperty("characters").GetInt32());
    var noteTemp = Path.Combine(root, "cancelled-note-temp");
    Directory.CreateDirectory(noteTemp);
    var previousTemp = Environment.GetEnvironmentVariable("TEMP");
    var previousTmp = Environment.GetEnvironmentVariable("TMP");
    try
    {
        Environment.SetEnvironmentVariable("TEMP", noteTemp);
        Environment.SetEnvironmentVariable("TMP", noteTemp);
        Equal(Path.GetFullPath(noteTemp), Path.TrimEndingDirectorySeparator(Path.GetTempPath()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await notesAdapters.First(item => item.Tool.Name == "killernotes_create").CallAsync(longNote.RootElement, cancelled.Token); }
        catch (OperationCanceledException) { }
        Equal(0, Directory.GetFileSystemEntries(noteTemp).Length);
    }
    finally
    {
        Environment.SetEnvironmentVariable("TEMP", previousTemp);
        Environment.SetEnvironmentVariable("TMP", previousTmp);
    }
    using var textId = JsonDocument.Parse("{\"id\":\"5\"}");
    Equal(true, (await notesAdapters.First(item => item.Tool.Name == "killernotes_get").CallAsync(textId.RootElement, CancellationToken.None)).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", null);
    Console.WriteLine("PASS native KillerNotes adapter");
    if (args.Contains("--notes-only", StringComparer.Ordinal)) return;

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
    // A MAC address has a fixed shape, and a vendor lookup that fails is an error even if it printed something.
    foreach (var mac in new[] { "00:00:0C:00:00:00 /speedtest", "0x0000.0C00.0000", "00:00:0C:00:00:00:00", "zz00:0C:00:00:00:00", "00:00:0C:00:00:00\n" })
    {
        using var badMac = JsonDocument.Parse(JsonSerializer.Serialize(new { mac }));
        Equal(true, (await scanByName["killerscan_mac_vendor"].CallAsync(badMac.RootElement, CancellationToken.None)).IsError);
    }
    foreach (var mac in new[] { "00-00-0C-00-00-00", "00000C000000", "0000.0C00.0000" })
    {
        using var goodMac = JsonDocument.Parse(JsonSerializer.Serialize(new { mac }));
        Equal(false, (await scanByName["killerscan_mac_vendor"].CallAsync(goodMac.RootElement, CancellationToken.None)).IsError);
    }
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN_EXIT", "4");
    Equal(true, (await scanByName["killerscan_mac_vendor"].CallAsync(vendor.RootElement, CancellationToken.None)).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN_EXIT", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", null);
    Console.WriteLine("PASS native KillerScan adapters");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", "1");
    var pdfAdapters = await KillerPdfAdapter.CreateAsync(Environment.ProcessPath);
    Equal(70, pdfAdapters.Count);
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
    foreach (var template in new[] { "../escaped", "..\\escaped", "C:\\escaped", "sub/name", ".." })
        Equal(true, (await Pdf("killerpdf_split", new { path = sourceOne, outputFolder = Path.Combine(root, "unsafe-split"), nameTemplate = template })).IsError);
    Equal(false, (await Pdf("killerpdf_accessibility", new { path = sourceOne })).IsError);
    var verified = await Pdf("killerpdf_verify_install", new { });
    Equal(false, verified.IsError);
    Equal(true, JsonDocument.Parse(verified.Text).RootElement.GetProperty("passed").GetBoolean());
    Equal(true, (await Pdf("killerpdf_verify_install", new { path = sourceOne })).IsError);
    Equal(false, (await Pdf("killerpdf_merge", new { inputs = new[] { sourceOne, sourceTwo }, output = Path.Combine(root, "merged.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_extract_pages", new { path = sourceOne, pages = "1", output = Path.Combine(root, "extract.pdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_split", new { path = sourceOne, outputFolder = Path.Combine(root, "split") })).IsError);
    Equal(false, (await Pdf("killerpdf_decrypt", new { path = sourceOne, output = Path.Combine(root, "decrypted.pdf"), password = "secret" })).IsError);
    // A KillerPDF that accepts passwords on standard input gets them there, never on its command line,
    // where other programs and process logs can read them. Text that happens to read like a password
    // option stays an ordinary value, and a KillerPDF without the option still gets passwords as before.
    var argumentsLog = Path.Combine(root, "pdf-arguments.json");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_ARGS_LOG", argumentsLog);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_SECRETS", "1");
    try
    {
        var secretAdapters = (await KillerPdfAdapter.CreateAsync(Environment.ProcessPath)).ToDictionary(item => item.Tool.Name);
        async Task<JsonElement> Logged(string tool, object arguments)
        {
            using var request = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            Equal(false, (await secretAdapters[tool].CallAsync(request.RootElement, CancellationToken.None)).IsError);
            return JsonDocument.Parse(File.ReadAllText(argumentsLog)).RootElement.Clone();
        }
        var decrypted = await Logged("killerpdf_decrypt", new { path = sourceOne, output = Path.Combine(root, "stdin-decrypted.pdf"), password = "s3cret é" });
        var decryptArguments = decrypted.GetProperty("args").EnumerateArray().Select(item => item.GetString()).ToArray();
        Equal(false, decryptArguments.Contains("s3cret é") || decryptArguments.Contains("--password"));
        Equal(true, decryptArguments.Contains("--secrets-stdin"));
        Equal("s3cret é", JsonDocument.Parse(decrypted.GetProperty("input").GetString()!).RootElement.GetProperty("password").GetString());
        var protectedCopy = await Logged("killerpdf_protect", new { path = sourceOne, output = Path.Combine(root, "stdin-protected.pdf"), userPassword = "open", ownerPassword = "owner" });
        var protectSecrets = JsonDocument.Parse(protectedCopy.GetProperty("input").GetString()!).RootElement;
        Equal("open", protectSecrets.GetProperty("user-password").GetString());
        Equal("owner", protectSecrets.GetProperty("owner-password").GetString());
        Equal(false, protectedCopy.GetProperty("args").EnumerateArray().Any(item => item.GetString() is "open" or "owner"));
        var redacted = await Logged("killerpdf_redact", new { path = sourceOne, output = Path.Combine(root, "stdin-redacted.pdf"), text = "--password" });
        var redactArguments = redacted.GetProperty("args").EnumerateArray().Select(item => item.GetString()).ToArray();
        Equal("--password", redactArguments[Array.IndexOf(redactArguments, "--text") + 1]);
        Equal(false, redactArguments.Contains("--secrets-stdin"));
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_SECRETS", null);
        var olderAdapters = (await KillerPdfAdapter.CreateAsync(Environment.ProcessPath)).ToDictionary(item => item.Tool.Name);
        using var olderRequest = JsonDocument.Parse(JsonSerializer.Serialize(new { path = sourceOne, output = Path.Combine(root, "older-decrypted.pdf"), password = "older" }));
        Equal(false, (await olderAdapters["killerpdf_decrypt"].CallAsync(olderRequest.RootElement, CancellationToken.None)).IsError);
        var olderArguments = JsonDocument.Parse(File.ReadAllText(argumentsLog)).RootElement.GetProperty("args").EnumerateArray().Select(item => item.GetString()).ToArray();
        Equal("older", olderArguments[Array.IndexOf(olderArguments, "--password") + 1]);
    }
    finally
    {
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_ARGS_LOG", null);
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_SECRETS", null);
    }
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
    var attachmentFile = Path.Combine(root, "notes.txt");
    var officeFile = Path.Combine(root, "letter.docx");
    var dataFile = Path.Combine(root, "records.csv");
    var formData = Path.Combine(root, "values.fdf");
    var certificateFile = Path.Combine(root, "signer.pfx");
    var recipientFile = Path.Combine(root, "recipient.cer");
    var macroFile = Path.Combine(root, "macro.json");
    foreach (var fixture in new[] { attachmentFile, officeFile, dataFile, formData, certificateFile, recipientFile, macroFile }) File.WriteAllText(fixture, "fixture");
    foreach (var template in new[] { "../escaped", "..\\escaped", "C:\\escaped", "sub/name", ".." })
        Equal(true, (await Pdf("killerpdf_data_merge", new { path = sourceOne, data = dataFile, outputFolder = Path.Combine(root, "unsafe-merge"), name = template })).IsError);
    string NewPdf(string name) => Path.Combine(root, name + ".pdf");
    Equal(false, (await Pdf("killerpdf_split", new { path = sourceOne, outputFolder = Path.Combine(root, "split-parts"), parts = 2, nameTemplate = "{name}-{index}" })).IsError);
    Equal(false, (await Pdf("killerpdf_flatten", new { path = sourceOne, output = NewPdf("scanned-look"), colorMode = "grayscale", compression = "jpeg", jpegQuality = 70, scannedLook = "tilt,grain" })).IsError);
    Equal(false, (await Pdf("killerpdf_optimize", new { path = sourceOne, output = NewPdf("optimized"), strip = "metadata,comments", imageDpi = 150, jpegQuality = 80 })).IsError);
    Equal(false, (await Pdf("killerpdf_number_pages", new { path = sourceOne, output = NewPdf("numbered"), template = "Page {page} of {pages}", edge = "footer", align = "center", numberFormat = "decimal", fontSize = 10 })).IsError);
    Equal(false, (await Pdf("killerpdf_bates", new { inputs = new[] { sourceOne, sourceTwo }, outputFolder = Path.Combine(root, "bates"), start = 1, digits = 6, prefix = "KP" })).IsError);
    Equal(false, (await Pdf("killerpdf_booklet", new { path = sourceOne, output = NewPdf("booklet"), sheet = "letter", binding = "long", marks = true })).IsError);
    Equal(false, (await Pdf("killerpdf_nup", new { path = sourceOne, output = NewPdf("nup"), grid = "2x2", margin = 18 })).IsError);
    Equal(false, (await Pdf("killerpdf_auto_bookmarks", new { path = sourceOne, output = NewPdf("bookmarked"), depth = 2 })).IsError);
    Equal(false, (await Pdf("killerpdf_scan_to_text", new { path = sourceOne, output = NewPdf("scan-text"), language = "eng" })).IsError);
    Equal(false, (await Pdf("killerpdf_scan_to_text", new { path = sourceOne, output = NewPdf("scan-review"), reviewFolder = Path.Combine(root, "scan-review"), keepScanned = new[] { "1:2", "3:17" } })).IsError);
    Equal(true, (await Pdf("killerpdf_scan_to_text", new { path = sourceOne, output = NewPdf("scan-bad-keep"), keepScanned = new[] { "first" } })).IsError);
    Equal(true, (await Pdf("killerpdf_scan_to_text", new { path = sourceOne, output = NewPdf("scan-bad-review"), reviewFolder = root })).IsError);
    Equal(false, (await Pdf("killerpdf_reverse_pages", new { path = sourceOne, output = NewPdf("reversed"), pages = "1-2" })).IsError);
    Equal(false, (await Pdf("killerpdf_fix_orientation", new { path = sourceOne, output = NewPdf("upright") })).IsError);
    Equal(false, (await Pdf("killerpdf_straighten", new { path = sourceOne, output = NewPdf("level") })).IsError);
    Equal(false, (await Pdf("killerpdf_divide_spreads", new { path = sourceOne, output = NewPdf("divided"), direction = "vertical", splitAt = 0.5 })).IsError);
    Equal(false, (await Pdf("killerpdf_rasterize_region", new { path = sourceOne, output = NewPdf("region"), page = 1, box = "72,72,300,300", dpi = 200 })).IsError);
    foreach (var report in new[] { "killerpdf_print_production", "killerpdf_signatures", "killerpdf_layers", "killerpdf_portfolio", "killerpdf_comments", "killerpdf_navigation_audit", "killerpdf_measurements", "killerpdf_separations", "killerpdf_list_attachments", "killerpdf_recognize_fields" })
        Equal(true, JsonDocument.Parse((await Pdf(report, new { path = sourceOne })).Text).RootElement.GetProperty("valid").GetBoolean());
    Equal(false, (await Pdf("killerpdf_compare_structure", new { path = sourceOne, changed = sourceTwo })).IsError);
    Equal(false, (await Pdf("killerpdf_render_separations", new { path = sourceOne, outputFolder = Path.Combine(root, "plates"), pages = "1", dpi = 72 })).IsError);
    Equal(false, (await Pdf("killerpdf_extract_attachments", new { path = sourceOne, outputFolder = Path.Combine(root, "attachments") })).IsError);
    Equal(false, (await Pdf("killerpdf_add_form_fields", new { path = sourceOne, output = NewPdf("fields") })).IsError);
    Equal(false, (await Pdf("killerpdf_set_layer", new { path = sourceOne, output = NewPdf("layer"), layer = "Notes", name = "Review", visible = false, locked = true })).IsError);
    Equal(false, (await Pdf("killerpdf_portfolio_view", new { path = sourceOne, output = NewPdf("portfolio"), view = "tile" })).IsError);
    Equal(false, (await Pdf("killerpdf_add_attachment", new { path = sourceOne, output = NewPdf("attached"), files = new[] { attachmentFile }, relationship = "supplement" })).IsError);
    Equal(false, (await Pdf("killerpdf_edit_attachment", new { path = sourceOne, output = NewPdf("edited-attachment"), name = "notes.txt", mime = "text/plain" })).IsError);
    Equal(false, (await Pdf("killerpdf_repair_links", new { path = sourceOne, output = NewPdf("links"), @unsafe = true })).IsError);
    Equal(false, (await Pdf("killerpdf_table_of_contents", new { path = sourceOne, output = NewPdf("contents"), title = "Contents", depth = 2 })).IsError);
    Equal(false, (await Pdf("killerpdf_page_labels", new { path = sourceOne, output = NewPdf("labels"), ranges = "1:lower-roman,3:decimal" })).IsError);
    Equal(false, (await Pdf("killerpdf_initial_view", new { path = sourceOne, output = NewPdf("view"), page = 1, zoom = "fit-width", layout = "single", panel = "outlines", hideToolbar = true })).IsError);
    Equal(false, (await Pdf("killerpdf_redact", new { path = sourceOne, output = NewPdf("redacted"), terms = new[] { "Jane Doe", "555-0100" }, find = new[] { "email", "phone" }, rasterize = true })).IsError);
    Equal(false, (await Pdf("killerpdf_protect", new { path = sourceOne, output = NewPdf("protected"), userPassword = "open", ownerPassword = "owner", allow = new[] { "print" } })).IsError);
    Equal(false, (await Pdf("killerpdf_export", new { path = sourceOne, output = Path.Combine(root, "export.docx"), pages = "1" })).IsError);
    Equal(false, (await Pdf("killerpdf_convert_office", new { path = officeFile, output = NewPdf("converted") })).IsError);
    Equal(false, (await Pdf("killerpdf_data_merge", new { path = sourceOne, data = dataFile, outputFolder = Path.Combine(root, "merge"), flatten = true })).IsError);
    Equal(false, (await Pdf("killerpdf_replace_text", new { path = sourceOne, output = NewPdf("replaced"), find = "fixture", replace = "sample", wholeWord = true })).IsError);
    Equal(false, (await Pdf("killerpdf_export_form_data", new { path = sourceOne, output = Path.Combine(root, "values.xfdf") })).IsError);
    Equal(false, (await Pdf("killerpdf_import_form_data", new { path = sourceOne, data = formData, output = NewPdf("filled") })).IsError);
    Equal(false, (await Pdf("killerpdf_xfa_to_acroform", new { path = sourceOne, output = NewPdf("acroform") })).IsError);
    Equal(false, (await Pdf("killerpdf_flatten_forms", new { path = sourceOne, output = NewPdf("flat-forms") })).IsError);
    Equal(false, (await Pdf("killerpdf_run_macro", new { path = sourceOne, output = NewPdf("macro"), macro = "starter:privacy", terms = new[] { "secret" } })).IsError);
    Equal(false, (await Pdf("killerpdf_run_macro", new { path = sourceOne, output = NewPdf("macro-file"), macroFile })).IsError);
    Equal(false, (await Pdf("killerpdf_list_macros", new { })).IsError);
    Equal(false, (await Pdf("killerpdf_sign", new { path = sourceOne, output = NewPdf("signed"), pfx = certificateFile, pfxPassword = "pw", timestamp = true, appearancePage = 1, appearanceCorner = "bottom-right" })).IsError);
    Equal(false, (await Pdf("killerpdf_scan", new { output = NewPdf("scanned"), showSettings = false })).IsError);
    Equal(false, (await Pdf("killerpdf_list_scanners", new { })).IsError);
    var pictureOne = Path.Combine(root, "picture-one.png");
    var pictureTwo = Path.Combine(root, "picture-two.jpg");
    File.WriteAllText(pictureOne, "fixture");
    File.WriteAllText(pictureTwo, "fixture");
    Equal(false, (await Pdf("killerpdf_images_to_pdf", new { images = new[] { pictureOne, pictureTwo }, output = NewPdf("pictures") })).IsError);
    Equal(true, (await Pdf("killerpdf_images_to_pdf", new { images = new[] { sourceOne }, output = NewPdf("not-pictures") })).IsError);
    Equal(true, (await Pdf("killerpdf_export", new { path = sourceOne, output = NewPdf("export-as-pdf") })).IsError);
    Equal(true, (await Pdf("killerpdf_redact", new { path = sourceOne, output = NewPdf("bad-redact"), find = new[] { "passport" } })).IsError);
    Equal(true, (await Pdf("killerpdf_bates", new { inputs = new[] { Path.Combine(root, "missing.pdf") }, outputFolder = Path.Combine(root, "bad-bates") })).IsError);
    Equal(true, (await Pdf("killerpdf_compare_structure", new { path = sourceOne, changed = Path.Combine(root, "missing.pdf") })).IsError);
    Equal(true, (await Pdf("killerpdf_replace_text", new { path = sourceOne, output = NewPdf("bad-replace"), find = "--flatten", replace = "x" })).IsError);
    Equal(true, (await Pdf("killerpdf_protect", new { path = sourceOne, output = NewPdf("bad-protect"), recipients = new[] { recipientFile + ",x.cer" } })).IsError);
    Equal(true, (await Pdf("killerpdf_divide_spreads", new { path = sourceOne, output = NewPdf("bad-divide"), splitAt = 0.95 })).IsError);
    Equal(true, (await Pdf("killerpdf_convert_office", new { path = sourceOne, output = NewPdf("bad-convert") })).IsError);
    Equal(true, (await Pdf("killerpdf_extract_pages", new { path = sourceOne, pages = "bad", output = Path.Combine(root, "bad.pdf") })).IsError);
    Equal(true, (await Pdf("killerpdf_extract_pages", new { path = sourceOne, pages = "1\n", output = Path.Combine(root, "bad.pdf") })).IsError);
    Equal(true, (await Pdf("killerpdf_merge", new { inputs = new[] { sourceOne, sourceTwo }, output = Path.Combine(root, "merged.pdf") })).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", null);
    Console.WriteLine("PASS native KillerPDF adapters");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", "1");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", "1");
    var integrationAdapters = IntegrationAdapter.Create(Environment.ProcessPath, Environment.ProcessPath, Environment.ProcessPath, Environment.ProcessPath, Environment.ProcessPath);
    Equal(12, integrationAdapters.Count);
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
    // Arguments of the wrong kind get the tool's own message instead of an internal error.
    Equal(true, (await Integration("killernotes_export_pdf", new { id = "1", output = Path.Combine(root, "typed.pdf") })).IsError);
    Equal(true, (await Integration("killernotes_export_notes_pdf", new { ids = new object[] { "1" }, output = Path.Combine(root, "typed-notes.pdf") })).IsError);
    Equal(true, (await Integration("killerscan_save_report_note", new { limit = "5" })).IsError);
    Equal(true, (await Integration("killerpdf_save_pages_as_notes", new { path = sourceOne, pages = "1", dpi = "150" })).IsError);
    Equal(true, (await Integration("killerpdf_save_pages_as_notes", new { path = sourceOne, pages = "1;2" })).IsError);
    // Page lists are ASCII digits only, with nothing after the last range.
    Equal(true, (await Integration("killerpdf_save_pages_as_notes", new { path = sourceOne, pages = "1-2\n" })).IsError);
    Equal(true, (await Integration("killerpdf_save_text_as_note", new { path = sourceOne, pages = "١" })).IsError);
    // Hardened HTML holds no base or meta tag but its own, however the content nests, splits or
    // encodes them. A browser starts such a tag only at a literal less-than sign and tag name.
    var tricks = "<me<meta http-equiv=refresh content=\"0; url=file://evil/share\">ta http-equiv=\"refresh\" content=\"0; url=file://evil/nested\">"
        + "<<meta x>meta http-equiv=refresh content=\"0; url=file://evil/joined\">"
        + "<meta http-equiv=\"&#x72;efresh\" content=\"0; url=file://evil/encoded\">"
        + "<meta content=\"a>b\" http-equiv=refresh>"
        + "<svg><style><meta http-equiv=refresh content=\"0; url=file://evil/svg\"></style></svg>"
        + "<BASE\thref=file://evil/><base/href=file://evil/><p>kept</p>";
    var hardened = IntegrationAdapter.Harden(tricks);
    Equal(2, System.Text.RegularExpressions.Regex.Matches(hardened, @"<(?:meta|base)(?:[\t\n\f\r />]|\z)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
    Equal(true, hardened.Contains("<p>kept</p>", StringComparison.Ordinal));
    // Disabled tags leave no visible markup in the page, so a report's own head stays out of its PDF.
    Equal(false, IntegrationAdapter.Harden("<!doctype html><html><head><meta charset=\"utf-8\"><title>Report</title></head><body><p>x</p></body></html>").Contains("&lt;", StringComparison.Ordinal));

    // A call cancelled while its input is written still stops the app it started.
    if (OperatingSystem.IsWindows())
    {
        var pidFile = Path.Combine(root, "cancelled-input.pid");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stopped = false;
        try { await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-Command", $"$PID | Set-Content -LiteralPath '{pidFile}'; Start-Sleep -Seconds 60"], TimeSpan.FromSeconds(60), 4096, cancelled.Token, "secret"); }
        catch (OperationCanceledException) { stopped = true; }
        Equal(true, stopped);
        await Task.Delay(5000);
        if (File.Exists(pidFile))
        {
            var leftover = int.Parse(File.ReadAllText(pidFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                using var survivor = System.Diagnostics.Process.GetProcessById(leftover);
                survivor.Kill(true);
                throw new InvalidOperationException("A call cancelled while writing its input left its app running.");
            }
            catch (ArgumentException) { }
        }
        Console.WriteLine("PASS cancelled input stops the app");
    }
    // When a later page cannot be saved, the result still names the notes already created.
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES_IMPORT_FAIL", "page-2");
    var partialPages = await Integration("killerpdf_save_pages_as_notes", new { path = sourceOne, pages = "1-2" });
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES_IMPORT_FAIL", null);
    Equal(true, partialPages.IsError);
    Equal(true, partialPages.Text.Contains("\"created\"", StringComparison.Ordinal) && partialPages.Text.Contains("43", StringComparison.Ordinal));
    Equal(42, JsonDocument.Parse((await Integration("killerpdf_save_text_as_note", new { path = sourceOne, pages = "1", title = "Fixture text" })).Text).RootElement.GetProperty("id").GetInt32());
    Equal(true, (await Integration("killerpdf_save_text_as_note", new { path = sourceOne, pages = "first" })).IsError);
    Equal(42, JsonDocument.Parse((await Integration("killerpdf_save_report_as_note", new { path = sourceOne, report = "signatures" })).Text).RootElement.GetProperty("id").GetInt32());
    Equal(true, (await Integration("killerpdf_save_report_as_note", new { path = sourceOne, report = "everything" })).IsError);
    Equal(true, (await Integration("killernotes_export_notes_pdf", new { ids = Array.Empty<int>(), output = Path.Combine(root, "notes.pdf") })).IsError);
    Equal(true, (await Integration("killer_create_pdf", new { content = "report", output = "relative.pdf" })).IsError);

    // A note or report turned into a PDF must not make Edge contact anything: not a server named by
    // address, not a page a refresh or base element points at, not a frame.
    var edgeInstalled = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
        .Any(folder => File.Exists(Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe")));
    if (OperatingSystem.IsWindows() && edgeInstalled)
    {
        var trap = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        trap.Start();
        var trapPort = ((System.Net.IPEndPoint)trap.LocalEndpoint).Port;
        var contacts = 0;
        var trapLoop = Task.Run(async () =>
        {
            while (true)
            {
                try { using var trapClient = await trap.AcceptTcpClientAsync(); Interlocked.Increment(ref contacts); }
                catch (Exception trapError) when (trapError is ObjectDisposedException or System.Net.Sockets.SocketException) { return; }
            }
        });
        var trapUrl = $"http://127.0.0.1:{trapPort}";
        var hostile = $"<!doctype html><html><head><base href=\"{trapUrl}/base/\"><META HTTP-EQUIV=\"Refresh\" CONTENT=\"0; url={trapUrl}/refresh\"></head>"
            + $"<body><h1>Fixture note</h1><img src=\"{trapUrl}/image.png\"><img src=\"relative.png\"><iframe src=\"{trapUrl}/frame\"></iframe>"
            + $"<link rel=\"stylesheet\" href=\"{trapUrl}/style.css\"><script>new Image().src='{trapUrl}/script'</script></body></html>";
        var hostileNote = Path.Combine(root, "hostile-note.html");
        File.WriteAllText(hostileNote, hostile);
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES_HTML", hostileNote);
        var notePdf = Path.Combine(root, "hostile-note.pdf");
        var noteResult = await Integration("killernotes_export_pdf", new { id = 1, output = notePdf });
        var reportPdf = Path.Combine(root, "hostile-report.pdf");
        var reportResult = await Integration("killer_create_pdf", new { content = hostile, output = reportPdf });
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES_HTML", null);
        await Task.Delay(1000);
        trap.Stop();
        await trapLoop;
        if (contacts != 0) throw new InvalidOperationException($"Rendering a PDF contacted a server {contacts} time(s).");
        if (noteResult.IsError || !File.Exists(notePdf)) throw new InvalidOperationException("The note PDF was not created: " + noteResult.Text);
        if (reportResult.IsError || !File.Exists(reportPdf)) throw new InvalidOperationException("The report PDF was not created: " + reportResult.Text);
        Console.WriteLine("PASS PDF rendering stays offline");
    }
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", null);
    Console.WriteLine("PASS cross-app integration adapters");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", "1");
    var stateDir = Path.Combine(root, "state");
    Environment.SetEnvironmentVariable("KILLERMCP_STATE_DIR", stateDir);
    Environment.SetEnvironmentVariable("KILLERBENCH_STATE_DIR", Path.Combine(root, "bench-state"));
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
    // Checked values end where the value ends, not before a trailing line break.
    await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "ok.example\n" } }, true);
    await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "ok.example", extra = 1 } }, true);

    KillerBenchSettings.WriteLevel(KillerBenchRepairLevel.Safe);
    Equal(KillerBenchRepairLevel.Safe, KillerBenchSettings.ReadLevel());
    Equal(false, Finished(await Bench("killerbench_repair_preview", new { action = "dhcp_renew" })).GetProperty("canExecute").GetBoolean());
    // Anything that can write a file can write the setting, so the first repair after the setting
    // appears or changes waits for a person at the desktop, even a safe one.
    var unconfirmed = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    Equal("NotApproved", Finished(await Bench("killerbench_repair_execute", new { token = unconfirmed.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Equal(1, confirmations.Count);
    Equal(true, confirmations[0].Effects.Contains("repair setting is new or has changed", StringComparison.Ordinal));
    Equal("Safe", confirmations[0].Tier);
    Equal(0, elevated.Count);
    approve = true;
    var safePreview = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    var safeToken = safePreview.GetProperty("token").GetString()!;
    var safeRun = Finished(await Bench("killerbench_repair_execute", new { token = safeToken }));
    Equal("Completed", safeRun.GetProperty("outcome").GetString());
    Equal("dns-cache missing.example --execute", elevated.Single());
    Equal(2, confirmations.Count);
    approve = false;
    await Bench("killerbench_repair_execute", new { token = safeToken }, true);

    var privacy = Finished(await Bench("killerbench_repair_preview", new { action = "privacy_setting", options = new { key = "camera-access", state = "off" } }));
    Equal("Completed", Finished(await Bench("killerbench_repair_execute", new { token = privacy.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Equal(1, elevated.Count);
    Equal(2, confirmations.Count);

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
    Equal(3, confirmations.Count);
    Equal(1, elevated.Count);
    approve = true;
    dism = Finished(await Bench("killerbench_repair_preview", new { action = "system_image_repair", options = new { tool = "dism" } }));
    Equal("Completed", Finished(await Bench("killerbench_repair_execute", new { token = dism.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Equal("repair dism --execute", elevated[^1]);

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_EXIT", "3");
    var failing = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    Equal("Uncertain", Finished(await Bench("killerbench_repair_execute", new { token = failing.GetProperty("token").GetString() })).GetProperty("outcome").GetString());
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_EXIT", null);
    Equal(4, confirmations.Count);
    Equal("dns_cache_flush", KillerBenchSettings.ReadLock()!.Action);
    Equal("Flush one hostname from the DNS cache", KillerBenchSettings.ReadLock()!.Title);
    Equal(true, File.Exists(Path.Combine(root, "bench-state", "agent-repair-lock.json")));
    Equal(false, Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } })).GetProperty("canExecute").GetBoolean());
    Equal("Recovered", Finished(await Bench("killerbench_repair_recover", new { action = "dns_cache_flush", options = new { host = "missing.example" } })).GetProperty("data").GetProperty("outcome").GetString());
    Equal(true, KillerBenchSettings.ClearLock());
    Equal(3, File.ReadAllLines(KillerBenchSettings.AuditPath).Count(line => line.Contains("\"completed\"")));

    // A repair running in another KillerMCP host blocks this one; the hold is a file in the KillerMCP state folder.
    var otherHost = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    using (var held = KillerBenchSettings.HoldRepair())
    {
        Equal(true, held is not null);
        await Bench("killerbench_repair_execute", new { token = otherHost.GetProperty("token").GetString() }, true);
    }
    var afterOtherHost = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    Equal("Completed", Finished(await Bench("killerbench_repair_execute", new { token = afterOtherHost.GetProperty("token").GetString() })).GetProperty("outcome").GetString());

    // A repair hold that cannot be opened is an error, and later repairs in this host still run.
    Directory.CreateDirectory(KillerBenchSettings.RepairHoldPath);
    var unopened = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    await Bench("killerbench_repair_execute", new { token = unopened.GetProperty("token").GetString() }, true);
    Directory.Delete(KillerBenchSettings.RepairHoldPath);
    var afterUnopened = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    Equal("Completed", Finished(await Bench("killerbench_repair_execute", new { token = afterUnopened.GetProperty("token").GetString() })).GetProperty("outcome").GetString());

    // A repair lock that cannot be saved still blocks repairs in this host, and the result says so.
    Directory.CreateDirectory(KillerBenchSettings.LockPath);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_EXIT", "3");
    var unsaved = Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } }));
    var unsavedRun = Finished(await Bench("killerbench_repair_execute", new { token = unsaved.GetProperty("token").GetString() }));
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH_EXIT", null);
    Equal("Uncertain", unsavedRun.GetProperty("outcome").GetString());
    Equal(true, unsavedRun.GetProperty("detail").GetString()!.Contains("could not be saved", StringComparison.Ordinal));
    Equal(false, Finished(await Bench("killerbench_repair_preview", new { action = "dns_cache_flush", options = new { host = "missing.example" } })).GetProperty("canExecute").GetBoolean());
    Directory.Delete(KillerBenchSettings.LockPath);

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
    Environment.SetEnvironmentVariable("KILLERBENCH_STATE_DIR", null);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", null);
    Console.WriteLine("PASS native KillerBench adapters and repair guardrails");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT", "1");
    var shotAdapters = await KillerShotAdapter.CreateAsync(Environment.ProcessPath);
    Equal(10, shotAdapters.Count);
    var shotByName = shotAdapters.ToDictionary(item => item.Tool.Name);
    async Task<AppCallResult> Shot(string name, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return await shotByName[name].CallAsync(document.RootElement, CancellationToken.None);
    }
    string[] ShotArgs(AppCallResult call) => JsonDocument.Parse(call.Text).RootElement.GetProperty("args").EnumerateArray().Select(item => item.GetString()!).ToArray();
    Equal(1, JsonDocument.Parse((await Shot("killershot_list_monitors", new { })).Text).RootElement.GetProperty("monitors").GetArrayLength());
    Equal(true, (await Shot("killershot_list_monitors", new { limit = 1 })).IsError);
    Equal("7", JsonDocument.Parse((await Shot("killershot_list_windows", new { limit = 7 })).Text).RootElement.GetProperty("windows")[0].GetProperty("limit").GetString());
    var monitorShot = await Shot("killershot_capture_monitor", new { monitor = "2" });
    Equal(false, monitorShot.IsError);
    var monitorArgs = ShotArgs(monitorShot);
    Equal(true, monitorArgs.SequenceEqual(["capture", "--mode", "monitor", "--monitor", "2", "--no-clipboard", "--save", "--no-shutter", "--json"]));
    Equal(true, (await Shot("killershot_capture_monitor", new { monitor = "0" })).IsError);
    var allArgs = ShotArgs(await Shot("killershot_capture_all", new { clipboard = true, cursor = false, delay = 2, output = root }));
    Equal(true, allArgs.SequenceEqual(["capture", "--mode", "all", "--out", root, "--clipboard", "--no-cursor", "--delay", "2", "--save", "--no-shutter", "--json"]));
    Equal(true, (await Shot("killershot_capture_all", new { output = "relative.png" })).IsError);
    Equal(true, ShotArgs(await Shot("killershot_capture_window", new { window = "Fixture" })).Contains("Fixture"));
    Equal(true, (await Shot("killershot_capture_window", new { })).IsError);
    Equal(true, ShotArgs(await Shot("killershot_capture_region", new { x = -10, y = 0, width = 640, height = 360 })).Contains("-10,0,640,360"));
    Equal(true, (await Shot("killershot_capture_region", new { x = 0, y = 0, width = 640 })).IsError);
    var recordArgs = ShotArgs(await Shot("killershot_record", new { mode = "monitor", monitor = "primary", seconds = 5, format = "gif" }));
    Equal(true, recordArgs.SequenceEqual(["record", "--mode", "monitor", "--seconds", "5", "--monitor", "primary", "--format", "gif", "--json"]));
    Equal(true, (await Shot("killershot_record", new { mode = "region", seconds = 5 })).IsError);
    Equal(true, (await Shot("killershot_record", new { mode = "all", monitor = "1", seconds = 5 })).IsError);
    Equal(true, (await Shot("killershot_record", new { mode = "all", seconds = 301 })).IsError);
    Equal(true, (await Shot("killershot_record", new { mode = "all", seconds = 3, sound = "loud" })).IsError);
    Equal("5", JsonDocument.Parse((await Shot("killershot_recent", new { limit = 5 })).Text).RootElement.GetProperty("limit").GetString());
    var image = Path.Combine(root, "shot.png");
    File.WriteAllText(image, "fixture");
    Equal(image, JsonDocument.Parse((await Shot("killershot_open_in_editor", new { path = image })).Text).RootElement.GetProperty("opened").GetString());
    Equal(true, (await Shot("killershot_open_in_editor", new { path = Path.Combine(root, "missing.png") })).IsError);
    Equal(false, (await Shot("killershot_settings", new { })).IsError);
    // An app that exits while a process it started still holds its output must not hold the call
    // until the timeout; the result comes back shortly after the app exits.
    var orphanFile = Path.Combine(root, "orphan.pid");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_ORPHAN", orphanFile);
    var orphanStarted = System.Diagnostics.Stopwatch.StartNew();
    var orphanResult = await Shot("killershot_capture_all", new { });
    var orphanSeconds = orphanStarted.Elapsed.TotalSeconds;
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_ORPHAN", null);
    try { System.Diagnostics.Process.GetProcessById(int.Parse(File.ReadAllText(orphanFile))).Kill(); } catch (Exception orphanError) when (orphanError is ArgumentException or InvalidOperationException or FormatException or IOException) { }
    if (orphanResult.IsError || orphanSeconds > 10) throw new InvalidOperationException($"A capture whose app left a process holding its output took {orphanSeconds:0.0} s: {orphanResult.Text}");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT", null);
    Console.WriteLine("PASS native KillerShot adapters");
    await KillerShotReviewChecks.RunAsync(Environment.ProcessPath!, root);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", "1");
    try { await PdfReviewChecks.RunAsync(Environment.ProcessPath!, root); }
    finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", null); }
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
