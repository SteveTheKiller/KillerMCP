using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Clients;

public static class ClientRegistration
{
    private const string ServerName = "killermcp";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string RegisterJsonClient(string configurationPath, string clientName, string executablePath, bool includeStdioType = false)
    {
        ValidateArguments(configurationPath, clientName, executablePath);
        var root = ReadRoot(configurationPath, clientName);
        var servers = ReadServers(root, clientName);
        if (servers.TryGetPropertyValue(ServerName, out var existing))
        {
            if (Matches(existing, executablePath, includeStdioType))
            {
                return $"{clientName} already has the correct KillerMCP connection.";
            }
            if (!MatchesLegacy(existing, executablePath, includeStdioType)) throw new InvalidOperationException($"{clientName} already has a different killermcp connection. Its settings were left unchanged.");
            servers[ServerName] = Registration(executablePath, includeStdioType);
            Write(configurationPath, root);
            return $"KillerMCP was upgraded in {clientName}.";
        }

        servers[ServerName] = Registration(executablePath, includeStdioType);
        Write(configurationPath, root);
        return $"KillerMCP was added to {clientName}.";
    }

    public static bool RemoveJsonClient(string configurationPath, string clientName, string executablePath, bool includeStdioType = false)
    {
        ValidateArguments(configurationPath, clientName, executablePath);
        if (!File.Exists(configurationPath))
        {
            return false;
        }

        var root = ReadRoot(configurationPath, clientName);
        var servers = ReadServers(root, clientName);
        if (!servers.TryGetPropertyValue(ServerName, out var existing))
        {
            return false;
        }

        if (!Matches(existing, executablePath, includeStdioType))
        {
            throw new InvalidOperationException($"{clientName} has a different killermcp connection. It was left unchanged.");
        }

        servers.Remove(ServerName);
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

    private static JsonObject ReadRoot(string path, string clientName)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException($"{clientName} MCP configuration is invalid.");
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
