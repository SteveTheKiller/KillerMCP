using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace KillerMCP.Notify;

/// <summary>The release notes for one version, read from the host's update status file, which
/// keeps the GitHub release body beside the version it describes.</summary>
internal sealed record ReleaseNotes(string Summary, IReadOnlyList<ReleaseNotes.Section> Sections)
{
    internal sealed record Section(string? Heading, IReadOnlyList<string> Items);

    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Emphasis = new(@"(\*\*|__|`)", RegexOptions.Compiled);

    internal bool IsEmpty => Summary.Length == 0 && Sections.Count == 0;

    internal static string ReleaseUrl(string version) =>
        "https://github.com/SteveTheKiller/KillerMCP/releases/tag/v" + version;

    internal static string DefaultStatusPath()
    {
        string? configured = Environment.GetEnvironmentVariable("KILLERMCP_UPDATE_CACHE");
        return !string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(configured) : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP", "update-status.json");
    }

    /// <summary>Returns the notes for <paramref name="version"/>, or null when the status file is
    /// missing, unreadable, or describes another version.</summary>
    internal static ReleaseNotes? Read(string statusPath, string version)
    {
        try
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(statusPath));
            if (root is null
                || !root.TryGetValue("LatestVersion", out object? latest) || latest is not string latestText
                || !string.Equals(latestText.TrimStart('v', 'V'), version.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase)
                || !root.TryGetValue("Notes", out object? notes) || notes is not string notesText)
                return null;
            ReleaseNotes parsed = Parse(notesText);
            return parsed.IsEmpty ? null : parsed;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads the changelog-style release body: an opening paragraph, then ### headings with
    /// "- " bullets. Links keep their text and inline emphasis marks are dropped.</summary>
    internal static ReleaseNotes Parse(string markdown)
    {
        var summary = new StringBuilder();
        var sections = new List<Section>();
        string? heading = null;
        List<string>? items = null;
        foreach (string raw in markdown.Replace("\r", string.Empty).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                if (items is not null) sections.Add(new Section(heading, items));
                heading = Clean(line.TrimStart('#'));
                items = new List<string>();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                items ??= new List<string>();
                items.Add(Clean(line.Substring(2)));
            }
            else if (items is { Count: > 0 } && raw.StartsWith(" ", StringComparison.Ordinal))
            {
                items[items.Count - 1] += " " + Clean(line);
            }
            else if (items is null)
            {
                if (summary.Length > 0) summary.Append(' ');
                summary.Append(Clean(line));
            }
        }
        if (items is { Count: > 0 }) sections.Add(new Section(heading, items));
        return new ReleaseNotes(summary.ToString(), sections);
    }

    private static string Clean(string text) => Emphasis.Replace(Link.Replace(text, "$1"), string.Empty).Trim();
}
