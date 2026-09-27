using System.Diagnostics;

namespace KillerMCP.Runtime;

internal static class AppVersion
{
    public static bool IsAtLeast(string path, Version minimum)
    {
        if (!OperatingSystem.IsWindows() || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var value = FileVersionInfo.GetVersionInfo(path).FileVersion;
        return Version.TryParse(value?.Split(' ')[0], out var actual) && actual >= minimum;
    }
}
