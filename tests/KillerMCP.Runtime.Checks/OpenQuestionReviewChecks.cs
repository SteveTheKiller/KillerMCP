using System.Net.Sockets;
using System.Text;
using KillerMCP;
using KillerTools.Engine.Documents;

// The review left two questions that a unit check settles: whether deeply dotted TOML keys can
// overflow the converter's stack, and whether idle local connections can lock the browser
// companion out of its own page.
static class OpenQuestionReviewChecks
{
    public static async Task RunAsync()
    {
        TomlDepth();
        await CompanionSlotsAsync();
    }

    private static void TomlDepth()
    {
        // A converter input is at most 4,096 characters, so this is the deepest dotted key a caller can send.
        var dotted = string.Join('.', Enumerable.Repeat("a", 2047)) + "=1";
        var header = "[" + string.Join('.', Enumerable.Repeat("a", 2046)) + "]";
        foreach (var input in new[] { dotted, header })
        {
            if (input.Length > 4096) throw new InvalidOperationException("The TOML depth fixture is longer than the converter accepts.");
            // A stack overflow ends the process, so reaching the next line is the check. Refusing the
            // input is fine; converting it is fine too.
            try { StructuredTextConverter.TomlToJson(input); }
            catch (Exception exception) when (exception is not StackOverflowException) { }
        }
        Console.WriteLine("PASS deeply dotted TOML keys do not overflow the converter");
    }

    private static async Task CompanionSlotsAsync()
    {
        var companion = new BrowserCompanion();
        var url = new Uri(companion.Url);
        var idle = new List<TcpClient>();
        try
        {
            // Connections that never send a request or never prove the token must not use up the
            // slots the page needs.
            for (var i = 0; i < 40; i++)
            {
                var client = new TcpClient();
                await client.ConnectAsync(url.Host, url.Port);
                idle.Add(client);
            }
            await Task.Delay(200);
            var status = await GetAsync(url, url.PathAndQuery);
            if (status != 200) throw new InvalidOperationException($"The companion page answered {status} while idle connections were open.");
            var forbidden = await GetAsync(url, "/?token=" + new string('0', 48));
            if (forbidden != 403) throw new InvalidOperationException($"A wrong token answered {forbidden}.");
        }
        finally
        {
            foreach (var client in idle) client.Dispose();
        }
        Console.WriteLine("PASS idle and unauthenticated connections do not lock out the browser companion");
    }

    private static async Task<int> GetAsync(Uri url, string pathAndQuery)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(url.Host, url.Port);
        using var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes($"GET {pathAndQuery} HTTP/1.1\r\nHost: {url.Host}:{url.Port}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var buffer = new byte[64];
        var read = 0;
        try { read = await stream.ReadAtLeastAsync(buffer, 12, false, timeout.Token); }
        catch (OperationCanceledException) { return 0; }
        catch (IOException) { return 0; }
        var line = Encoding.ASCII.GetString(buffer, 0, read);
        return line.StartsWith("HTTP/1.1 ", StringComparison.Ordinal) && int.TryParse(line.AsSpan(9, 3), out var code) ? code : 0;
    }
}
