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

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            _ = HandleAsync(client);
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var headerText = await ReadHeadersAsync(stream).ConfigureAwait(false);
                var lines = headerText.Split("\r\n", StringSplitOptions.None);
                var requestLine = lines[0].Split(' ', 3);
                if (requestLine.Length != 3 || !Uri.TryCreate($"http://127.0.0.1{requestLine[1]}", UriKind.Absolute, out var url))
                {
                    await RespondAsync(stream, 400, "Bad request").ConfigureAwait(false);
                    return;
                }
                var headers = lines.Skip(1).Where(line => line.Contains(':')).Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
                if (!headers.TryGetValue("Host", out var host) || host != $"127.0.0.1:{port}" || QueryToken(url.Query) != token)
                {
                    await RespondAsync(stream, 403, "Forbidden").ConfigureAwait(false);
                    return;
                }
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
                await stream.ReadExactlyAsync(body).ConfigureAwait(false);
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
        }
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        while (bytes.Count < 16_384)
        {
            var value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException();
            bytes.Add((byte)value);
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
        var reason = status switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", _ => "Error" };
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers).ConfigureAwait(false);
        if (payload.Length > 0) await stream.WriteAsync(payload).ConfigureAwait(false);
    }
}
