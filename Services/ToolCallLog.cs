using System.Text.Json;

namespace KillerMCP;

internal static class ToolCallLog
{
    private const long MaximumBytes = 5 * 1024 * 1024;
    private const int Attempts = 5;
    private static readonly object Gate = new();
    private static readonly string Path = Environment.GetEnvironmentVariable("KILLERMCP_TOOL_CALL_LOG")
        ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KillerMCP", "tool-calls.jsonl");

    public static void Record(string? tool, string outcome, long? durationMs = null)
    {
        var entry = JsonSerializer.Serialize(new
        {
            timeUtc = DateTimeOffset.UtcNow,
            tool = tool ?? "(missing)",
            outcome,
            durationMs,
        });
        var bytes = System.Text.Encoding.UTF8.GetBytes(entry + Environment.NewLine);
        lock (Gate)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                    RollOver();
                    // Other KillerMCP processes may share this file, so allow concurrent appends.
                    using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(bytes);
                    return;
                }
                catch (IOException) when (attempt < Attempts)
                {
                    Thread.Sleep(20 * attempt);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("KillerMCP could not write its tool call log.");
                    return;
                }
            }
        }
    }

    private static void RollOver()
    {
        var file = new FileInfo(Path);
        if (!file.Exists || file.Length < MaximumBytes)
        {
            return;
        }

        File.Move(Path, System.IO.Path.ChangeExtension(Path, ".1.jsonl"), overwrite: true);
    }
}
