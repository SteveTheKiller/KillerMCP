using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Configuration;

/// <summary>A client's settings file has comments, which rewriting would drop, so it was left unchanged.</summary>
public sealed class CommentedSettingsException(string message) : IOException(message);

public static class ClientRegistration
{
    internal sealed record Change(string Path);
    private const string ServerName = "killermcp";
    private const int Attempts = 3;
    // The settings belong to other apps, so text in their entries is written back as it was instead of escaped.
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Tests use this to change a settings file between KillerMCP reading it and replacing it.
    internal static Action<string>? BeforeReplace;
    internal static Action<string>? BeforePublish;

    public static string RegisterJsonClient(string configurationPath, string clientName, string executablePath, bool includeStdioType = false, string serverName = ServerName)
        => RegisterTracked(configurationPath, clientName, executablePath, includeStdioType, serverName).Message;

    internal static (string Message, Change? Change) RegisterTracked(string configurationPath, string clientName, string executablePath, bool includeStdioType, string serverName)
    {
        for (var attempt = 1; ; attempt++)
        {
            var (root, message, original) = Plan(configurationPath, clientName, executablePath, includeStdioType, serverName);
            if (root is null) return (message, null);
            if (Write(configurationPath, root, original, out var change)) return (message, change);
            if (attempt == Attempts) throw new IOException(KeptChanging(clientName, configurationPath));
        }
    }

    /// <summary>Throws the same errors RegisterJsonClient would, without writing anything, so setup can
    /// check every client before it changes any.</summary>
    public static void CheckJsonClient(string configurationPath, string clientName, string executablePath, bool includeStdioType = false, string serverName = ServerName) =>
        Plan(configurationPath, clientName, executablePath, includeStdioType, serverName);

    // Returns the settings to write, or null when the client already has the right connection, with the
    // file text they were planned from.
    private static (JsonObject? Root, string Message, string? Original) Plan(string configurationPath, string clientName, string executablePath, bool includeStdioType, string serverName)
    {
        ValidateArguments(configurationPath, clientName, executablePath);
        var root = ReadRoot(configurationPath, clientName, out var commented, out var original);
        var (planned, message) = Plan(root, commented, configurationPath, clientName, executablePath, includeStdioType, serverName);
        return (planned, message, original);
    }

    private static (JsonObject? Root, string Message) Plan(JsonObject root, bool commented, string configurationPath, string clientName, string executablePath, bool includeStdioType, string serverName)
    {
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
        RefuseInterruptedUpdate(configurationPath);
        for (var attempt = 1; ; attempt++)
        {
            if (!File.Exists(configurationPath))
            {
                return false;
            }

            var root = ReadRoot(configurationPath, clientName, out var commented, out var original);
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
            if (Write(configurationPath, root, original)) return true;
            if (attempt == Attempts) throw new IOException(KeptChanging(clientName, configurationPath));
        }
    }

    private static string KeptChanging(string clientName, string configurationPath) =>
        $"{clientName} kept changing {configurationPath} while KillerMCP was updating it. Close {clientName} and run setup again.";

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
    private static JsonObject ReadRoot(string path, string clientName, out bool commented, out string? original)
    {
        RefuseInterruptedUpdate(path);
        commented = false;
        original = null;
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        original = text;
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

    private static void RefuseInterruptedUpdate(string path)
    {
        var recovery = TargetPath(path) + ".killermcp-recovery";
        if (File.Exists(recovery)) throw new IOException("An interrupted settings update needs review. Recovery copy: " + recovery);
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

    // Hold the settings file exclusively from comparison through publication. A durable recovery copy
    // protects an existing file if the process or machine stops during the write.
    private static bool Write(string path, JsonObject root, string? original)
        => Write(path, root, original, out _);

    private static bool Write(string path, JsonObject root, string? original, out Change? change)
    {
        change = null;
        var target = TargetPath(path);
        var directory = Path.GetDirectoryName(target)
            ?? throw new InvalidOperationException("The MCP configuration directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        var recovery = target + ".killermcp-recovery";
        if (File.Exists(recovery)) throw new IOException("An interrupted settings update needs review. Recovery copy: " + recovery);
        try
        {
            File.WriteAllText(temporary, root.ToJsonString(Json) + Environment.NewLine, new UTF8Encoding(false));
            BeforeReplace?.Invoke(target);
            using var held = new FileStream(target, original is null ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            byte[] previous = new byte[held.Length];
            held.ReadExactly(previous);
            string? current = null;
            if (original is not null)
            {
                using var reader = new StreamReader(new MemoryStream(previous), Encoding.UTF8, true);
                current = reader.ReadToEnd();
            }
            if (!string.Equals(current, original, StringComparison.Ordinal)) return false;
            if (original is not null)
            {
                using var backup = new FileStream(recovery, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                backup.Write(previous);
                backup.Flush(true);
            }
            BeforePublish?.Invoke(target);
            try
            {
                held.Position = 0;
                var written = File.ReadAllBytes(temporary);
                held.Write(written);
                held.SetLength(written.Length);
                held.Flush(true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Settings update failed. Review " + target + (original is null ? "." : "; recovery copy: " + recovery), failure);
            }
            if (original is not null) File.Delete(recovery);
            change = new Change(path);
            return true;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    internal static string TargetPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            return File.Exists(fullPath) && new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true) is { } target ? target.FullName : fullPath;
        }
        catch (IOException)
        {
            return fullPath;
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
