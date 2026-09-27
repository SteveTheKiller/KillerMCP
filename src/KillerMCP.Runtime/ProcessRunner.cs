using System.Diagnostics;
using System.Text;

namespace KillerMCP.Runtime;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool OutputExceeded);

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("The application command did not start.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var output = ReadBoundedAsync(process.StandardOutput, maximumCharacters, timeoutSource.Token);
        var error = ReadBoundedAsync(process.StandardError, maximumCharacters, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException("The application command timed out.");
        }

        var standardOutput = await output.ConfigureAwait(false);
        var standardError = await error.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, standardOutput.Text, standardError.Text, standardOutput.Exceeded || standardError.Exceeded);
    }

    private static async Task<(string Text, bool Exceeded)> ReadBoundedAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        var text = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[4096];
        var exceeded = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            var remaining = maximumCharacters - text.Length;
            if (remaining > 0)
            {
                text.Append(buffer, 0, Math.Min(read, remaining));
            }
            exceeded |= read > remaining;
        }

        return (text.ToString(), exceeded);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }
    }
}
