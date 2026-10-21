using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace KillerMCP;

/// <summary>
/// Decides which paths a tool call may open and where it may write. Opening a file on a server sends
/// the user's Windows sign-in to it, so network paths are limited to servers on the local network and
/// servers the user lists in network-servers.json in the KillerMCP state folder. KillerMCP and
/// KillerBench keep the files that decide what agents may do (the repair setting, the repair lock,
/// repair approvals, the server list and the action log) in their state folders, so no tool may create
/// anything there, whichever route the path takes to reach them.
/// </summary>
public static class AgentPaths
{
    public const string ProtectedMessage = "Agents cannot write into the KillerMCP or KillerBench settings folders.";
    public const string NetworkMessage = "KillerMCP opens network paths only on servers in the local network or listed in network-servers.json in the KillerMCP settings folder.";
    public const string DeviceMessage = "Device paths are not accepted.";
    private const int LinkHops = 8;

    public static string AllowlistPath => Path.Combine(KillerBenchSettings.Folder, "network-servers.json");

    /// <summary>
    /// Returns null when a tool may open the path, otherwise the reason it may not. Nothing here touches
    /// a remote server before the server has passed the rule, so a refused server is never contacted.
    /// </summary>
    public static string? AccessRefusal(string path) => AccessRefusal(path, 0);

    private static string? AccessRefusal(string path, int hops)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (hops > LinkHops) return NetworkMessage;
        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return ServerRefusal(normalized[8..]);
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) && IsDrivePath(normalized[4..])) return LocalRefusal(normalized[4..], hops);
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal) || normalized.StartsWith(@"\??\", StringComparison.Ordinal)) return DeviceMessage;
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal)) return ServerRefusal(normalized[2..]);
        return IsDrivePath(normalized) ? LocalRefusal(normalized, hops) : null;
    }

    private static bool IsDrivePath(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';

    // A local path can still reach a server through a mapped drive or a symbolic link. Attributes and
    // link targets are read from the link itself, so checking never follows a link to its server.
    private static string? LocalRefusal(string path, int hops)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "The path could not be checked.";
        }
        if (!IsDrivePath(full)) return AccessRefusal(full, hops + 1);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (new DriveInfo(full[..1]).DriveType == DriveType.Network)
                {
                    var remote = MappedDrive(full[..2]);
                    return remote is null ? NetworkMessage : ServerRefusal(remote.TrimStart('\\'));
                }
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return "The path could not be checked.";
            }
        }
        var current = full[..3];
        foreach (var part in full[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            if (!attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            FileSystemInfo link = attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(current) : new FileInfo(current);
            if (link.LinkTarget is not { } target) continue;
            var resolved = Path.IsPathFullyQualified(target) ? target : Path.Combine(Path.GetDirectoryName(current)!, target);
            var rest = Path.GetRelativePath(current, full);
            var refusal = AccessRefusal(rest == "." ? resolved : Path.Combine(resolved, rest), hops + 1);
            if (refusal is not null) return refusal;
            return null;
        }
        return null;
    }

    private static string? ServerRefusal(string uncWithoutPrefix)
    {
        var server = uncWithoutPrefix.Split('\\', 2)[0];
        // WebDAV paths name the server as host@SSL or host@port.
        var host = server.Split('@', 2)[0];
        if (host.Length == 0) return NetworkMessage;
        if (Allowlisted(server) || Allowlisted(host)) return null;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal)) return IsLocalNetwork(literal) ? null : NetworkMessage;
        try
        {
            var addresses = Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            return addresses.Length > 0 && addresses.All(IsLocalNetwork) ? null : NetworkMessage;
        }
        catch (Exception exception) when (exception is SocketException or TimeoutException or ArgumentException)
        {
            return NetworkMessage;
        }
    }

    private static bool Allowlisted(string server)
    {
        try
        {
            var file = new FileInfo(AllowlistPath);
            if (!file.Exists || file.Length > 65_536) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array
                && servers.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString()?.Trim(), server, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Loopback, private (RFC 1918), link-local and IPv6 unique local or site-local addresses.</summary>
    public static bool IsLocalNetwork(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 169 && bytes[1] == 254;
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6
            && (address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal || address.IsIPv6SiteLocal);
    }

    private static string? MappedDrive(string drive)
    {
        var length = 1024;
        var buffer = new StringBuilder(length);
        return WNetGetConnection(drive, buffer, ref length) == 0 ? buffer.ToString() : null;
    }

    [DllImport("mpr.dll", EntryPoint = "WNetGetConnectionW", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

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
