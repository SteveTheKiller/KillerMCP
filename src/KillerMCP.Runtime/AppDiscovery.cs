namespace KillerMCP.Runtime;

public static class AppDiscovery
{
    private sealed record Definition(string Variable, string Directory, string Executable);

    private static readonly IReadOnlyDictionary<string, Definition> Apps = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase)
    {
        ["killendar"] = new("KILLENDAR_CLI", "Killendar", "Killendar.exe"),
        ["killerbench"] = new("KILLERBENCH_CLI", "KillerBench", "killerbench-cli.exe"),
        ["killernotes"] = new("KILLERNOTES_CLI", "KillerNotes", "KillerNotes.exe"),
        ["killerpdf"] = new("KILLERPDF_CLI", "KillerPDF", "KillerPDF.App.exe"),
        ["killerscan"] = new("KILLERSCAN_CLI", "KillerScan", "KillerScan.exe"),
        ["killershell"] = new("KILLERSHELL_CLI", "KillerShell", "KillerShell.exe"),
    };

    public static string? Discover(string app, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (!Apps.TryGetValue(app, out var definition))
        {
            throw new ArgumentException($"Unknown app: {app}", nameof(app));
        }

        environment ??= ReadEnvironment();
        if (environment.TryGetValue(definition.Variable, out var configured))
        {
            return IsFile(configured) ? Path.GetFullPath(configured!) : null;
        }

        foreach (var candidate in Candidates(definition, environment))
        {
            if (IsFile(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> DiscoverAll(IReadOnlyDictionary<string, string?>? environment = null) => Apps.Keys
        .Select(app => (App: app, Path: Discover(app, environment)))
        .Where(result => result.Path is not null)
        .ToDictionary(result => result.App, result => result.Path!, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Candidates(Definition definition, IReadOnlyDictionary<string, string?> environment)
    {
        if (environment.TryGetValue("LOCALAPPDATA", out var local) && !string.IsNullOrWhiteSpace(local))
        {
            yield return Path.Combine(local, "Programs", definition.Directory, definition.Executable);
        }

        if (environment.TryGetValue("ProgramFiles", out var machine) && !string.IsNullOrWhiteSpace(machine))
        {
            yield return Path.Combine(machine, definition.Directory, definition.Executable);
        }
    }

    private static bool IsFile(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironment() => Environment.GetEnvironmentVariables()
        .Cast<System.Collections.DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => entry.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
}
