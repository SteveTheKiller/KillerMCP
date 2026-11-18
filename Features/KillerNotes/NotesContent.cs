using System.Collections.Concurrent;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KillerMCP;

// Note text on a command line can be read by other programs running as the user and is kept in
// process-creation logs, so it goes through --content-file whenever the installed KillerNotes accepts it.
// An older KillerNotes still gets text that fits its command line, which holds about 32,000 characters.
internal static class NotesContent
{
    internal const int CommandLineLimit = 30000;
    internal const int NoteLimit = 500000;
    private static readonly ConcurrentDictionary<string, bool> FileSupport = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> PipeSupport = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<Func<string, string>?> PipeNames = new();

    internal static IDisposable WithPipeNames(Func<string, string> names)
    {
        var previous = PipeNames.Value;
        PipeNames.Value = names;
        return new PipeNameScope(previous);
    }

    private sealed class PipeNameScope(Func<string, string>? previous) : IDisposable
    {
        public void Dispose() => PipeNames.Value = previous;
    }

    internal static IEnumerable<string> Prefix(string notes) =>
        notes.EndsWith("KillerNotes.exe", StringComparison.OrdinalIgnoreCase) ? ["--cli"] : [];

    internal static bool AcceptsFile(string help) => help.Contains("--content-file", StringComparison.Ordinal);

    // The KillerNotes adapter reads the help when it starts, so what it found replaces anything remembered.
    internal static void Remember(string notes, string help)
    {
        FileSupport[notes] = AcceptsFile(help);
        PipeSupport[notes] = help.Contains("app-pipe-v1", StringComparison.Ordinal);
    }

    internal static string? AvailabilityRefusal(string notes) =>
        PipeSupport.TryGetValue(notes, out bool required) && required && PipeMissing("KillerNotes-MCP-" + Process.GetCurrentProcess().SessionId)
            ? "Open and unlock KillerNotes before using active-vault commands" : null;

    internal static bool PipeMissing(string name)
    {
        if (!OperatingSystem.IsWindows()) return false;
        name = ResolvePipeName(name);
        if (WaitNamedPipe(@"\\.\pipe\" + name, 1)) return false;
        return Marshal.GetLastWin32Error() == 2;
    }

    internal static string ResolvePipeName(string name) => PipeNames.Value?.Invoke(name) ?? name;

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipe(string name, uint timeout);

    // True or false once KillerNotes has answered its help request, and null when it did not answer.
    internal static async Task<bool?> SupportsFileAsync(string notes, CancellationToken token)
    {
        if (FileSupport.TryGetValue(notes, out var known)) return known;
        var help = await ProcessRunner.RunAsync(notes, Prefix(notes).Concat(["--help"]), TimeSpan.FromSeconds(15), 65536, token).ConfigureAwait(false);
        // Only an answer KillerNotes gave is remembered. A failed request is asked again next time.
        if (help.ExitCode != 0) return null;
        var supported = AcceptsFile(help.StandardOutput);
        FileSupport[notes] = supported;
        return supported;
    }

    internal const string ProbeRefusal = "KillerNotes did not answer how it accepts note text, so the note was not sent. Try again, or open KillerNotes once and retry.";

    // Note text waits for KillerNotes in its own folder inside KillerMCP's settings folder, which agents
    // cannot read, with access for this account only. A folder left by a crash is removed on a later save.
    internal static string StagingRoot => Path.Combine(KillerBenchSettings.Folder, "note-staging");

    // The arguments that carry the note text, and the temporary folder to delete afterward when
    // the text went into a file. Error is set when KillerNotes cannot take text this long.
    internal static async Task<(string[] Arguments, string? Folder, string? Error)> ArgumentsAsync(string notes, string content, CancellationToken token, IEnumerable<string>? otherArguments = null)
    {
        if (content.Length > NoteLimit) return ([], null, "The note text is longer than the 500,000 characters a KillerNotes note holds");
        // A help request can fail once while KillerNotes starts, so it is asked again before the save is refused.
        var accepts = await SupportsFileAsync(notes, token).ConfigureAwait(false)
            ?? await SupportsFileAsync(notes, token).ConfigureAwait(false);
        // Without an answer the text never falls back to the command line, where other programs can read it.
        if (accepts is null) return ([], null, ProbeRefusal);
        if (accepts == false)
        {
            if (content.Length <= CommandLineLimit && ProcessRunner.FitsCommandLine(notes, Prefix(notes).Concat(otherArguments ?? []).Concat(["--content", content])))
                return (["--content", content], null, null);
            return ([], null, "This note is too large for a command line; update KillerNotes to save it from a file");
        }
        RemoveStale();
        var folder = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"));
        try
        {
            CreatePrivateFolder(folder);
            var file = Path.Combine(folder, "note.md");
            await File.WriteAllTextAsync(file, content, new UTF8Encoding(false), token).ConfigureAwait(false);
            return (["--content-file", file], folder, null);
        }
        catch
        {
            Delete(folder);
            throw;
        }
    }

    private static void CreatePrivateFolder(string folder)
    {
        Directory.CreateDirectory(StagingRoot);
        if (!OperatingSystem.IsWindows()) { Directory.CreateDirectory(folder); return; }
        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(folder).Create(security);
    }

    private static void RemoveStale()
    {
        string[] folders;
        try { folders = Directory.GetDirectories(StagingRoot); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return; }
        foreach (var folder in folders)
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
            try { if (Directory.GetCreationTimeUtc(folder) < DateTime.UtcNow.AddHours(-1)) Directory.Delete(folder, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static void Delete(string? folder)
    {
        try { if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
