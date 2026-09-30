using System.Text.Json;

namespace KillerMCP;

internal static class ToolCallLog
{
    private static readonly string Path = Environment.GetEnvironmentVariable("KILLERMCP_TOOL_CALL_LOG")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KillerMCP", "tool-calls.jsonl");

    public static void Record(string? tool, string outcome)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var entry = JsonSerializer.Serialize(new
            {
                timeUtc = DateTimeOffset.UtcNow,
                tool = tool ?? "(missing)",
                outcome,
            });
            File.AppendAllText(Path, entry + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("KillerMCP could not write its tool call log.");
        }
    }
}
