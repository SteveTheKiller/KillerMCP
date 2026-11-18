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
    private const int MaximumOptionsLength = 16 * 1024;
    // The elevated side never receives a path, an argument or a timeout. It gets a catalog action name and its
    // options, rebuilds the plan itself and runs only the admin-owned KillerBench CLI in Program Files.
    private sealed record Request(string Pipe, int ParentId, string Action, string Options, bool Elevate = true);
    private sealed record Handshake(string Kind, string Pipe, bool Elevate, bool Administrator = false, string? UserSid = null);

    internal static Task<KillerBenchRun> RunAsync(KillerBenchRepairPlan plan, CancellationToken token,
        Func<ProcessStartInfo, Process?>? startForChecks = null, Action? beforeCommit = null) =>
        RunAsync(plan.Action, plan.Options, TimeSpan.FromSeconds(plan.ExecuteSeconds), token, startForChecks, plan.Elevate, beforeCommit);

    private static async Task<KillerBenchRun> RunAsync(string action, string options, TimeSpan timeout,
        CancellationToken token, Func<ProcessStartInfo, Process?>? startForChecks,
        bool elevate, Action? beforeCommit)
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
            // Consent-only requests carry no action or options, including when the caller supplied them.
            var request = new Request(name, Environment.ProcessId, elevate ? action : string.Empty, elevate ? options : string.Empty, elevate);
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
            var connected = pipe.WaitForConnectionAsync(deadline.Token);
            // A helper that refuses the request exits without connecting, so stop waiting when it does.
            if (await Task.WhenAny(connected, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false) != connected)
            {
                deadline.Cancel();
                try { await connected.ConfigureAwait(false); } catch (OperationCanceledException) { }
                throw new IOException("The administrator approval helper refused this repair before it connected.");
            }
            await connected.ConfigureAwait(false);
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
            if (request is null || request.ParentId <= 0 || request.Action is null || request.Options is null
                || !request.Pipe.StartsWith("KillerMCP-Repair-", StringComparison.Ordinal)
                || request.Pipe.Length != "KillerMCP-Repair-".Length + 32
                || !Guid.TryParseExact(request.Pipe["KillerMCP-Repair-".Length..], "N", out _)
                || (!request.Elevate && (request.Action.Length != 0 || request.Options.Length != 0))) return true;
            if (!OperatingSystem.IsWindows() || !Environment.IsPrivilegedProcess) return true;
            // Validate before connecting, so a request the catalog refuses never reaches Commit.
            (string Cli, KillerBenchRepairPlan Plan)? prepared = request.Elevate ? Prepare(request.Action, request.Options) : null;
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
            if (prepared is { } work)
            {
                // The held handle denies writes, renames and deletes until the CLI has started and finished.
                using var held = TrustedExecutable(work.Cli);
                var response = await ProcessRunner.RunAsync(work.Cli, work.Plan.ExecuteArguments,
                    TimeSpan.FromSeconds(work.Plan.ExecuteSeconds), MaximumOutput,
                    environment: ChildEnvironment(ReadEnvironment())).ConfigureAwait(false);
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

    /// <summary>Rebuilds an elevated repair from the catalog and finds the only CLI allowed to run it.</summary>
    internal static (string Cli, KillerBenchRepairPlan Plan) Prepare(string action, string options, string? programFiles = null)
    {
        var definition = KillerBenchRepairs.Find(action) ?? throw new RepairTrustException("The repair is not in the KillerMCP catalog.");
        if (options.Length > MaximumOptionsLength) throw new RepairTrustException("The repair options are too large.");
        KillerBenchRepairPlan? plan;
        try
        {
            using var document = JsonDocument.Parse(options);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new RepairTrustException("The repair options must be an object.");
            (plan, _) = definition.Build(document.RootElement, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (JsonException) { throw new RepairTrustException("The repair options are not valid JSON."); }
        if (plan is null) throw new RepairTrustException("The catalog refused the repair options.");
        if (!plan.Elevate) throw new RepairTrustException("This repair never runs with administrator rights.");
        // KILLERBENCH_CLI and the per-user install are ignored here. Only the machine copy can be trusted.
        programFiles ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var cli = Path.Combine(programFiles, "KillerBench", "killerbench-cli.exe");
        TrustedExecutable(cli).Dispose();
        return (cli, plan);
    }

    /// <summary>
    /// Opens an executable that only SYSTEM, Administrators or TrustedInstaller can change, along with every
    /// folder above it. The returned handle shares read access only, so the file cannot be replaced while held.
    /// </summary>
    internal static FileStream TrustedExecutable(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new RepairTrustException("Elevated repairs are only available on Windows.");
        FileStream held;
        try { held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new RepairTrustException("KillerBench is not installed in Program Files, so elevated repairs are unavailable.");
        }
        try
        {
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new RepairTrustException("The KillerBench CLI is redirected.");
            ValidateAccess(file.GetAccessControl(), true);
            var parent = true;
            for (var folder = file.Directory; folder is not null; folder = folder.Parent)
            {
                if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) throw new RepairTrustException("A folder above the KillerBench CLI is redirected.");
                ValidateAccess(folder.GetAccessControl(), parent);
                parent = false;
            }
            return held;
        }
        catch (Exception exception) when (exception is not RepairTrustException)
        {
            held.Dispose();
            throw new RepairTrustException("The KillerBench CLI location could not be checked.");
        }
        catch { held.Dispose(); throw; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ValidateAccess(System.Security.AccessControl.FileSystemSecurity security, bool protectChildren)
    {
        var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        if (descriptor.DiscretionaryAcl is null || descriptor.Owner is null || !TrustedIdentity(descriptor.Owner.Value))
            throw new RepairTrustException("The KillerBench CLI is not in a folder owned by SYSTEM, Administrators or TrustedInstaller.");
        foreach (System.Security.AccessControl.GenericAce ace in descriptor.DiscretionaryAcl)
            if (ace is not System.Security.AccessControl.CommonAce { IsCallback: false })
                throw new RepairTrustException("The KillerBench CLI folder has an access rule that cannot be checked.");
        // Write, append, delete, change permissions, take ownership, write attributes and generic write or all.
        var mutation = 0x10000000u | 0x40000000u | 0x00010000u | 0x00040000u | 0x00080000u | 0x40u | 0x100u | 0x10u;
        if (protectChildren) mutation |= 0x2u | 0x4u;
        foreach (System.Security.AccessControl.FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != System.Security.AccessControl.AccessControlType.Allow) continue;
            var inheritOnly = (rule.PropagationFlags & System.Security.AccessControl.PropagationFlags.InheritOnly) != 0;
            if (inheritOnly && !protectChildren) continue;
            var sid = rule.IdentityReference.Value;
            if (TrustedIdentity(sid) || inheritOnly && sid == "S-1-3-0") continue;
            if ((unchecked((uint)rule.FileSystemRights) & mutation) != 0)
                throw new RepairTrustException("Someone other than an administrator can change the KillerBench CLI or a folder above it.");
        }
    }

    private static bool TrustedIdentity(string sid) => sid is "S-1-5-18" or "S-1-5-32-544"
        or "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    /// <summary>Clears every runtime injection variable for the elevated child and leaves the rest alone.</summary>
    internal static IReadOnlyDictionary<string, string?> ChildEnvironment(IReadOnlyDictionary<string, string?> current) => current.Keys
        .Where(name => name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COR_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(name => name, _ => (string?)null, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string?> ReadEnvironment() => Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);

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

internal sealed class RepairTrustException(string message) : InvalidOperationException(message);
