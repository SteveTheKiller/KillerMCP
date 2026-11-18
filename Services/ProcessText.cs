using System.Security.Principal;
using System.Text;

namespace KillerMCP;

internal static class ProcessText
{
    // Quotes an argument the way the Windows command line parser reads it back, including a trailing
    // backslash before the closing quote, which the plain replace-the-quotes version got wrong.
    internal static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0) return value;
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            slashes = 0;
            quoted.Append(character);
        }
        quoted.Append('\\', slashes * 2);
        return quoted.Append('"').ToString();
    }

    // Session-local names carry the account SID, so another program in the same session cannot hold the
    // name first and quietly suppress update notices.
    internal static string LocalName(string name)
    {
        string? sid = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().User?.Value : null;
        return @"Local\" + name + (sid is null ? "" : "-" + sid);
    }

    internal static string? TestSeam(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // A test file seam must name a fully qualified file with the expected extension. An administrator or
    // SYSTEM host also requires it inside its own temp folder: a standard user can plant variables that an
    // elevated process inherits, but can already write that folder, so the seam hands them nothing new.
    // CI runners are administrators, which is why elevated runs are narrowed instead of refused.
    internal static string? TestPath(string name, string extension)
    {
        string? value = TestSeam(name);
        if (value is null || !Path.IsPathFullyQualified(value) || !Path.GetExtension(value).Equals(extension, StringComparison.OrdinalIgnoreCase)) return null;
        string full = Path.GetFullPath(value);
        if (!IsElevated()) return full;
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        return full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    internal static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
