using System.Reflection;
using System.Text;
using System.Text.Json;
using KillerMCP.Features.Tools;
using KillerMCP.Models;

namespace KillerMCP.Services.Mcp;

internal static class McpServer
{
    // The largest legitimate message is a PDF made from up to a million characters of content, which
    // JSON escaping can grow several times over.
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private const int MaximumResultCharacters = 32 * 1024 * 1024;
    private const int MaximumRunningCalls = 32;

    public static async Task RunAsync()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version!;
        var currentVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        BuiltInTools.Status = await UpdateChecker.CheckForUpdateAsync(currentVersion);
        UpdateChecker.StartMonitoring(currentVersion, BuiltInTools.Status, status => BuiltInTools.Status = status);
        var adaptersReady = RegisterAdaptersAsync(AppCatalog.LoadAsync());

        // Tool calls run on their own tasks so a slow one never holds up ping, tools/list or other calls.
        // Each keeps a cancellation source by request id for notifications/cancelled, and every reply is
        // written as one whole line.
        var outputGate = new object();
        var running = new System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var calls = new List<Task>();
        void Send(object message)
        {
            var text = JsonSerializer.Serialize(message, json);
            lock (outputGate) Console.WriteLine(text);
        }
        object Failure(JsonElement id, Exception exception)
        {
            var (code, message) = exception is McpRequestException known
                ? (known.Code, known.Message)
                : (-32603, "Internal error: " + (exception.Message.Length <= 1024 ? exception.Message : exception.Message[..1024]));
            return new { jsonrpc = "2.0", id, error = new { code, message } };
        }
        using var input = new BoundedLineReader(Console.OpenStandardInput(), MaximumMessageBytes);
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            if (ReferenceEquals(line, BoundedLineReader.Oversized))
            {
                Send(new { jsonrpc = "2.0", id = (object?)null, error = new { code = -32600, message = $"The message is larger than {MaximumMessageBytes / 1024 / 1024} MiB." } });
                continue;
            }
            JsonElement request;
            try { request = JsonSerializer.Deserialize<JsonElement>(line); }
            catch { continue; }
            // Only an object can be a request or a notification; anything else is ignored.
            if (request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("method", out var methodElement)) continue;
            var method = methodElement.ValueKind == JsonValueKind.String ? methodElement.GetString() : null;
            if (!request.TryGetProperty("id", out var id))
            {
                if (method == "notifications/cancelled"
                    && request.TryGetProperty("params", out var cancelParams) && cancelParams.ValueKind == JsonValueKind.Object
                    && cancelParams.TryGetProperty("requestId", out var cancelledId)
                    && running.TryGetValue(cancelledId.GetRawText(), out var cancelled))
                {
                    // The call can finish and release its source between the lookup and here.
                    try { cancelled.Cancel(); } catch (ObjectDisposedException) { }
                }
                continue;
            }
            if (method == "tools/call")
            {
                var key = id.GetRawText();
                if (running.Count >= MaximumRunningCalls)
                {
                    Send(new { jsonrpc = "2.0", id, error = new { code = -32000, message = $"KillerMCP is already running {MaximumRunningCalls} tool calls. Try again when one finishes." } });
                    continue;
                }
                var source = new CancellationTokenSource();
                if (!running.TryAdd(key, source))
                {
                    source.Dispose();
                    Send(new { jsonrpc = "2.0", id, error = new { code = -32600, message = "A request with this id is still running." } });
                    continue;
                }
                var callParams = request.TryGetProperty("params", out var givenParams) ? givenParams : default;
                calls.RemoveAll(task => task.IsCompleted);
                calls.Add(Task.Run(async () =>
                {
                    try
                    {
                        await adaptersReady.ConfigureAwait(false);
                        var result = ToolRegistry.Call(callParams, json, source.Token);
                        // A cancelled request gets no reply, and a result too large to send is an error instead.
                        if (!source.IsCancellationRequested)
                        {
                            var reply = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, json);
                            if (reply.Length <= MaximumResultCharacters) lock (outputGate) Console.WriteLine(reply);
                            else Send(new { jsonrpc = "2.0", id, result = new { content = new[] { new { type = "text", text = $"The result is larger than {MaximumResultCharacters / 1024 / 1024} MiB, so it was not sent. Ask for less, such as fewer pages or a lower limit." } }, isError = true } });
                        }
                    }
                    catch (Exception exception)
                    {
                        if (!source.IsCancellationRequested) Send(Failure(id, exception));
                    }
                    finally
                    {
                        running.TryRemove(key, out _);
                        source.Dispose();
                    }
                }));
                continue;
            }
            if (method == "tools/list" && !adaptersReady.IsCompleted)
            {
                calls.Add(Task.Run(async () =>
                {
                    await adaptersReady.ConfigureAwait(false);
                    Send(new { jsonrpc = "2.0", id, result = new { tools = ToolRegistry.Tools } });
                }));
                continue;
            }
            // A client may ask for anything, including methods this server never advertised, so one
            // request must never end the process. Failures go back as JSON-RPC errors on that id.
            try
            {
                object result = method switch
                {
                    "initialize" => new
                    {
                        protocolVersion = "2025-06-18",
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "KillerMCP", version = currentVersion },
                        instructions = "Use KillerMCP tools by task. Exact tool names are optional when the request is clear." + UpdateChecker.Instruction(BuiltInTools.Status),
                    },
                    "ping" => new { },
                    "tools/list" => new { tools = ToolRegistry.Tools },
                    _ => throw new McpRequestException(-32601, $"Method not found: {method}"),
                };
                Send(new { jsonrpc = "2.0", id, result });
            }
            catch (Exception exception)
            {
                Send(Failure(id, exception));
            }
        }
        // The client closed the connection, so nobody is waiting for the remaining calls.
        // App discovery can still be waiting on an app that hangs. Stopping it first lets calls waiting for the
        // tool list finish instead of holding the host open, and no such app outlives the host.
        AppProcesses.StopAll();
        foreach (var source in running.Values)
        {
            try { source.Cancel(); } catch (ObjectDisposedException) { }
        }
        Task.WaitAll([.. calls], TimeSpan.FromSeconds(10));
        // A KillerBench repair that started keeps running to its own time limit, which is up to four hours for
        // some repairs. The host stays until it ends, with no limit of its own, so it keeps the one-repair hold
        // that stops another window from starting a second repair, and records the result and any
        // uncertain-result lock. The repair's own limit and the approval window's five minutes bound the wait.
        KillerBenchRepairJobs.WaitForRunning(Timeout.InfiniteTimeSpan);
    }

    private static async Task RegisterAdaptersAsync(Task<IReadOnlyList<AppAdapter>> adapters) =>
        ToolRegistry.SetAdapters(await adapters.ConfigureAwait(false));

    /// <summary>
    /// Reads UTF-8 lines from a stream without ever holding more than the limit. A longer line is skipped to
    /// its end and reported as <see cref="Oversized"/>.
    /// </summary>
    private sealed class BoundedLineReader(Stream stream, int limit) : IDisposable
    {
        public static readonly string Oversized = new('\0', 1);
        private readonly byte[] buffer = new byte[64 * 1024];
        private readonly MemoryStream line = new();
        private int position;
        private int length;

        public string? ReadLine()
        {
            line.SetLength(0);
            var skipping = false;
            while (true)
            {
                if (position == length)
                {
                    length = stream.Read(buffer, 0, buffer.Length);
                    position = 0;
                    if (length == 0) return skipping ? Oversized : line.Length > 0 ? Decode() : null;
                }
                var end = Array.IndexOf(buffer, (byte)'\n', position, length - position);
                var stop = end < 0 ? length : end;
                if (!skipping)
                {
                    if (line.Length + (stop - position) > limit)
                    {
                        skipping = true;
                        line.SetLength(0);
                    }
                    else
                    {
                        line.Write(buffer, position, stop - position);
                    }
                }
                position = end < 0 ? length : end + 1;
                if (end >= 0) return skipping ? Oversized : Decode();
            }
        }

        private string Decode()
        {
            var bytes = line.GetBuffer().AsSpan(0, (int)line.Length);
            if (bytes.Length > 0 && bytes[^1] == (byte)'\r') bytes = bytes[..^1];
            return Encoding.UTF8.GetString(bytes);
        }

        public void Dispose() => line.Dispose();
    }
}
