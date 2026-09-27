using KillerMCP.Runtime;
using System.Text.Json;

if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHELL") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_BENCH") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SCAN") == "1" || Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF") == "1")
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
    if (arguments.Length > 0 && arguments[0] is "/scan" or "/probe")
    {
        Console.Write("[{\"ip\":\"192.0.2.10\"}]");
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
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES") == "1") Console.WriteLine("search <query>");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR") == "1") Console.WriteLine("agenda <yyyy-MM-dd>");
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHELL") == "1") Console.WriteLine("search <folder>\nlist <folder>\ninfo <path>\nread <file>");
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
    if (arguments.Length == 2 && arguments[0] is "device-code" or "win32-code")
    {
        Console.Write(JsonSerializer.Serialize(new { code = arguments[1], name = arguments[0] == "device-code" ? "Device problem" : "Windows error" }));
        return;
    }
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
        ["killerbench"] = Path.Combine(machine, "KillerBench", "killerbench-cli.exe"),
        ["killernotes"] = Path.Combine(machine, "KillerNotes", "KillerNotes.exe"),
        ["killendar"] = Path.Combine(local, "Programs", "Killendar", "Killendar.exe"),
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
    var adapter = await KillerNotesAdapter.CreateAsync(Environment.ProcessPath);
    if (adapter is null)
    {
        throw new InvalidOperationException("The KillerNotes fixture adapter was not discovered.");
    }
    Equal("killernotes_search", adapter.Tool.Name);
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
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", null);
    Console.WriteLine("PASS native Killendar adapter");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", "1");
    var shellAdapters = await KillerShellAdapter.CreateAsync(Environment.ProcessPath);
    Equal(4, shellAdapters.Count);
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
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHELL", null);
    Console.WriteLine("PASS native KillerShell adapter");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", "1");
    var benchAdapters = KillerBenchAdapter.Create(Environment.ProcessPath);
    Equal(2, benchAdapters.Count);
    using var code = JsonDocument.Parse("{\"code\":\"0x1F\"}");
    var codeResult = await benchAdapters[0].CallAsync(code.RootElement, CancellationToken.None);
    Equal(false, codeResult.IsError);
    Equal("0x1F", JsonDocument.Parse(codeResult.Text).RootElement.GetProperty("code").GetString());
    using var badCode = JsonDocument.Parse("{\"code\":\"4294967296\"}");
    Equal(true, (await benchAdapters[1].CallAsync(badCode.RootElement, CancellationToken.None)).IsError);
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", null);
    Console.WriteLine("PASS native KillerBench adapters");

    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SCAN", "1");
    var scanAdapters = await KillerScanAdapter.CreateAsync(Environment.ProcessPath);
    Equal(9, scanAdapters.Count);
    var scanByName = scanAdapters.ToDictionary(item => item.Tool.Name);
    using var empty = JsonDocument.Parse("{}");
    var networkResult = await scanByName["killerscan_local_network"].CallAsync(empty.RootElement, CancellationToken.None);
    Equal("192.0.2.10", JsonDocument.Parse(networkResult.Text).RootElement.GetProperty("localIp").GetString());
    var scanResult = await scanByName["killerscan_scan_network"].CallAsync(empty.RootElement, CancellationToken.None);
    Equal(1, JsonDocument.Parse(scanResult.Text).RootElement.GetArrayLength());
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
