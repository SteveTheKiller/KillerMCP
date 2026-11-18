using System.Text.Json;
using KillerMCP.Features.Tools;
using KillerMCP.Models;

namespace KillerMCP.Services.Mcp;

internal static class ToolRegistry
{
    internal const string UnexpectedFailure = "The tool failed unexpectedly. The details are in the agent client's KillerMCP server log.";

    private static IReadOnlyDictionary<string, AppAdapter> Adapters = new Dictionary<string, AppAdapter>();
    private static readonly HashSet<string> CoreToolNames = new(BuiltInTools.Definitions.Select(tool =>
        (string)tool.GetType().GetProperty("name")!.GetValue(tool)!), StringComparer.Ordinal);
    private static readonly HashSet<string> FastLocalTools = new(StringComparer.Ordinal)
    {
        "text_statistics", "encode_base64", "decode_base64", "convert_case", "parse_url", "calculate_percentage", "killermcp_update_status",
    };

    public static bool IsFastLocalCall(JsonElement parameters) => parameters.ValueKind == JsonValueKind.Object
        && parameters.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
        && CoreToolNames.Contains(name.GetString()!) && FastLocalTools.Contains(name.GetString()!);

    public static object[] Tools => BuiltInTools.Definitions.Concat(Adapters.Values.Select(adapter => new
    {
        name = adapter.Tool.Name,
        description = adapter.Tool.Description,
        inputSchema = adapter.Tool.InputSchema,
        annotations = ToolSafety.For(adapter.Tool.Name),
    })).ToArray<object>();

    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement.Clone();

    // Arguments that name a file or folder a tool creates. Each one is checked against AgentPaths
    // before any tool runs, so no tool can write into the KillerMCP or KillerBench settings folders.
    private static readonly HashSet<string> OutputArguments = new(StringComparer.Ordinal) { "output", "outputFolder", "reviewFolder" };

    // Arguments of app tools that name a file or folder to open, alone or in a list. Core tools other
    // than the local file tools take text, so their arguments are never read as paths.
    private static readonly HashSet<string> PathArguments = new(StringComparer.Ordinal)
    {
        "path", "inputs", "images", "files", "data", "pfx", "recipients", "changed", "macroFile", "bookmarks", "root", "folder",
        "output", "outputFolder", "reviewFolder",
    };

    private static readonly HashSet<string> LocalFileTools = new(StringComparer.Ordinal)
    {
        "encode_file_base64_local", "decode_file_base64_local", "write_text_file_local", "check_pdf_signatures_local",
    };

    private static string? PathRefusal(string name, JsonElement arguments)
    {
        if (!Adapters.ContainsKey(name) && !LocalFileTools.Contains(name)) return null;
        foreach (var property in arguments.EnumerateObject())
        {
            if (name == "killershell_read_registry_key" && property.Name == "path") continue;
            if (!PathArguments.Contains(property.Name)) continue;
            var values = property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray().ToArray() : [property.Value];
            foreach (var value in values)
            {
                if (value.ValueKind != JsonValueKind.String) continue;
                var path = value.GetString()!;
                if (AgentPaths.AccessRefusal(path) is { } network) return network;
                var writes = OutputArguments.Contains(property.Name) || name is "decode_file_base64_local" or "write_text_file_local" && property.Name == "path";
                if (writes && AgentPaths.WriteRefusal(path) is { } refusal) return refusal;
                if (!writes && AgentPaths.ReadRefusal(path) is { } readRefusal) return readRefusal;
            }
        }
        return null;
    }

