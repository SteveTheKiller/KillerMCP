using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

public sealed record RuntimeTool(string Name, string Description, JsonObject InputSchema);
public sealed record AppCallResult(string Text, bool IsError = false);
public sealed record AppAdapter(RuntimeTool Tool, Func<JsonElement, CancellationToken, Task<AppCallResult>> CallAsync);
