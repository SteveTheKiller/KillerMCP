using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace KillerMCP;

internal static class ElevatedRepairBridge
{
    private const int MaximumFrameBytes = 32 * 1024 * 1024;
    private const int MaximumOutput = 4 * 1024 * 1024;
    private sealed record Request(string Pipe, int ParentId, string Executable, string[] Arguments, double TimeoutSeconds);

    internal static async Task<KillerBenchRun> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken token, Func<ProcessStartInfo, Process?>? startForChecks = null)
    {
        var name = "KillerMCP-Repair-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var request = new Request(name, Environment.ProcessId, executable, arguments.ToArray(), timeout.TotalSeconds);
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request));
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(ElevatedRepairBridge).Assembly.GetName().Name + ".dll"));
        start.ArgumentList.Add("--elevated-repair");
        start.ArgumentList.Add(encoded);
        using var process = (startForChecks ?? Process.Start)(start) ?? throw new InvalidOperationException("The elevated repair helper did not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout + TimeSpan.FromSeconds(15));
        try
        {
            while (true)
            {
                await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var client)) throw new IOException("The repair helper identity could not be checked.");
                if (client == process.Id) break;
                pipe.Disconnect();
            }
            var header = new byte[4];
            await pipe.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            var length = BitConverter.ToInt32(header);
            if (length is < 1 or > MaximumFrameBytes) throw new InvalidDataException("The repair helper returned an invalid result length.");
            var payload = new byte[length];
            await pipe.ReadExactlyAsync(payload, deadline.Token).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<KillerBenchRun>(payload) ?? throw new InvalidDataException("The repair helper returned no result.");
            if (result.Output.Length > MaximumOutput || result.Error.Length > 65536) throw new InvalidDataException("The repair helper result exceeds its limit.");
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("The repair helper did not finish successfully.");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("The repair did not report a complete result in time and may still be running.");
        }
    }

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] != "--elevated-repair") return false;
        Environment.ExitCode = 1;
        try
        {
            if (args.Length != 2 || args[1].Length > 24000) return true;
            var request = JsonSerializer.Deserialize<Request>(Convert.FromBase64String(args[1]));
            if (request is null || request.ParentId <= 0 || request.TimeoutSeconds is <= 0 or > 14400
                || !request.Pipe.StartsWith("KillerMCP-Repair-", StringComparison.Ordinal)
                || request.Pipe.Length != "KillerMCP-Repair-".Length + 32
                || !Guid.TryParseExact(request.Pipe["KillerMCP-Repair-".Length..], "N", out _)
                || !Path.IsPathFullyQualified(request.Executable)) return true;
            using var pipe = new NamedPipeClientStream(".", request.Pipe, PipeDirection.Out, PipeOptions.Asynchronous, System.Security.Principal.TokenImpersonationLevel.Anonymous);
            await pipe.ConnectAsync(10000).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != request.ParentId)
                throw new IOException("The repair host identity could not be checked.");
            var response = await ProcessRunner.RunAsync(request.Executable, request.Arguments,
                TimeSpan.FromSeconds(request.TimeoutSeconds), MaximumOutput).ConfigureAwait(false);
            var result = new KillerBenchRun(response.ExitCode, response.StandardOutput,
                response.StandardError.Length > 65536 ? response.StandardError[..65536] : response.StandardError, response.OutputExceeded);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result);
            if (bytes.Length > MaximumFrameBytes) throw new InvalidDataException("The repair result exceeds its limit.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), deadline.Token).ConfigureAwait(false);
            await pipe.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
            await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            Environment.ExitCode = 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or FormatException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Console.Error.WriteLine("The elevated repair helper could not return a complete result.");
        }
        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
}
