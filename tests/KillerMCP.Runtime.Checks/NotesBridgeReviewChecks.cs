using System.Diagnostics;
using System.Text.Json;
using KillerMCP;

internal static class NotesBridgeReviewChecks
{
    public static async Task RunAsync()
    {
        await ClosedAsync(); await UnknownOutcomeAsync(); await ExtensionsAsync(); await CalendarClosedAsync();
    }

    public static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE") != "1") return false;
        if (args.SequenceEqual(["--help"]) && Environment.GetEnvironmentVariable("KILLERMCP_CALENDAR_BRIDGE_FIXTURE") == "1")
        { Console.Write("agenda <yyyy-MM-dd>\ncreate <json> appointment-request-id-v1\napp-pipe-v1"); return true; }
        if (args.SequenceEqual(["--help"])) Console.Write("create --title title --content-file path\n" + (Environment.GetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_SLOW") == "1" ? "" : "app-pipe-v1\n") + "search <query>\n" +
            (Environment.GetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_EXTRA") == "1" ? "templates trash-list version-get tag-definitions create-from-template today restore pin unpin version-restore rename-group create-tag rename-tag set-tag-color template-group daily-group" : ""));
        else
        {
            if (Environment.GetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_LOG") is { } log) File.WriteAllText(log, JsonSerializer.Serialize(args));
            if (Environment.GetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_SLOW") == "1") Thread.Sleep(5000);
            Console.Write(JsonSerializer.Serialize(new { arguments = args }));
        }
        return true;
    }

    public static async Task ClosedAsync()
    {
        using var pipeNames = NotesContent.WithPipeNames(_ => "KillerMCP-closed-test-" + Guid.NewGuid().ToString("N"));
        string log = Path.Combine(Path.GetTempPath(), "killermcp-notes-closed-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", "1");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_LOG", log);
        try
        {
            var tool = (await KillerNotesAdapter.CreateAsync(Environment.ProcessPath)).Single(item => item.Tool.Name == "killernotes_stats");
            var clock = Stopwatch.StartNew();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { }), CancellationToken.None);
            if (!result.IsError || !result.Text.Contains("Open and unlock KillerNotes", StringComparison.Ordinal) || clock.ElapsedMilliseconds >= 200 || File.Exists(log))
                throw new InvalidOperationException("Closed app must be refused before spawn in under 200 ms: " + result.Text + "; " + clock.ElapsedMilliseconds + " ms");
            Console.WriteLine("PASS closed Notes refuses before spawning in " + clock.ElapsedMilliseconds + " ms (1 selected regression)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", null);
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_LOG", null);
            if (File.Exists(log)) File.Delete(log);
        }
    }

    public static async Task UnknownOutcomeAsync()
    {
        string log = Path.Combine(Path.GetTempPath(), "killermcp-notes-dispatch-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", "1");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_SLOW", "1");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_LOG", log);
        try
        {
            var tool = (await KillerNotesAdapter.CreateAsync(Environment.ProcessPath)).Single(item => item.Tool.Name == "killernotes_create");
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var call = tool.CallAsync(JsonSerializer.SerializeToElement(new { title = "fixture", content = "fixture" }), stop.Token);
            while (!File.Exists(log) && !call.IsCompleted) await Task.Delay(10);
            if (!File.Exists(log)) throw new InvalidOperationException("Fake note write was never dispatched.");
            stop.Cancel();
            AppCallResult result;
            try { result = await call; }
            catch (OperationCanceledException) { throw new InvalidOperationException("A dispatched write lost its unknown-outcome warning on cancellation."); }
            if (!result.IsError || !result.Text.Contains("outcome unknown", StringComparison.OrdinalIgnoreCase) || !result.Text.Contains("retry", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A dispatched write must warn to check before retrying: " + result.Text);
            Console.WriteLine("PASS dispatched Notes write reports unknown outcome on cancellation (1 selected regression)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", null);
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_SLOW", null);
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_LOG", null);
            if (File.Exists(log)) File.Delete(log);
        }
    }

    public static async Task ExtensionsAsync()
    {
        (string Name, object Input, string[] Arguments)[] cases =
        [
            ("templates", new { }, ["templates"]), ("trash_list", new { }, ["trash-list"]),
            ("tag_definitions", new { }, ["tag-definitions"]), ("version_get", new { id = 3, versionId = 5 }, ["version-get", "--id", "3", "--version-id", "5"]),
            ("create_from_template", new { templateId = 4, title = "fixture", group = "Work" }, ["create-from-template", "--template-id", "4", "--title", "fixture", "--group", "Work"]),
            ("today", new { }, ["today"]), ("restore", new { id = 3 }, ["restore", "--id", "3"]),
            ("pin", new { id = 3 }, ["pin", "--id", "3"]), ("unpin", new { id = 3 }, ["unpin", "--id", "3"]),
            ("version_restore", new { id = 3, versionId = 5 }, ["version-restore", "--id", "3", "--version-id", "5"]),
            ("rename_group", new { name = "Work", newName = "Cases" }, ["rename-group", "--name", "Work", "--new-name", "Cases"]),
            ("create_tag", new { name = "urgent", color = "Red" }, ["create-tag", "--name", "urgent", "--color", "Red"]),
            ("rename_tag", new { name = "urgent", newName = "priority" }, ["rename-tag", "--name", "urgent", "--new-name", "priority"]),
            ("set_tag_color", new { name = "urgent", color = "" }, ["set-tag-color", "--name", "urgent", "--color", ""]),
            ("template_group", new { name = "Templates" }, ["template-group", "--name", "Templates"]),
            ("daily_group", new { name = "Daily" }, ["daily-group", "--name", "Daily"]),
        ];
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", "1");
        try
        {
            var old = await KillerNotesAdapter.CreateAsync(Environment.ProcessPath);
            if (old.Any(tool => cases.Any(item => tool.Tool.Name == "killernotes_" + item.Name))) throw new InvalidOperationException("Older help exposed unsupported tools.");
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_EXTRA", "1");
            var tools = (await KillerNotesAdapter.CreateAsync(Environment.ProcessPath)).ToDictionary(item => item.Tool.Name);
            string pipeName = "KillerMCP-notes-extension-test-" + Guid.NewGuid().ToString("N");
            using var pipeNames = NotesContent.WithPipeNames(_ => pipeName);
            using var pipe = new System.IO.Pipes.NamedPipeServerStream(pipeName,
                System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
            foreach (var item in cases)
            {
                if (!tools.TryGetValue("killernotes_" + item.Name, out var tool)) throw new InvalidOperationException("Missing supported tool: " + item.Name);
                var result = await tool.CallAsync(JsonSerializer.SerializeToElement(item.Input), CancellationToken.None);
                using var response = JsonDocument.Parse(result.Text);
                if (result.IsError || !response.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).SequenceEqual(item.Arguments))
                    throw new InvalidOperationException("Wrong arguments for " + item.Name + ": " + result.Text);
            }
            Console.WriteLine("PASS capability-gated Notes extensions and exact arguments (16 selected cases)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", null);
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_EXTRA", null);
        }
    }

    public static async Task CalendarClosedAsync()
    {
        using var pipeNames = NotesContent.WithPipeNames(_ => "KillerMCP-calendar-closed-test-" + Guid.NewGuid().ToString("N"));
        string log = Path.Combine(Path.GetTempPath(), "killermcp-calendar-closed-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", "1");
        Environment.SetEnvironmentVariable("KILLERMCP_CALENDAR_BRIDGE_FIXTURE", "1");
        Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_LOG", log);
        try
        {
            var tool = await KillendarAdapter.CreateAsync(Environment.ProcessPath) ?? throw new InvalidOperationException("No calendar fixture adapter.");
            var clock = Stopwatch.StartNew();
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { date = "2026-11-13" }), CancellationToken.None);
            if (!result.IsError || !result.Text.Contains("Open and unlock Killendar", StringComparison.Ordinal) || clock.ElapsedMilliseconds >= 200 || File.Exists(log))
                throw new InvalidOperationException("Closed calendar must refuse before spawn in under 200 ms: " + result.Text);
            Console.WriteLine("PASS closed Calendar refuses before spawning in " + clock.ElapsedMilliseconds + " ms (1 selected regression)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_FIXTURE", null);
            Environment.SetEnvironmentVariable("KILLERMCP_CALENDAR_BRIDGE_FIXTURE", null);
            Environment.SetEnvironmentVariable("KILLERMCP_NOTES_BRIDGE_LOG", null);
            if (File.Exists(log)) File.Delete(log);
        }
    }
}
