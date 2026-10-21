namespace KillerMCP.Models;

/// <summary>A request the server rejects with a JSON-RPC error code instead of a result.</summary>
internal sealed class McpRequestException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
