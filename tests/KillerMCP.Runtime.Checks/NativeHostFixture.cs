using KillerMCP;
using KillerMCP.Services.Mcp;

internal static class NativeHostFixture
{
    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.SequenceEqual(["--review-downloads-folder"]))
        {
            Console.Write(KillerMCP.Features.Tools.DownloadFiles.DownloadsFolder);
            return true;
        }
        if (args.Length == 0 || args[0] != "--review-mcp-host") return false;
        if (args.Length != 3 || !Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[2]))
            throw new ArgumentException("The native host fixture requires absolute MCP and Bench test folders.");
        using var folders = KillerBenchSettings.UseTestFolders(args[1], args[2]);
        string calendarPipe = "Killendar-fixture-" + Guid.NewGuid().ToString("N");
        using var names = NotesContent.WithPipeNames(name => name.StartsWith("Killendar-", StringComparison.Ordinal) ? calendarPipe : name);
        using var server = new System.IO.Pipes.NamedPipeServerStream(calendarPipe, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        var appointment = Adapter095Checks.RespondAsync(server, false);
        await McpServer.RunAsync();
        if (appointment.IsCompleted) await appointment;
        return true;
    }
}
