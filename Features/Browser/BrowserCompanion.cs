using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KillerMCP;

sealed record BrowserCompanionState(string Value, long UpdatedAt);

sealed class BrowserCompanion
{
    private static readonly IReadOnlyDictionary<string, int> Limits = new Dictionary<string, int>
    {
        ["device"] = 4096,
        ["key"] = 1024,
        ["html"] = 8192,
        ["signature"] = 1_000_000,
        ["camera"] = 2_000_000,
    };

    private readonly ConcurrentDictionary<string, BrowserCompanionState> state = new(StringComparer.Ordinal);
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    private readonly string page;
    private readonly Task ready;
    private int port;

    public BrowserCompanion()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("KillerMCP.browser-companion.html") ?? throw new InvalidOperationException("Browser companion page is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        page = reader.ReadToEnd();
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        ready = AcceptLoopAsync();
    }

    public string Url => $"http://127.0.0.1:{port}/?token={token}";

    public BrowserCompanionState Get(string type) => state.TryGetValue(type, out var value)
        ? value
        : throw new InvalidOperationException("Open the browser companion and provide this input first");

    // Each connection runs on its own task with a time limit, and only a few run at once, so a
    // connection that never finishes its request cannot hold up the page or tie up the host.
    // A connection proves the token in its request line before it takes one of the working slots, so
    // sockets that never send a request, or send a wrong token, only occupy the wider and shorter
    // header pool and cannot lock the page out of its own companion.
    private const int MaximumConnections = 16;
    private const int MaximumPendingHeaders = 256;
    private static readonly TimeSpan HeaderTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RequestTime = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim connections = new(MaximumConnections, MaximumConnections);
    private readonly SemaphoreSlim pendingHeaders = new(MaximumPendingHeaders, MaximumPendingHeaders);

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                Console.Error.WriteLine("KillerMCP browser companion could not accept a connection: " + exception.Message);
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (!pendingHeaders.Wait(0))
            {
                client.Dispose();
                continue;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        var headerSlot = true;
        var workingSlot = false;
        using (client)
        {
            using var deadline = new CancellationTokenSource(RequestTime);
            try
            {
                using var stream = client.GetStream();
                string headerText;
                using (var headerDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token))
                {
                    headerDeadline.CancelAfter(HeaderTime);
                    headerText = await ReadHeadersAsync(stream, headerDeadline.Token).ConfigureAwait(false);
                }
                var lines = headerText.Split("\r\n", StringSplitOptions.None);
                var requestLine = lines[0].Split(' ', 3);
                if (requestLine.Length != 3 || !Uri.TryCreate($"http://127.0.0.1{requestLine[1]}", UriKind.Absolute, out var url))
                {
                    await RespondAsync(stream, 400, "Bad request").ConfigureAwait(false);
                    return;
                }
                var headers = lines.Skip(1).Where(line => line.Contains(':')).Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
                if (!headers.TryGetValue("Host", out var host) || host != $"127.0.0.1:{port}" || !TokenMatches(QueryToken(url.Query)))
                {
                    await RespondAsync(stream, 403, "Forbidden").ConfigureAwait(false);
                    return;
                }
                pendingHeaders.Release();
                headerSlot = false;
                if (!connections.Wait(0))
                {
                    await RespondAsync(stream, 503, "Busy").ConfigureAwait(false);
                    return;
                }
                workingSlot = true;
                if (requestLine[0] == "GET" && url.AbsolutePath == "/")
                {
                    await RespondAsync(stream, 200, page, "text/html; charset=utf-8").ConfigureAwait(false);
                    return;
                }
                if (requestLine[0] != "POST" || url.AbsolutePath != "/state" || !headers.TryGetValue("Origin", out var origin) || origin != $"http://127.0.0.1:{port}")
                {
                    await RespondAsync(stream, 404, "Not found").ConfigureAwait(false);
                    return;
                }
                if (!headers.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, out var length) || length < 0 || length > 2_100_000)
                {
                    await RespondAsync(stream, 413, "Too large").ConfigureAwait(false);
                    return;
                }
                var body = new byte[length];
                await stream.ReadExactlyAsync(body, deadline.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                var type = document.RootElement.GetProperty("type").GetString();
                var value = document.RootElement.GetProperty("value").GetString();
                if (type is null || value is null || !Limits.TryGetValue(type, out var maximum) || value.Length > maximum)
                {
                    await RespondAsync(stream, 400, "Invalid state").ConfigureAwait(false);
                    return;
                }
                state[type] = new(value, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                await RespondAsync(stream, 204, "").ConfigureAwait(false);
            }
            catch
            {
                try { await RespondAsync(client.GetStream(), 400, "Invalid request").ConfigureAwait(false); } catch { }
            }
            finally
            {
                if (headerSlot) pendingHeaders.Release();
                if (workingSlot) connections.Release();
            }
        }
    }

    private bool TokenMatches(string? candidate) => candidate is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(token));

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 16_384)
        {
            if (await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false) == 0) throw new EndOfStreamException();
            bytes.Add(one[0]);
            var count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == 13 && bytes[count - 3] == 10 && bytes[count - 2] == 13 && bytes[count - 1] == 10) return Encoding.ASCII.GetString(bytes.ToArray(), 0, count - 4);
        }
        throw new InvalidDataException("Headers are too large.");
    }

    private static string? QueryToken(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Split('=', 2)).FirstOrDefault(parts => Uri.UnescapeDataString(parts[0]) == "token") is { Length: 2 } match ? Uri.UnescapeDataString(match[1]) : null;

    private static async Task RespondAsync(NetworkStream stream, int status, string body, string contentType = "text/plain; charset=utf-8")
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", 503 => "Service Unavailable", _ => "Error" };
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers).ConfigureAwait(false);
        if (payload.Length > 0) await stream.WriteAsync(payload).ConfigureAwait(false);
    }
}
