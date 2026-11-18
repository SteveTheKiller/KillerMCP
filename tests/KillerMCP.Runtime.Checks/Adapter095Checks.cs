using KillerMCP;
using System.Text.Json;
using System.IO.Pipes;
using System.Text;

internal static class Adapter095Checks
{
    internal static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_095_NOTES") != "1") return false;
        if (args.SequenceEqual(["--help"])) Console.Write("create --title title\n--export <in.pdf>");
        else if (args.FirstOrDefault() == "get") Console.Write(JsonSerializer.Serialize(new { content = new string('"', 50000) }));
        else if (args.FirstOrDefault() == "--export")
        {
            using var markdown = File.Create(args[2]);
            markdown.SetLength(16 * 1024 * 1024);
        }
        else throw new Exception("Unexpected Notes fixture command");
        return true;
    }

    internal static async Task RunAsync()
    {
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF", "1");
        foreach (string version in new[] { "unknown", "1.9.4", "1.9.5" })
        {
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION", version);
            var adapters = await KillerPdfAdapter.CreateAsync(Environment.ProcessPath);
            bool safe = version == "1.9.5";
            var redact = adapters.SingleOrDefault(adapter => adapter.Tool.Name == "killerpdf_redact");
            if ((redact != null) != safe) throw new Exception($"Redaction safety gate failed on {version}");
            if (safe && (!redact!.Tool.Description.Contains("no output", StringComparison.OrdinalIgnoreCase)
                || !redact.Tool.Description.Contains("search matches", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("Redaction must explain rasterization refusal and the scope of text verification");
            var export = adapters.Single(adapter => adapter.Tool.Name == "killerpdf_export");
            if (export.Tool.InputSchema.ToString().Contains(".epub", StringComparison.OrdinalIgnoreCase) != safe)
                throw new Exception($"EPUB capability mismatch on {version}");
        }
        Console.WriteLine("PDF 0.9.5 adapter checks passed on three version profiles.");
        Environment.SetEnvironmentVariable("KILLERMCP_095_NOTES", "1");
        var get = (await KillerNotesAdapter.CreateAsync(Environment.ProcessPath)).Single(adapter => adapter.Tool.Name == "killernotes_get");
        var note = await get.CallAsync(JsonSerializer.SerializeToElement(new { id = 1, maxChars = 50000 }), CancellationToken.None);
        if (note.IsError || JsonDocument.Parse(note.Text).RootElement.GetProperty("content").GetString() != new string('"', 50000))
            throw new Exception("Could not read the full escaped 50,000-character note");
        Console.WriteLine("Full escaped note read passed.");
        string source = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            File.WriteAllText(source, "fixture");
            var save = IntegrationAdapter.Create(Environment.ProcessPath, null, null, null, Environment.ProcessPath)
                .Single(tool => tool.Tool.Name == "killerpdf_save_text_as_note");
            long before = GC.GetTotalAllocatedBytes(true);
            var result = await save.CallAsync(JsonSerializer.SerializeToElement(new { path = source }), CancellationToken.None);
            long allocated = GC.GetTotalAllocatedBytes(true) - before;
            if (!result.IsError || !result.Text.Contains("500,000") || allocated >= 16 * 1024 * 1024)
                throw new Exception($"Oversized text export used {allocated} bytes before refusal: {result.Text}");
            Console.WriteLine($"Oversized Markdown refused after {allocated} allocated bytes.");
        }
        finally { File.Delete(source); }
        Environment.SetEnvironmentVariable("KILLERMCP_095_NOTES", null);
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_KILLENDAR", "1");
        await CalendarAsync(false);
        await CalendarAsync(true);
    }

    internal static async Task CalendarAsync(bool dropResponse)
    {
        string name = "Killendar-Test-" + Guid.NewGuid().ToString("N");
        using var mapping = NotesContent.WithPipeNames(_ => name);
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var tool = await KillendarAdapter.CreateAppointmentAsync(Environment.ProcessPath) ?? throw new Exception("Missing calendar adapter");
        var serve = RespondAsync(server, dropResponse);
        var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { requestId = Guid.NewGuid(), title = "Site visit", start = "2026-10-01T09:00", end = "2026-10-01T10:00" }), CancellationToken.None);
        if (!serve.IsCompleted && !server.IsConnected) throw new Exception("Calendar create did not use the private pipe");
        await serve;
        if (result.IsError != dropResponse || !result.Text.Contains(dropResponse ? "outcome unknown" : "Site visit", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Calendar returned the wrong create outcome: " + result.Text);
        Console.WriteLine("Calendar pipe: " + (dropResponse ? "lost response" : "successful create"));
    }

    internal static async Task RespondAsync(NamedPipeServerStream server, bool dropResponse)
    {
        await server.WaitForConnectionAsync();
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        var request = JsonDocument.Parse(await reader.ReadLineAsync() ?? throw new Exception("Missing calendar request"));
        if (!dropResponse)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = 1, title = request.RootElement.GetProperty("title").GetString() }) + "\n");
            await server.WriteAsync(bytes);
            if (OperatingSystem.IsWindows()) server.WaitForPipeDrain();
        }
        server.Disconnect();
    }
}
