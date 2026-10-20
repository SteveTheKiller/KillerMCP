using System.Diagnostics;
using System.Text;

namespace KillerMCP.Runtime;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool OutputExceeded);

internal static class ProcessRunner
{
    // CreateProcess rejects command lines longer than 32,767 characters.
    private const int MaximumCommandLineLength = 32_000;

    internal static bool FitsCommandLine(string executable, IEnumerable<string> arguments) =>
        executable.Length + 3 + arguments.Sum(EstimatedLength) <= MaximumCommandLineLength;

    private static int EstimatedLength(string argument)
    {
        var length = argument.Length + 3;
        foreach (var character in argument)
        {
            if (character is '"' or '\\')
            {
                length++;
            }
        }

        return length;
    }

    // standardInput, when given, is written to the app as UTF-8 and then closed. It carries what must not
    // appear on a command line, where other programs and process logs can read it.
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken cancellationToken = default, string? standardInput = null)
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
                RedirectStandardInput = standardInput is not null,
                StandardInputEncoding = standardInput is not null ? new UTF8Encoding(false) : null,
            },
        };
        var commandLineLength = executable.Length + 3;
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
            commandLineLength += EstimatedLength(argument);
        }

        if (commandLineLength > MaximumCommandLineLength)
        {
            throw new InvalidOperationException("The request is too large to pass to the application.");
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The application command did not start.");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("The application command did not start.");
        }

        if (standardInput is not null)
        {
            try
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The app exited before reading its input; its exit code and output say why.
            }
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var readSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token);
        var output = ReadBoundedAsync(process.StandardOutput, maximumCharacters, readSource.Token);
        var error = ReadBoundedAsync(process.StandardError, maximumCharacters, readSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            readSource.Cancel();
            await Task.WhenAll(output, error).ConfigureAwait(false);
            // A caller that cancels gets a cancellation; only a real timeout is reported as one.
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException("The application command timed out.");
        }

        // A process the app started can keep its output open after the app exits, so the readers
        // get a short grace period and then return what they have.
        readSource.CancelAfter(OutputGrace);
        var standardOutput = await output.ConfigureAwait(false);
        var standardError = await error.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, standardOutput.Text, standardError.Text, standardOutput.Exceeded || standardError.Exceeded);
    }

    private static readonly TimeSpan OutputGrace = TimeSpan.FromSeconds(2);

    private static async Task<(string Text, bool Exceeded)> ReadBoundedAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        var text = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var buffer = new char[4096];
        var exceeded = false;
        int read;
        try
        {
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                var remaining = maximumCharacters - text.Length;
                if (remaining > 0)
                {
                    text.Append(buffer, 0, Math.Min(read, remaining));
                }
                exceeded |= read > remaining;
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by a timeout, a cancellation or the grace period; keep what was read.
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