    // The schemas every call is checked against are the ones tools/list advertises.
    private static readonly IReadOnlyDictionary<string, JsonElement> CoreSchemas = BuiltInTools.Definitions.ToDictionary(
        tool => (string)tool.GetType().GetProperty("name")!.GetValue(tool)!,
        tool => JsonSerializer.SerializeToElement(tool.GetType().GetProperty("inputSchema")!.GetValue(tool)),
        StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, JsonElement> AdapterSchemas = new Dictionary<string, JsonElement>();

    public static void SetAdapters(IEnumerable<AppAdapter> adapters)
    {
        var byName = adapters.ToDictionary(adapter => adapter.Tool.Name, StringComparer.Ordinal);
        AdapterSchemas = byName.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value.Tool.InputSchema), StringComparer.Ordinal);
        Adapters = byName;
    }

    private static string? SchemaRefusal(string? name, JsonElement arguments)
    {
        if (name is null) return null;
        var schema = AdapterSchemas.TryGetValue(name, out var adapterSchema) ? adapterSchema
            : CoreSchemas.TryGetValue(name, out var coreSchema) ? coreSchema : default;
        return schema.ValueKind == JsonValueKind.Undefined ? null : ToolSchema.Validate(arguments, schema) is { } problem ? "Invalid arguments: " + problem : null;
    }

    public static object Call(JsonElement parameters, JsonSerializerOptions json, CancellationToken cancellationToken = default)
        => CallAsync(parameters, json, cancellationToken).GetAwaiter().GetResult();

    public static async Task<object> CallAsync(JsonElement parameters, JsonSerializerOptions json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            throw new McpRequestException(-32602, "tools/call needs params with a tool name.");
        }
        var name = nameElement.GetString();
        if (name is null || (!CoreToolNames.Contains(name) && !Adapters.ContainsKey(name)))
            throw new McpRequestException(-32602, "Unknown tool: " + name);
        // MCP makes arguments optional; a tool without inputs is called with an empty object. Arguments
        // that are given must be an object.
        var hasArguments = parameters.TryGetProperty("arguments", out var given) && given.ValueKind != JsonValueKind.Null;
        if (hasArguments && given.ValueKind != JsonValueKind.Object) throw new McpRequestException(-32602, "tools/call arguments must be an object.");
        var arguments = hasArguments ? given : EmptyArguments;
        var loggedName = name is not null && (CoreToolNames.Contains(name) || Adapters.ContainsKey(name))
            ? name : "(unknown)";
        ToolCallLog.Record(loggedName, "started");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if ((SchemaRefusal(name, arguments) ?? (name is null ? null : PathRefusal(name, arguments))) is { } refusal)
            {
                ToolCallLog.Record(loggedName, "error", stopwatch.ElapsedMilliseconds);
                return new { content = new[] { new { type = "text", text = refusal } }, isError = true };
            }
            if (name is not null && Adapters.TryGetValue(name, out var adapter))
            {
                var adapterResult = await adapter.CallAsync(arguments, cancellationToken).ConfigureAwait(false);
                ToolCallLog.Record(loggedName, adapterResult.IsError ? "error" : "ok", stopwatch.ElapsedMilliseconds);
                return adapterResult.IsError
                    ? new { content = new[] { new { type = "text", text = adapterResult.Text } }, isError = true }
                    : new { content = new[] { new { type = "text", text = adapterResult.Text } }, isError = false };
            }

            object value = await BuiltInTools.CallAsync(name, arguments, cancellationToken).ConfigureAwait(false);
            ToolCallLog.Record(loggedName, "ok", stopwatch.ElapsedMilliseconds);
            return new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(value, json) } } };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ToolCallException exception)
        {
            ToolCallLog.Record(loggedName, "error", stopwatch.ElapsedMilliseconds);
            return new { content = new[] { new { type = "text", text = exception.Message } }, isError = true };
        }
        catch (Exception exception)
        {
            ToolCallLog.Record(loggedName, "error", stopwatch.ElapsedMilliseconds);
            // An unexpected exception can name local paths, so the agent gets fixed text and the detail goes
            // to standard error, which the client keeps in its local server log.
            Console.Error.WriteLine("KillerMCP " + loggedName + " failed: " + exception.GetType().Name + ": " + exception.Message);
            return new { content = new[] { new { type = "text", text = UnexpectedFailure } }, isError = true };
        }
    }
}
