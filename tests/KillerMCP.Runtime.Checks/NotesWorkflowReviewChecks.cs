using System.Text.Json;
using KillerMCP;

internal static class NotesWorkflowReviewChecks
{
    public static async Task RunAsync()
    {
        await OversizeAsync(); await PageNumberAsync(); await SelectedPageCapAsync();
        await RenderedPageCapAsync(); await UnknownOutcomeAsync(); await LargePdfAsync(); await ClosedAsync();
    }

    private static async Task<AppAdapter> PageToolAsync()
    {
        await KillerNotesAdapter.CreateAsync(Environment.ProcessPath);
        return IntegrationAdapter.Create(Environment.ProcessPath, null, null, null, Environment.ProcessPath).Single(item => item.Tool.Name == "killerpdf_save_pages_as_notes");
    }

    public static bool Serve(string[] args)
    {
        string? mode = Environment.GetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW");
        if (mode is null) return false;
        if (args.SequenceEqual(["--help"])) { Console.Write("create --title title --content-file path\n--export <in.pdf>\n" + (mode == "closed" ? "app-pipe-v1" : "")); return true; }
        if (args.Length > 2 && args[0] == "--to-image")
        {
            if (Environment.GetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG") is { } renderLog) File.AppendAllText(renderLog, "render\n");
            Directory.CreateDirectory(args[2]);
            if (mode == "overrender")
            {
                for (int page = 1; page <= 101; page++) File.WriteAllText(Path.Combine(args[2], $"fixture-page-{page:D3}.png"), "fixture");
                return true;
            }
            File.WriteAllText(Path.Combine(args[2], "fixture-page-005.png"), "fixture");
            string second = Path.Combine(args[2], "fixture-page-006.png");
            using var image = File.Create(second);
            image.SetLength(mode == "oversize" ? 32L * 1024 * 1024 + 1 : 1);
            Console.Write("rendered"); return true;
        }
        if (args.Length > 0 && args[0] == "import-image")
        {
            if (Environment.GetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG") is { } log) File.AppendAllText(log, args[2] + "\n");
            if (mode == "cancel") Thread.Sleep(5000);
            if (new FileInfo(args[2]).Length > 32L * 1024 * 1024 || mode == "fail-page" && args[2].EndsWith("006.png", StringComparison.Ordinal)) { Console.Error.Write("Image import refused"); Environment.ExitCode = 1; }
            else Console.Write("{\"id\":7}");
            return true;
        }
        throw new InvalidOperationException("Unexpected workflow fixture command: " + string.Join(" ", args));
    }

    public static async Task OversizeAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-notes-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.pdf"), log = Path.Combine(root, "imports.txt");
        File.WriteAllText(source, "fixture");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", "oversize");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", log);
        try
        {
            var tool = await PageToolAsync();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "5-6" }), CancellationToken.None);
            if (!result.IsError || File.Exists(log) && File.ReadAllLines(log).Any(line => line != "render")) throw new InvalidOperationException("Every PNG must be checked before the first note is created: " + result.Text);
            Console.WriteLine("PASS oversized PNG causes no partial note import (1 selected regression)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", null);
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", null);
            Directory.Delete(root, true);
        }
    }

    public static async Task PageNumberAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-notes-page-number-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.pdf");
        File.WriteAllText(source, "fixture");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", "fail-page");
        try
        {
            var tool = await PageToolAsync();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "5-6" }), CancellationToken.None);
            using var response = JsonDocument.Parse(result.Text);
            if (!result.IsError || response.RootElement.GetProperty("failedAtPage").GetInt32() != 6 || response.RootElement.GetProperty("created").GetArrayLength() != 1)
                throw new InvalidOperationException("Failure must name the real source page and preserve created-note IDs: " + result.Text);
            Console.WriteLine("PASS import failure names source page 6 (1 selected regression)");
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", null); Directory.Delete(root, true); }
    }

    public static async Task SelectedPageCapAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-notes-page-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.pdf"), log = Path.Combine(root, "calls.txt");
        File.WriteAllText(source, "fixture");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", "selection");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", log);
        try
        {
            var tool = await PageToolAsync();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "1-101" }), CancellationToken.None);
            if (!result.IsError || File.Exists(log)) throw new InvalidOperationException("Over 100 selected pages must be refused before rendering: " + result.Text);
            Console.WriteLine("PASS more than 100 selected pages refused before render (1 selected regression)");
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", null); Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", null); Directory.Delete(root, true); }
    }

    public static async Task RenderedPageCapAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-notes-render-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.pdf"), log = Path.Combine(root, "calls.txt");
        File.WriteAllText(source, "fixture");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", "overrender");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", log);
        try
        {
            var tool = await PageToolAsync();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "1-100" }), CancellationToken.None);
            if (!result.IsError || File.ReadAllLines(log).Any(line => line != "render")) throw new InvalidOperationException("Over 100 rendered pages must be refused before importing any notes: " + result.Text);
            Console.WriteLine("PASS more than 100 rendered pages refused before import (1 selected regression)");
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", null); Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", null); Directory.Delete(root, true); }
    }

    public static async Task UnknownOutcomeAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-notes-workflow-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.pdf"), log = Path.Combine(root, "calls.txt");
        File.WriteAllText(source, "fixture");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", "cancel");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", log);
        try
        {
            var tool = await PageToolAsync();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var call = tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "5-6" }), stop.Token);
            while (SharedLineCount(log) < 2 && !call.IsCompleted) await Task.Delay(10);
            if (call.IsCompleted) throw new InvalidOperationException("The fake import was not held open.");
            stop.Cancel();
            AppCallResult result;
            try { result = await call; }
            catch (OperationCanceledException) { throw new InvalidOperationException("Cross-app note import lost its unknown-outcome warning."); }
            if (!result.IsError || !result.Text.Contains("outcome unknown", StringComparison.OrdinalIgnoreCase) || !result.Text.Contains("retry", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cross-app import must warn before retrying: " + result.Text);
            Console.WriteLine("PASS canceled cross-app Notes import reports unknown outcome (1 selected regression)");
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", null); Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", null); await DeleteWhenReleasedAsync(root); }
    }

    // The fake KillerNotes appends to the log while the check polls it, so reads share the file and a busy moment counts as not yet.
    private static int SharedLineCount(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            int lines = 0;
            while (reader.ReadLine() is not null) lines++;
            return lines;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or IOException) { return 0; }
    }

    // A canceled fake can hold its log for a moment after the call returns.
    private static async Task DeleteWhenReleasedAsync(string root)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); return; }
            catch (IOException) when (attempt < 50) { await Task.Delay(100); }
        }
    }

    public static async Task LargePdfAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-notes-large-pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.pdf");
        using (var input = File.Create(source)) input.SetLength(65L * 1024 * 1024);
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", "selection");
        try
        {
            var tool = await PageToolAsync();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "5-6" }), CancellationToken.None);
            if (result.IsError) throw new InvalidOperationException("Cross-app PDF input cap must match the 512 MiB adapter cap: " + result.Text);
            using (var input = File.OpenWrite(source)) input.SetLength(513L * 1024 * 1024);
            var oversized = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "5-6" }), CancellationToken.None);
            if (!oversized.IsError || !oversized.Text.Contains("512 MiB", StringComparison.Ordinal)) throw new InvalidOperationException("The cross-app PDF cap was not enforced: " + oversized.Text);
            Console.WriteLine("PASS cross-app PDF input size boundaries (2 selected cases)");
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", null); Directory.Delete(root, true); }
    }

    public static async Task ClosedAsync()
    {
        using var pipeNames = NotesContent.WithPipeNames(_ => "KillerMCP-workflow-closed-test-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(Path.GetTempPath(), "killermcp-notes-workflow-closed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.pdf"), log = Path.Combine(root, "calls.txt");
        File.WriteAllText(source, "fixture");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", "closed");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", log);
        try
        {
            var tool = await PageToolAsync();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, pages = "5-6" }), CancellationToken.None);
            if (!result.IsError || !result.Text.Contains("Open and unlock KillerNotes", StringComparison.Ordinal) || File.Exists(log))
                throw new InvalidOperationException("Closed Notes must stop the page workflow before rendering or import: " + result.Text);
            Console.WriteLine("PASS closed Notes preflight stops cross-app workflow (1 selected regression)");
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW", null); Environment.SetEnvironmentVariable("KILLERMCP_NOTES_WORKFLOW_LOG", null); Directory.Delete(root, true); }
    }
}
