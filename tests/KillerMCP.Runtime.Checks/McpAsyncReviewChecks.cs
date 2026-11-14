using System.Diagnostics;
using System.Text.Json;
using KillerMCP;
using KillerMCP.Models;
using KillerMCP.Services.Mcp;

internal static class McpAsyncReviewChecks
{
    public static async Task RunAsync()
    {
        await CheckNetworkCancellationAsync();
        var started = new CountdownEvent(32);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ToolRegistry.SetAdapters([new AppAdapter(new("review_slow", "Slow external app fixture", new System.Text.Json.Nodes.JsonObject { ["type"] = "object", ["properties"] = new System.Text.Json.Nodes.JsonObject() }), async (_, token) =>
        {
            started.Signal();
            await release.Task.WaitAsync(token);
            return new("done");
        })]);
        var json = new JsonSerializerOptions();
        var slow = JsonSerializer.SerializeToElement(new { name = "review_slow", arguments = new { } });
        var fast = JsonSerializer.SerializeToElement(new { name = "encode_base64", arguments = new { text = "ready" } });
        var running = Enumerable.Range(0, 32).Select(_ => InvokeAsync(slow, json)).ToArray();
        if (!started.Wait(TimeSpan.FromSeconds(15))) throw new InvalidOperationException("The 32 app calls did not start.");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var answer = InvokeAsync(fast, json);
            if (!answer.Wait(TimeSpan.FromMilliseconds(100))) throw new InvalidOperationException("A builtin waited behind 32 app calls.");
            var result = JsonSerializer.Serialize(await answer);
            if (!result.Contains("cmVhZHk=")) throw new InvalidOperationException("The builtin result was incorrect.");
            Console.WriteLine($"PASS builtin under 32 app calls: {stopwatch.Elapsed.TotalMilliseconds:F1} ms (1 selected regression)");
        }
        finally
        {
            release.SetResult();
            await Task.WhenAll(running);
            ToolRegistry.SetAdapters([]);
        }
    }

    private static async Task CheckNetworkCancellationAsync()
    {
        foreach (var (name, arguments) in new[]
        {
            ("lookup_domain_dns", "{\"name\":\"example.com\"}"),
            ("lookup_domain_rdap", "{\"domain\":\"example.com\"}"),
            ("lookup_cve", "{\"query\":\"CVE-2025-0001\"}"),
            ("search_gifs", "{\"query\":\"fixture\"}"),
            ("list_killer_scripts", "{}"),
        })
        {
            using var handler = new StalledBodyHandler();
            using var client = new HttpClient(handler);
            using var cancelled = new CancellationTokenSource();
            var call = KillerMCP.Features.Tools.BuiltInTools.CallAsync(name, JsonSerializer.Deserialize<JsonElement>(arguments), cancelled.Token, client, TimeSpan.FromSeconds(5));
            await handler.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            cancelled.Cancel();
            try { await call.WaitAsync(TimeSpan.FromSeconds(1)); throw new InvalidOperationException(name + " ignored cancellation."); }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
            using var deadlineHandler = new StalledBodyHandler();
            using var deadlineClient = new HttpClient(deadlineHandler);
            var deadlineCall = KillerMCP.Features.Tools.BuiltInTools.CallAsync(name, JsonSerializer.Deserialize<JsonElement>(arguments), CancellationToken.None, deadlineClient, TimeSpan.FromMilliseconds(50));
            try { await deadlineCall.WaitAsync(TimeSpan.FromSeconds(1)); throw new InvalidOperationException(name + " ignored its body deadline."); }
            catch (KillerMCP.Models.ToolCallException) { }
            Console.WriteLine($"PASS {name}: stalled body cancellation and deadline (2 selected regressions)");
        }
    }

    private sealed class StalledBodyHandler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource BodyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new StalledBody(BodyStarted)) });
    }

    private sealed class StalledBody(TaskCompletionSource started) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static Task<object> InvokeAsync(JsonElement parameters, JsonSerializerOptions json) => ToolRegistry.CallAsync(parameters, json);
}
