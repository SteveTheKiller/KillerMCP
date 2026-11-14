using System.Text.Json;
using KillerMCP;

internal static class ReleaseGapReviewChecks
{
    private const string Mode = "KILLERMCP_RELEASE_GAP_FIXTURE";
    private const string Log = "KILLERMCP_RELEASE_GAP_LOG";

    public static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable(Mode) is not { } mode) return false;
        if (args.SequenceEqual(["--help"]))
        {
            Console.Write("search <folder> [--max-seconds]\ncreate --title title --content-file path\n" + (mode == "closed" ? "app-pipe-v1" : ""));
            return true;
        }
        if (args.SequenceEqual(["help"])) { Console.Write("full-scan"); return true; }
        if (args.SequenceEqual(["capabilities"])) { Environment.ExitCode = 2; return true; }
        if (args[0] == "full-scan") { Console.Write("{\"outcome\":\"Completed\",\"results\":[]}"); return true; }
        if (args[0] == "search")
        {
            int filter = Array.IndexOf(args, "--content");
            var matches = Directory.EnumerateFiles(args[1], "*.json", SearchOption.AllDirectories)
                .Where(path => filter < 0 || File.ReadAllText(path).Contains(args[filter + 1], StringComparison.Ordinal));
            Console.Write(JsonSerializer.Serialize(new { results = matches.Select(path => new { path }).ToArray() }));
            return true;
        }
        if (args[0] == "create")
        {
            File.WriteAllText(Environment.GetEnvironmentVariable(Log)!, "dispatched");
            Thread.Sleep(5000);
            Console.Write("{\"id\":42}");
            return true;
        }
        throw new InvalidOperationException("Unsupported release-gap fixture command.");
    }

    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-release-gaps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var settings = KillerBenchSettings.UseTestFolders(Path.Combine(root, "mcp"), Path.Combine(root, "bench"));
        Environment.SetEnvironmentVariable(Mode, "search");
        try
        {
            Directory.CreateDirectory(KillerBenchSettings.Folder);
            File.WriteAllText(Path.Combine(KillerBenchSettings.Folder, "fixture.json"), "harmless-secret-marker");
            var tools = await KillerShellAdapter.CreateAsync(Environment.ProcessPath!);
            var search = tools.Single(item => item.Tool.Name == "killershell_search_files");
            var blocked = await search.CallAsync(JsonSerializer.SerializeToElement(new { root, content = "harmless-secret-marker" }), CancellationToken.None);
            if (!blocked.IsError || blocked.Text.Contains("fixture.json")) throw new InvalidOperationException("Recursive content search exposed a protected settings marker: " + blocked.Text);
            Console.WriteLine("PASS recursive search refuses a protected settings ancestor (1 selected regression)");
            string ordinary = Path.Combine(root, "ordinary");
            Directory.CreateDirectory(ordinary);
            File.WriteAllText(Path.Combine(ordinary, "visible.json"), "ordinary-marker");
            var allowed = await search.CallAsync(JsonSerializer.SerializeToElement(new { root = ordinary, content = "ordinary-marker" }), CancellationToken.None);
            if (allowed.IsError || !allowed.Text.Contains("visible.json")) throw new InvalidOperationException("Ordinary content search was refused: " + allowed.Text);
            Console.WriteLine("PASS ordinary recursive content search stays available (1 selected regression)");

            Environment.SetEnvironmentVariable(Mode, "write");
            NotesContent.Remember(Environment.ProcessPath!, "create --content-file");
            var bench = (await KillerBenchAdapter.CreateAsync(Environment.ProcessPath, Environment.ProcessPath,
                new KillerBenchOptions { Wait = TimeSpan.FromMilliseconds(1) })).ToDictionary(item => item.Tool.Name);
            var job = await bench["killerbench_scan"].CallAsync(JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
            string id = JsonDocument.Parse(job.Text).RootElement.GetProperty("jobId").GetString()!;
            for (int attempt = 0; attempt < 500; attempt++)
            {
                var status = await bench["killerbench_job"].CallAsync(JsonSerializer.SerializeToElement(new { jobId = id }), CancellationToken.None);
                if (JsonDocument.Parse(status.Text).RootElement.GetProperty("status").GetString() != "running") break;
                await Task.Delay(20);
            }
            string log = Path.Combine(root, "note-dispatched.txt");
            Environment.SetEnvironmentVariable(Log, log);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var call = bench["killerbench_save_scan_note"].CallAsync(JsonSerializer.SerializeToElement(new { jobId = id }), stop.Token);
            while (!File.Exists(log) && !call.IsCompleted) await Task.Delay(10);
            if (!File.Exists(log)) throw new InvalidOperationException("Notes write was never dispatched: " + (await call).Text);
            stop.Cancel();
            AppCallResult result;
            try { result = await call; }
            catch (OperationCanceledException) { throw new InvalidOperationException("Canceled Bench note write lost its uncertain-outcome warning."); }
            if (!result.IsError || !result.Text.Contains("outcome unknown", StringComparison.OrdinalIgnoreCase) || !result.Text.Contains("retry", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Canceled Bench note write must warn before retrying: " + result.Text);
            Console.WriteLine("PASS canceled Bench note write reports unknown outcome (1 selected regression)");

            File.Delete(log);
            NotesContent.Remember(Environment.ProcessPath!, "create --content-file app-pipe-v1");
            using var pipeNames = NotesContent.WithPipeNames(_ => "KillerMCP-closed-test-" + Guid.NewGuid().ToString("N"));
            var closed = await bench["killerbench_save_scan_note"].CallAsync(JsonSerializer.SerializeToElement(new { jobId = id }), CancellationToken.None);
            if (!closed.IsError || !closed.Text.Contains("Open and unlock KillerNotes", StringComparison.Ordinal) || File.Exists(log))
                throw new InvalidOperationException("Bench note write must preflight the closed vault: " + closed.Text);
            Console.WriteLine("PASS closed Notes refuses Bench note dispatch (1 selected regression)");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Mode, null);
            Environment.SetEnvironmentVariable(Log, null);
            NotesContent.Remember(Environment.ProcessPath!, "create --content-file");
            Directory.Delete(root, true);
        }
    }
}
