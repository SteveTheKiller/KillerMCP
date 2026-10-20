using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Clients;

/// <summary>A client's settings file has comments, which rewriting would drop, so it was left unchanged.</summary>
public sealed class CommentedSettingsException(string message) : IOException(message);

public static class ClientRegistration
{
    private const string ServerName = "killermcp";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string RegisterJsonClient(string configurationPath, string clientName, string executablePath, bool includeStdioType = false, string serverName = ServerName)
    {
        var (root, message) = Plan(configurationPath, clientName, executablePath, includeStdioType, serverName);
        if (root is not null) Write(configurationPath, root);
        return message;
    }

    /// <summary>Throws the same errors RegisterJsonClient would, without writing anything, so setup can
    /// check every client before it changes any.</summary>
    public static void CheckJsonClient(string configurationPath, string clientName, string executablePath, bool includeStdioType = false, string serverName = ServerName) =>
        Plan(configurationPath, clientName, executablePath, includeStdioType, serverName);

    // Returns the settings to write, or null when the client already has the right connection.
    private static (JsonObject? Root, string Message) Plan(string configurationPath, string clientName, string executablePath, bool includeStdioType, string serverName)
    {
        ValidateArguments(configurationPath, clientName, executablePath);
        var root = ReadRoot(configurationPath, clientName, out var commented);
        var servers = ReadServers(root, clientName);
        if (commented)
        {
            if (servers.TryGetPropertyValue(serverName, out var current) && Matches(current, executablePath, includeStdioType) && (serverName == ServerName || !servers.ContainsKey(ServerName)))
                return (null, $"{clientName} already has the correct KillerMCP connection.");
            throw new CommentedSettingsException($"{clientName} settings at {configurationPath} contain comments, so KillerMCP left them unchanged. Add this entry inside \"mcpServers\": \"{serverName}\": {Registration(executablePath, includeStdioType).ToJsonString()}");
        }
        if (serverName != ServerName && servers.TryGetPropertyValue(ServerName, out var oldEntry))
        {
            if (!Matches(oldEntry, executablePath, includeStdioType) && !MatchesLegacy(oldEntry, executablePath, includeStdioType))
                throw new InvalidOperationException($"{clientName} has a different {ServerName} connection. Its settings were left unchanged.");
            if (servers.ContainsKey(serverName) && !Matches(servers[serverName], executablePath, includeStdioType))
                throw new InvalidOperationException($"{clientName} has a different {serverName} connection. Its settings were left unchanged.");
            servers.Remove(ServerName);
            servers[serverName] = Registration(executablePath, includeStdioType);
            return (root, $"KillerMCP was renamed in {clientName}.");
        }
        if (servers.TryGetPropertyValue(serverName, out var existing))
        {
            if (Matches(existing, executablePath, includeStdioType))
            {
                return (null, $"{clientName} already has the correct KillerMCP connection.");
            }
            if (!MatchesLegacy(existing, executablePath, includeStdioType)) throw new InvalidOperationException($"{clientName} already has a different killermcp connection. Its settings were left unchanged.");
            servers[serverName] = Registration(executablePath, includeStdioType);
            return (root, $"KillerMCP was upgraded in {clientName}.");
        }

        servers[serverName] = Registration(executablePath, includeStdioType);
        return (root, $"KillerMCP was added to {clientName}.");
    }

    public static bool RemoveJsonClient(string configurationPath, string clientName, string executablePath, bool includeStdioType = false, string serverName = ServerName)
    {
        ValidateArguments(configurationPath, clientName, executablePath);
        if (!File.Exists(configurationPath))
        {
            return false;
        }

        var root = ReadRoot(configurationPath, clientName, out var commented);
        var servers = ReadServers(root, clientName);
        if (!servers.TryGetPropertyValue(serverName, out var existing))
        {
            return false;
        }

        if (!Matches(existing, executablePath, includeStdioType))
        {
            throw new InvalidOperationException($"{clientName} has a different killermcp connection. It was left unchanged.");
        }

        if (commented) throw new CommentedSettingsException($"{clientName} settings at {configurationPath} contain comments, so KillerMCP left them unchanged.");
        servers.Remove(serverName);
        Write(configurationPath, root);
        return true;
    }

    public static bool Matches(JsonNode? value, string executablePath, bool includeStdioType = false)
    {
        if (value is not JsonObject registration
            || registration["command"]?.GetValue<string>() is not string command
            || registration["args"] is not JsonArray args
            || args.Count != 0
            || !PathEquals(command, executablePath))
        {
            return false;
        }

        return !includeStdioType || registration["type"]?.GetValue<string>() == "stdio";
    }

    public static bool MatchesLegacy(JsonNode? value, string executablePath, bool includeStdioType = false)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (directory is null || value is not JsonObject registration
            || registration["command"]?.GetValue<string>() is not string command
            || registration["args"] is not JsonArray args
            || args.Count != 1
            || args[0]?.GetValue<string>() is not string script
            || !PathEquals(command, Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node"))
            || !PathEquals(script, Path.Combine(directory, "killermcp.mjs")))
        {
            return false;
        }
        return !includeStdioType || registration["type"]?.GetValue<string>() == "stdio";
    }

    private static JsonObject Registration(string executablePath, bool includeStdioType)
    {
        var registration = new JsonObject { ["command"] = executablePath, ["args"] = new JsonArray() };
        if (includeStdioType) registration["type"] = "stdio";
        return registration;
    }

    // An empty file is an empty object and trailing commas are accepted. A file that parses only when
    // comments are skipped is reported as commented, so callers never write it back without them.
    private static JsonObject ReadRoot(string path, string clientName, out bool commented)
    {
        commented = false;
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject
                ?? throw new InvalidDataException($"{clientName} MCP configuration is invalid.");
        }
        catch (JsonException)
        {
        }
        try
        {
            var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                ?? throw new InvalidDataException($"{clientName} MCP configuration is invalid.");
            commented = true;
            return root;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{clientName} MCP configuration is invalid.", exception);
        }
    }

    private static JsonObject ReadServers(JsonObject root, string clientName)
    {
        if (!root.TryGetPropertyValue("mcpServers", out var value) || value is null)
        {
            var servers = new JsonObject();
            root["mcpServers"] = servers;
            return servers;
        }

        return value as JsonObject
            ?? throw new InvalidDataException($"{clientName} MCP server configuration is invalid.");
    }

    private static void Write(string path, JsonObject root)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The MCP configuration directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(Json) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ValidateArguments(string configurationPath, string clientName, string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(configurationPath) || !Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("Client configuration and executable paths must be absolute.");
        }
    }

    private static bool PathEquals(string left, string right) => string.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
