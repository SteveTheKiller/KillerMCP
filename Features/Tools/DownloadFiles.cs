using System.Runtime.InteropServices;
using System.Text;
using KillerMCP.Models;

namespace KillerMCP.Features.Tools;

internal static class DownloadFiles
{
    private const int MaximumBytes = 4 * 1024 * 1024;

    // Data types Windows opens in a viewer. Anything it runs, follows or renders with script is refused.
    private static readonly HashSet<string> InertExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml", ".log", ".ini", ".toml", ".ics", ".vcf",
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".pem", ".cer", ".crt",
    };
    internal static string DownloadsFolder
    {
        get
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer));
            try { return Marshal.PtrToStringUni(pointer)!; }
            finally { Marshal.FreeCoTaskMem(pointer); }
        }
    }

    internal static object WriteText(string path, string text) => Write(path, new UTF8Encoding(false, true).GetBytes(text));

    internal static object Decode(string path, string base64)
    {
        if (base64.Length > (MaximumBytes + 2) / 3 * 4) throw new ToolCallException("File content exceeds 4 MiB.");
        try { return Write(path, Convert.FromBase64String(base64)); }
        catch (FormatException) { throw new ToolCallException("Invalid Base64 content."); }
    }

    private static object Write(string path, byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) throw new ToolCallException("File content exceeds 4 MiB.");
        if (!Path.IsPathFullyQualified(path) || path.Replace('/', '\\').IndexOf(':', 2) >= 0)
            throw new ToolCallException("Choose a fully qualified path for a new file in Downloads.");
        if (!InertExtensions.Contains(Path.GetExtension(path)))
            throw new ToolCallException("Choose a data file name, such as .txt, .md, .csv, .json or .pdf. Programs, shortcuts and web pages cannot be saved.");
        string? temporary = null;
        try
        {
            if (AgentPaths.AccessRefusal(path) is { } access) throw new ToolCallException(access);
            if (AgentPaths.WriteRefusal(path) is { } refusal) throw new ToolCallException(refusal);
            var downloads = AgentPaths.Resolve(DownloadsFolder);
            var target = AgentPaths.Resolve(path);
            if (!string.Equals(Path.GetDirectoryName(target), downloads, StringComparison.OrdinalIgnoreCase))
                throw new ToolCallException("Files can only be saved directly in your Downloads folder.");
            if (File.Exists(target) || Directory.Exists(target)) throw new ToolCallException("Output already exists. Choose a new filename.");
            temporary = Path.Combine(downloads, ".killermcp-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            // The Internet zone travels with the file, so Windows warns before anything opens it as trusted.
            File.WriteAllText(temporary + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n", Encoding.ASCII);
            File.Move(temporary, target, false);
            temporary = null;
            return new { path = target, bytes = bytes.Length };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ToolCallException("Unable to save file. Choose a new filename in Downloads.");
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
}
