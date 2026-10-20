using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KillerMCP.Runtime;

/// <summary>
/// Decides where a tool call may write. KillerMCP and KillerBench keep the files that decide what agents
/// may do (the repair setting, the repair lock, repair approvals and the action log) in their state
/// folders, so no tool may create anything there, whichever route the path takes to reach them.
/// </summary>
public static class AgentPaths
{
    public const string ProtectedMessage = "Agents cannot write into the KillerMCP or KillerBench settings folders.";

    private static readonly StringComparison Comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static IEnumerable<string> ProtectedFolders => [KillerBenchSettings.Folder, KillerBenchSettings.BenchFolder];

    /// <summary>Returns null when a tool may write to the path, otherwise the reason it may not.</summary>
    public static string? WriteRefusal(string path)
    {
        string target;
        try
        {
            target = Resolve(path);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return "The output path could not be checked.";
        }
        foreach (var folder in ProtectedFolders)
        {
            if (IsInside(target, Path.GetFullPath(folder))) return ProtectedMessage;
            try
            {
                if (IsInside(target, Resolve(folder))) return ProtectedMessage;
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return "The output path could not be checked.";
            }
        }
        return null;
    }

    /// <summary>
    /// The real location a path names: the nearest part that exists is resolved through junctions,
    /// symbolic links, short names and substituted drives, and the rest is appended to it.
    /// </summary>
    internal static string Resolve(string path)
    {
        var full = Path.GetFullPath(path);
        var existing = full;
        var missing = new Stack<string>();
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null) return full;
            missing.Push(Path.GetFileName(existing));
            existing = parent;
        }
        var resolved = OperatingSystem.IsWindows() ? FinalPath(existing) : ResolveLinks(existing);
        while (missing.Count > 0) resolved = Path.Combine(resolved, missing.Pop());
        return resolved;
    }

    private static bool IsInside(string target, string folder)
    {
        var root = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return target.Equals(root, Comparison) || target.StartsWith(root + Path.DirectorySeparatorChar, Comparison);
    }

    private static string ResolveLinks(string path)
    {
        var current = Path.GetPathRoot(path) ?? string.Empty;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null && info.ResolveLinkTarget(true) is { } target) current = target.FullName;
        }
        return current;
    }

    private static string FinalPath(string path)
    {
        const uint FileReadAttributes = 0x80;
        const uint ShareAll = 0x7;
        const uint OpenExisting = 3;
        const uint BackupSemantics = 0x02000000;
        using var handle = CreateFile(path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("Could not open " + path, Marshal.GetHRForLastWin32Error());
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0 || length >= buffer.Capacity) throw new IOException("Could not resolve " + path, Marshal.GetHRForLastWin32Error());
        var final = buffer.ToString();
        if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + final[8..];
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder buffer, uint length, uint flags);
}
