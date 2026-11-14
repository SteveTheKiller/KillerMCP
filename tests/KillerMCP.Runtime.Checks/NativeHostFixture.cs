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
        await McpServer.RunAsync();
        return true;
    }
}
