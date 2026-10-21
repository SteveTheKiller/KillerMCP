using System.Text.Json;
using KillerMCP.Features.Tools;
using KillerMCP.Models;

namespace KillerMCP.Services.Mcp;

internal static class ToolRegistry
{
    private static IReadOnlyDictionary<string, AppAdapter> Adapters = new Dictionary<string, AppAdapter>();
    private static readonly HashSet<string> CoreToolNames = new(BuiltInTools.Definitions.Select(tool =>
        (string)tool.GetType().GetProperty("name")!.GetValue(tool)!), StringComparer.Ordinal);

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
        "path", "inputs", "images", "files", "data", "pfx", "recipients", "changed", "macroFile", "root", "folder",
        "output", "outputFolder", "reviewFolder",
    };

    private static readonly HashSet<string> LocalFileTools = new(StringComparer.Ordinal)
    {
        "encode_file_base64_local", "decode_file_base64_local", "check_pdf_signatures_local",
    };

    private static string? PathRefusal(string name, JsonElement arguments)
    {
        if (!Adapters.ContainsKey(name) && !LocalFileTools.Contains(name)) return null;
        foreach (var property in arguments.EnumerateObject())
        {
            if (!PathArguments.Contains(property.Name)) continue;
            var values = property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray().ToArray() : [property.Value];
            foreach (var value in values)
            {
                if (value.ValueKind != JsonValueKind.String) continue;
                var path = value.GetString()!;
                if (AgentPaths.AccessRefusal(path) is { } network) return network;
                var writes = OutputArguments.Contains(property.Name) || name == "decode_file_base64_local" && property.Name == "path";
                if (writes && AgentPaths.WriteRefusal(path) is { } refusal) return refusal;
            }
        }
        return null;
    }

    public static void SetAdapters(IEnumerable<AppAdapter> adapters) => Adapters = adapters.ToDictionary(adapter => adapter.Tool.Name, StringComparer.Ordinal);

    public static object Call(JsonElement parameters, JsonSerializerOptions json, CancellationToken cancellationToken = default)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            throw new McpRequestException(-32602, "tools/call needs params with a tool name.");
        }
        var name = nameElement.GetString();
        // MCP makes arguments optional; a tool without inputs is called with an empty object.
        var arguments = parameters.TryGetProperty("arguments", out var given) && given.ValueKind == JsonValueKind.Object
            ? given
            : EmptyArguments;
        var loggedName = name is not null && (CoreToolNames.Contains(name) || Adapters.ContainsKey(name))
            ? name : "(unknown)";
        ToolCallLog.Record(loggedName, "started");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (name is not null && PathRefusal(name, arguments) is { } refusal)
            {
                ToolCallLog.Record(loggedName, "error", stopwatch.ElapsedMilliseconds);
                return new { content = new[] { new { type = "text", text = refusal } }, isError = true };
            }
            if (name is not null && Adapters.TryGetValue(name, out var adapter))
            {
                var adapterResult = adapter.CallAsync(arguments, cancellationToken).GetAwaiter().GetResult();
                ToolCallLog.Record(loggedName, adapterResult.IsError ? "error" : "ok", stopwatch.ElapsedMilliseconds);
                return adapterResult.IsError
                    ? new { content = new[] { new { type = "text", text = adapterResult.Text } }, isError = true }
                    : new { content = new[] { new { type = "text", text = adapterResult.Text } }, isError = false };
            }

            object value = BuiltInTools.Call(name, arguments);
            ToolCallLog.Record(loggedName, "ok", stopwatch.ElapsedMilliseconds);
            return new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(value, json) } } };
        }
        catch (ToolCallException exception)
        {
            ToolCallLog.Record(loggedName, "error", stopwatch.ElapsedMilliseconds);
            return new { content = new[] { new { type = "text", text = exception.Message } }, isError = true };
        }
        catch (Exception exception)
        {
            ToolCallLog.Record(loggedName, "error", stopwatch.ElapsedMilliseconds);
            var message = exception.Message.Length <= 1024 ? exception.Message : exception.Message[..1024];
            return new { content = new[] { new { type = "text", text = "The tool failed: " + message } }, isError = true };
        }
    }
}
