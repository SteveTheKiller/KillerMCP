using System.Diagnostics;
using System.Text.Json;

namespace KillerMCP.Clients;

public static class ClientConfiguration
{
    public static IReadOnlyList<string> RegisterAll(string executablePath, IReadOnlyDictionary<string, string?>? environment = null) => Configure(executablePath, remove: false, environment);
    public static IReadOnlyList<string> RemoveAll(string executablePath, IReadOnlyDictionary<string, string?>? environment = null) => Configure(executablePath, remove: true, environment);

    private static IReadOnlyList<string> Configure(string executablePath, bool remove, IReadOnlyDictionary<string, string?>? environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath)) throw new ArgumentException("KillerMCP executable path must be absolute.");
        var values = environment ?? Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(item => (string)item.Key, item => item.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();

        var codex = FindCodex(values);
        if (codex is not null) messages.Add(remove ? RemoveCodex(codex, executablePath) : RegisterCodex(codex, executablePath));

        foreach (var client in JsonClients(values))
        {
            if (remove)
            {
                if (ClientRegistration.RemoveJsonClient(client.Path, client.Name, executablePath, client.IncludeStdioType, client.Name == "Claude Desktop" ? "KillerMCP" : "killermcp")) messages.Add($"KillerMCP was removed from {client.Name}.");
            }
            else
            {
                messages.Add(ClientRegistration.RegisterJsonClient(client.Path, client.Name, executablePath, client.IncludeStdioType, client.Name == "Claude Desktop" ? "KillerMCP" : "killermcp"));
            }
        }
        return messages;
    }

    public static IReadOnlyList<JsonClient> JsonClients(IReadOnlyDictionary<string, string?>? environment = null)
    {
        var values = environment ?? Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(item => (string)item.Key, item => item.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        var profile = Value(values, "USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Value(values, "APPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Value(values, "LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Value(values, "ProgramFiles") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var clients = new List<JsonClient>();
        Add(clients, values, "KILLERMCP_TEST_CLAUDE_CONFIG", "Claude Code", Path.Combine(profile, ".claude.json"),
            FindExecutable("CLAUDE_CLI_PATH", OperatingSystem.IsWindows() ? "claude.exe" : "claude", values) is not null || File.Exists(Path.Combine(profile, ".claude.json")), true);
        Add(clients, values, "KILLERMCP_TEST_CURSOR_CONFIG", "Cursor", Path.Combine(profile, ".cursor", "mcp.json"),
            FindOnPath(OperatingSystem.IsWindows() ? "cursor.exe" : "cursor", values) is not null || Directory.Exists(Path.Combine(profile, ".cursor")) || File.Exists(Path.Combine(local, "Programs", "Cursor", "Cursor.exe")) || File.Exists(Path.Combine(programFiles, "Cursor", "Cursor.exe")));
        Add(clients, values, "KILLERMCP_TEST_COPILOT_CONFIG", "GitHub Copilot", Path.Combine(profile, ".copilot", "mcp-config.json"),
            FindOnPath(OperatingSystem.IsWindows() ? "copilot.exe" : "copilot", values) is not null || FindOnPath(OperatingSystem.IsWindows() ? "code.exe" : "code", values) is not null || Directory.Exists(Path.Combine(profile, ".copilot")));
        Add(clients, values, "KILLERMCP_TEST_GEMINI_CONFIG", "Gemini CLI", Path.Combine(profile, ".gemini", "settings.json"),
            FindOnPath(OperatingSystem.IsWindows() ? "gemini.exe" : "gemini", values) is not null || Directory.Exists(Path.Combine(profile, ".gemini")));
        Add(clients, values, "KILLERMCP_TEST_WINDSURF_CONFIG", "Windsurf", Path.Combine(profile, ".codeium", "windsurf", "mcp_config.json"),
            FindOnPath(OperatingSystem.IsWindows() ? "windsurf.exe" : "windsurf", values) is not null || Directory.Exists(Path.Combine(profile, ".codeium", "windsurf")) || File.Exists(Path.Combine(local, "Programs", "Windsurf", "Windsurf.exe")));
        Add(clients, values, "KILLERMCP_TEST_CLAUDE_DESKTOP_CONFIG", "Claude Desktop", Path.Combine(roaming, "Claude", "claude_desktop_config.json"),
            Directory.Exists(Path.Combine(roaming, "Claude")) || File.Exists(Path.Combine(local, "Programs", "Claude", "Claude.exe")) || File.Exists(Path.Combine(local, "AnthropicClaude", "claude.exe")) || File.Exists(Path.Combine(programFiles, "Claude", "Claude.exe")));
        return clients;
    }

    private static string RegisterCodex(string codex, string executablePath)
    {
        var existing = Run(codex, ["mcp", "get", "killermcp", "--json"]);
        if (existing.ExitCode == 0)
        {
            if (MatchesCodex(existing.StandardOutput, executablePath)) return "Codex already has the correct KillerMCP connection.";
            if (!TryReadLegacyCodex(existing.StandardOutput, executablePath, out var legacyCommand, out var legacyArguments)) throw new InvalidOperationException("Codex already has a different killermcp connection. Its settings were left unchanged.");
            var removed = Run(codex, ["mcp", "remove", "killermcp"]);
            if (removed.ExitCode != 0) throw new InvalidOperationException("Codex could not replace the previous KillerMCP connection: " + removed.StandardError.Trim());
            var upgraded = Run(codex, ["mcp", "add", "killermcp", "--", executablePath]);
            if (upgraded.ExitCode != 0)
            {
                Run(codex, ["mcp", "add", "killermcp", "--", legacyCommand, .. legacyArguments]);
                throw new InvalidOperationException("Codex could not add the native KillerMCP connection: " + upgraded.StandardError.Trim());
            }
            var upgradedState = Run(codex, ["mcp", "get", "killermcp", "--json"]);
            if (upgradedState.ExitCode != 0 || !MatchesCodex(upgradedState.StandardOutput, executablePath)) throw new InvalidOperationException("Codex did not save the native KillerMCP connection.");
            return "KillerMCP was upgraded in Codex.";
        }
        if (!existing.StandardError.Contains("No MCP server named", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Codex could not read its MCP configuration: " + existing.StandardError.Trim());
        var added = Run(codex, ["mcp", "add", "killermcp", "--", executablePath]);
        if (added.ExitCode != 0) throw new InvalidOperationException("Codex could not add the KillerMCP connection: " + added.StandardError.Trim());
        var saved = Run(codex, ["mcp", "get", "killermcp", "--json"]);
        if (saved.ExitCode != 0 || !MatchesCodex(saved.StandardOutput, executablePath)) throw new InvalidOperationException("Codex did not save the expected KillerMCP connection.");
        return "KillerMCP was added to Codex.";
    }

    private static string RemoveCodex(string codex, string executablePath)
    {
        var existing = Run(codex, ["mcp", "get", "killermcp", "--json"]);
        if (existing.ExitCode != 0)
        {
            if (existing.StandardError.Contains("No MCP server named", StringComparison.OrdinalIgnoreCase)) return "Codex has no KillerMCP connection.";
            throw new InvalidOperationException("Codex could not read its MCP configuration: " + existing.StandardError.Trim());
        }
        if (!MatchesCodex(existing.StandardOutput, executablePath)) throw new InvalidOperationException("Codex has a different killermcp connection. It was left unchanged.");
        var removed = Run(codex, ["mcp", "remove", "killermcp"]);
        if (removed.ExitCode != 0) throw new InvalidOperationException("Codex could not remove its KillerMCP connection: " + removed.StandardError.Trim());
        return "KillerMCP was removed from Codex.";
    }

    private static bool MatchesCodex(string json, string executablePath)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var transport = document.RootElement.GetProperty("transport");
            return transport.GetProperty("type").GetString() == "stdio" && PathEquals(transport.GetProperty("command").GetString()!, executablePath) && transport.GetProperty("args").GetArrayLength() == 0;
        }
        catch { return false; }
    }

    private static bool TryReadLegacyCodex(string json, string executablePath, out string command, out string[] arguments)
    {
        command = string.Empty;
        arguments = [];
        try
        {
            using var document = JsonDocument.Parse(json);
            var transport = document.RootElement.GetProperty("transport");
            var directory = Path.GetDirectoryName(executablePath)!;
            command = transport.GetProperty("command").GetString()!;
            arguments = transport.GetProperty("args").EnumerateArray().Select(value => value.GetString()!).ToArray();
            return transport.GetProperty("type").GetString() == "stdio"
                && PathEquals(command, Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node"))
                && arguments.Length == 1
                && PathEquals(arguments[0], Path.Combine(directory, "killermcp.mjs"));
        }
        catch { command = string.Empty; arguments = []; return false; }
    }

    private static void Add(List<JsonClient> clients, IReadOnlyDictionary<string, string?> environment, string overrideName, string name, string normalPath, bool installed, bool includeStdioType = false)
    {
        var overridePath = Value(environment, overrideName);
        if (!string.IsNullOrWhiteSpace(overridePath)) clients.Add(new JsonClient(name, Path.GetFullPath(overridePath), includeStdioType));
        else if (installed) clients.Add(new JsonClient(name, Path.GetFullPath(normalPath), includeStdioType));
    }

    private static string? FindExecutable(string overrideName, string executable, IReadOnlyDictionary<string, string?> environment)
    {
        var configured = Value(environment, overrideName);
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? Path.GetFullPath(configured) : FindOnPath(executable, environment);
    }

    private static string? FindCodex(IReadOnlyDictionary<string, string?> environment)
    {
        var configured = Value(environment, "CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        var found = FindOnPath(OperatingSystem.IsWindows() ? "codex.exe" : "codex", environment);
        if (found is not null || !OperatingSystem.IsWindows()) return found;
        var local = Value(environment, "LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "OpenAI", "Codex", "bin");
        try
        {
            return Directory.Exists(root)
                ? Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
        }
        catch { return null; }
    }

    private static string? FindOnPath(string executable, IReadOnlyDictionary<string, string?> environment)
    {
        foreach (var directory in (Value(environment, "PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var path = Path.Combine(directory.Trim('"'), executable); if (File.Exists(path)) return Path.GetFullPath(path); }
            catch { }
        }
        return null;
    }

    private static CommandResult Run(string executable, IEnumerable<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = executable, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) return new CommandResult(1, string.Empty, "Process did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Client configuration command timed out."); }
        Task.WaitAll(output, error);
        return new CommandResult(process.ExitCode, output.Result, error.Result);
    }

    private static string? Value(IReadOnlyDictionary<string, string?> values, string name) => values.TryGetValue(name, out var value) ? value : values.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
}

public sealed record JsonClient(string Name, string Path, bool IncludeStdioType);
