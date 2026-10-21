using System.Text.RegularExpressions;

namespace KillerMCP;

internal static class KillerPdfCapabilities
{
    internal static bool Supports(string help, string marker)
    {
        if (help.Contains(marker, StringComparison.Ordinal)) return true;
        string command = marker.Split(' ', 2)[0].TrimStart('-');
        return Regex.IsMatch(help, @"^\s*killerpdf(?:\.exe)?\s+" + Regex.Escape(command) + @"(?=\s|$)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
