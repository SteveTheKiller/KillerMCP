using System.Collections.Concurrent;
using System.Text;

namespace KillerMCP;

// Note text on a command line can be read by other programs running as the user and is kept in
// process-creation logs, so it goes through --content-file whenever the installed KillerNotes accepts it.
// An older KillerNotes still gets text that fits its command line, which holds about 32,000 characters.
internal static class NotesContent
{
    internal const int CommandLineLimit = 30000;
    internal const int NoteLimit = 500000;
    private static readonly ConcurrentDictionary<string, bool> FileSupport = new(StringComparer.OrdinalIgnoreCase);

    internal static IEnumerable<string> Prefix(string notes) =>
        notes.EndsWith("KillerNotes.exe", StringComparison.OrdinalIgnoreCase) ? ["--cli"] : [];

    internal static bool AcceptsFile(string help) => help.Contains("--content-file", StringComparison.Ordinal);

    // The KillerNotes adapter reads the help when it starts, so what it found replaces anything remembered.
    internal static void Remember(string notes, string help) => FileSupport[notes] = AcceptsFile(help);

    internal static async Task<bool> SupportsFileAsync(string notes, CancellationToken token)
    {
        if (FileSupport.TryGetValue(notes, out var known)) return known;
        var help = await ProcessRunner.RunAsync(notes, Prefix(notes).Concat(["--help"]), TimeSpan.FromSeconds(15), 65536, token).ConfigureAwait(false);
        // Only an answer KillerNotes gave is remembered. A failed request is asked again next time instead of
        // sending every later note on the command line.
        if (help.ExitCode != 0) return false;
        var supported = AcceptsFile(help.StandardOutput);
        FileSupport[notes] = supported;
        return supported;
    }

    // The arguments that carry the note text, and the temporary folder to delete afterward when
    // the text went into a file. Error is set when KillerNotes cannot take text this long.
    internal static async Task<(string[] Arguments, string? Folder, string? Error)> ArgumentsAsync(string notes, string content, CancellationToken token, IEnumerable<string>? otherArguments = null)
    {
        if (content.Length > NoteLimit) return ([], null, "The note text is longer than the 500,000 characters a KillerNotes note holds");
        if (!await SupportsFileAsync(notes, token).ConfigureAwait(false))
        {
            if (content.Length <= CommandLineLimit && ProcessRunner.FitsCommandLine(notes, Prefix(notes).Concat(otherArguments ?? []).Concat(["--content", content])))
                return (["--content", content], null, null);
            return ([], null, "This note is too large for a command line; update KillerNotes to save it from a file");
        }
        var folder = Path.Combine(Path.GetTempPath(), "killermcp-note-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
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

    internal static void Delete(string? folder)
    {
        try { if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
