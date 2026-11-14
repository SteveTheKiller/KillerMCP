using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace KillerMCP;

internal static class ElevatedRepairBridge
{
    private const int MaximumFrameBytes = 32 * 1024 * 1024;
    private const int MaximumOutput = 4 * 1024 * 1024;
    private sealed record Request(string Pipe, int ParentId, string Executable, string[] Arguments, double TimeoutSeconds, bool Elevate = true);
    private sealed record Handshake(string Kind, string Pipe, bool Elevate, bool Administrator = false, string? UserSid = null);

    internal static async Task<KillerBenchRun> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken token, Func<ProcessStartInfo, Process?>? startForChecks = null,
        bool elevate = true, Action? beforeCommit = null)
    {
        if (!OperatingSystem.IsWindows()) throw new RepairConsentException("Windows administrator approval is only available on Windows.");
        if (Environment.IsPrivilegedProcess && startForChecks is null)
            throw new RepairConsentException("Restart the agent client normally. An already elevated KillerMCP cannot request fresh Windows administrator approval.");
        var executionCommitted = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var name = "KillerMCP-Repair-" + Guid.NewGuid().ToString("N");
            using var pipe = CreatePipe(name);
            // Consent-only requests carry no executable or arguments, including when the caller supplied them.
            var request = new Request(name, Environment.ProcessId, elevate ? executable : string.Empty,
                elevate ? arguments.ToArray() : [], timeout.TotalSeconds, elevate);
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
            using var process = (startForChecks ?? Process.Start)(start) ?? throw new RepairConsentException("The administrator approval helper did not start.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout + TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(deadline.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var client)) throw new IOException("The repair helper identity could not be checked.");
            if (client != process.Id) throw new IOException("The administrator approval helper identity did not match the launched process.");
            var ready = await ReadFrame<Handshake>(pipe, 4096, deadline.Token).ConfigureAwait(false);
            if (ready.Kind != "Ready" || ready.Pipe != name || ready.Elevate != elevate || !ready.Administrator || string.IsNullOrEmpty(ready.UserSid))
                throw new IOException("The administrator approval helper did not return authenticated readiness.");
            token.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            token.ThrowIfCancellationRequested();
            // A partial Commit write can reach the peer. From this point an elevated command may run.
            executionCommitted = elevate;
            using var completion = new CancellationTokenSource(timeout + TimeSpan.FromSeconds(15));
            await WriteFrame(pipe, new Handshake("Commit", name, elevate), completion.Token).ConfigureAwait(false);
            var result = await ReadFrame<KillerBenchRun>(pipe, MaximumFrameBytes, completion.Token).ConfigureAwait(false);
            if (result.Output is null || result.Error is null || result.Output.Length > MaximumOutput || result.Error.Length > 65536)
                throw new InvalidDataException("The repair helper result exceeds its limit or is incomplete.");
            if (!elevate && (result.ExitCode != 0 || result.Truncated || result.Error.Length != 0
                || JsonSerializer.Deserialize<Handshake>(result.Output) != ready))
                throw new InvalidDataException("The administrator approval helper returned an invalid consent receipt.");
            await process.WaitForExitAsync(completion.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("The repair helper did not finish successfully.");
            return result;
        }
        catch (Exception exception) when (!executionCommitted && exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            throw new RepairConsentException("Windows administrator approval did not complete, so no repair was started. " + exception.Message, exception);
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
                || (request.Elevate && !Path.IsPathFullyQualified(request.Executable))
                || (!request.Elevate && (request.Executable.Length != 0 || request.Arguments.Length != 0))) return true;
            if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) return true;
            using var pipe = new NamedPipeClientStream(".", request.Pipe, (PipeAccessRights)0x0012019b,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous, HandleInheritability.None);
            await pipe.ConnectAsync(10000).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != request.ParentId)
                throw new IOException("The repair host identity could not be checked.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var ready = new Handshake("Ready", request.Pipe, request.Elevate, true, WindowsIdentity.GetCurrent().User!.Value);
            await WriteFrame(pipe, ready, deadline.Token).ConfigureAwait(false);
            var commit = await ReadFrame<Handshake>(pipe, 4096, deadline.Token).ConfigureAwait(false);
            if (commit.Kind != "Commit" || commit.Pipe != request.Pipe || commit.Elevate != request.Elevate)
                throw new IOException("The repair host did not commit this operation.");
            KillerBenchRun result;
            if (request.Elevate)
            {
                var response = await ProcessRunner.RunAsync(request.Executable, request.Arguments,
                    TimeSpan.FromSeconds(request.TimeoutSeconds), MaximumOutput).ConfigureAwait(false);
                result = new KillerBenchRun(response.ExitCode, response.StandardOutput,
                    response.StandardError.Length > 65536 ? response.StandardError[..65536] : response.StandardError, response.OutputExceeded);
            }
            else result = new KillerBenchRun(0, JsonSerializer.Serialize(ready), string.Empty, false);
            using var resultDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await WriteFrame(pipe, result, resultDeadline.Token).ConfigureAwait(false);
            Environment.ExitCode = 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or JsonException or FormatException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Console.Error.WriteLine("The elevated repair helper could not return a complete result.");
        }
        return true;
    }

    private static async Task<T> ReadFrame<T>(Stream pipe, int maximum, CancellationToken token)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BitConverter.ToInt32(header);
        if (length < 1 || length > maximum) throw new InvalidDataException("The repair helper returned an invalid frame length.");
        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidDataException("The repair helper returned an empty frame.");
    }

    private static async Task WriteFrame<T>(Stream pipe, T value, CancellationToken token)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > MaximumFrameBytes) throw new InvalidDataException("The repair helper frame exceeds its limit.");
        await pipe.WriteAsync(BitConverter.GetBytes(payload.Length), token).ConfigureAwait(false);
        await pipe.WriteAsync(payload, token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreatePipe(string name)
    {
        var owner = WindowsIdentity.GetCurrent().User!.Value;
        // Administrators receive client read/write rights without FILE_CREATE_PIPE_INSTANCE (0x4).
        // A protected DACL admits alternate-admin UAC credentials while rejecting other local users.
        var sddl = $"O:{owner}D:P(A;;GA;;;{owner})(A;;0x0012019b;;;BA)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            // Duplex, overlapped, first instance; PIPE_REJECT_REMOTE_CLIENTS limits access to this PC.
            var handle = CreateNamedPipe("\\\\.\\pipe\\" + name, 0x40080003, 0x8, 1, 65536, 65536, 0, ref security);
            if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { LocalFree(descriptor); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out IntPtr security, out uint size);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances, uint outputBuffer, uint inputBuffer, uint timeout, ref SecurityAttributes security);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
}

internal sealed class RepairConsentException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
