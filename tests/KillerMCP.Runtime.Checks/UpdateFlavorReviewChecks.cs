using KillerMCP;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class UpdateFlavorReviewChecks
{
    public static async Task RunAsync()
    {
        var bundled = File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll")) && File.Exists(Path.Combine(AppContext.BaseDirectory, "System.Private.CoreLib.dll"));
        string actualAsset = bundled ? "KillerMCP-Store.exe" : "KillerMCP-Setup.exe";
        Check(UpdateChecker.InitialStatus("0.9.4"), actualAsset);
        string root = Path.Combine(Path.GetTempPath(), "killermcp-update-flavor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? oldCache = Environment.GetEnvironmentVariable("KILLERMCP_UPDATE_CACHE");
        string? oldApi = Environment.GetEnvironmentVariable("KILLERMCP_UPDATE_API");
        string cache = Path.Combine(root, "status.json"), preferences = Path.Combine(root, "update-preferences.json");
        try
        {
            Check(UpdateChecker.InitialStatus("0.9.4", root), "KillerMCP-Setup.exe");
            File.WriteAllText(Path.Combine(root, "coreclr.dll"), "runtime fixture");
            File.WriteAllText(Path.Combine(root, "System.Private.CoreLib.dll"), "runtime fixture");
            Check(UpdateChecker.InitialStatus("0.9.4", root), "KillerMCP-Store.exe");
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_CACHE", cache);
            File.WriteAllText(preferences, "{\"Behavior\":\"off\"}");
            Check(await UpdateChecker.CheckForUpdateAsync("0.9.4"), actualAsset);
            File.WriteAllText(preferences, "{\"Behavior\":\"check\"}");
            File.WriteAllText(cache, JsonSerializer.Serialize(new { LatestVersion = "99.0.0", CheckedAt = DateTimeOffset.UtcNow }));
            var cached = await UpdateChecker.CheckForUpdateAsync("0.9.4");
            Check(cached, actualAsset);
            if (!cached.Cached) throw new InvalidOperationException("Successful status cache was not selected.");
            File.WriteAllText(cache, JsonSerializer.Serialize(new { LatestVersion = (string?)null, CheckedAt = DateTimeOffset.UtcNow, Unavailable = true }));
            var failure = await UpdateChecker.CheckForUpdateAsync("0.9.4");
            Check(failure, actualAsset);
            if (!failure.Cached || !failure.Unavailable) throw new InvalidOperationException("Failure status cache was not selected.");
            File.Delete(cache);
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_API", "http://127.0.0.1:1/");
            var unavailable = await UpdateChecker.CheckForUpdateAsync("0.9.4");
            Check(unavailable, actualAsset);
            if (!unavailable.Unavailable || unavailable.Cached) throw new InvalidOperationException("Fresh failed status path was not selected.");
            File.Delete(cache);
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_API", $"http://127.0.0.1:{port}/");
            var response = ReplyAsync(listener, "fixture");
            var fresh = await UpdateChecker.CheckForUpdateAsync("0.9.4");
            await response.WaitAsync(TimeSpan.FromSeconds(5));
            Check(fresh, actualAsset);
            if (!fresh.UpdateAvailable || fresh.Cached || fresh.Unavailable) throw new InvalidOperationException("Fresh successful status path was not selected.");
            if (!UpdateChecker.Instruction(fresh).Contains("/" + actualAsset, StringComparison.Ordinal)) throw new InvalidOperationException("Update instructions use a different setup flavor.");
            File.Delete(cache);
            var oversizedResponse = ReplyAsync(listener, new string('a', 300 * 1024));
            var oversized = await UpdateChecker.CheckForUpdateAsync("0.9.4");
            try { await oversizedResponse.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (IOException) when (oversized.Unavailable) { }
            if (!oversized.Unavailable || oversized.UpdateAvailable)
                throw new InvalidOperationException("Oversized release metadata was accepted.");
            Console.WriteLine($"PASS public update status flavor and response bounds ({actualAsset}, 10 selected regressions)");
            // A cache stamped in the future is expired, so a corrected clock never freezes update checks.
            File.WriteAllText(cache, JsonSerializer.Serialize(new { LatestVersion = "99.0.0", CheckedAt = DateTimeOffset.UtcNow.AddDays(30) }));
            var futureResponse = ReplyAsync(listener, "fixture");
            var future = await UpdateChecker.CheckForUpdateAsync("0.9.4");
            await futureResponse.WaitAsync(TimeSpan.FromSeconds(5));
            if (future.Cached) throw new InvalidOperationException("A future CheckedAt was trusted instead of checking again.");
            if (!UpdateChecker.IsExpired(DateTimeOffset.UtcNow.AddHours(2), TimeSpan.FromDays(1)) || UpdateChecker.IsExpired(DateTimeOffset.UtcNow, TimeSpan.FromDays(1)))
                throw new InvalidOperationException("Future notice stamps did not expire.");
            const string real = "https://api.github.com/repos/SteveTheKiller/KillerMCP/releases/latest";
            foreach (var planted in new[] { "http://updates.example.com/latest", "https://evil.example/latest", "https://user@api.github.com/x", "file:///C:/x.json" })
                if (UpdateChecker.UpdateEndpoint(planted) != real) throw new InvalidOperationException("A planted update endpoint was honored: " + planted);
            if (UpdateChecker.UpdateEndpoint("http://127.0.0.1:9/x") != "http://127.0.0.1:9/x") throw new InvalidOperationException("The loopback test endpoint was refused.");
            if (UpdateChecker.Canonical(new Version("1.2")) != "1.2.0" || UpdateChecker.Canonical(new Version("1.2.3.4")) != "1.2.3")
                throw new InvalidOperationException("Release versions are not canonical.");
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_CACHE", Path.Combine(root, "notes.txt"));
            if (ProcessText.TestPath("KILLERMCP_UPDATE_CACHE", ".json") is not null) throw new InvalidOperationException("A non-json update cache seam was honored.");
            if (ProcessText.Quote(@"C:\Has Space\") != "\"C:\\Has Space\\\\\"" || ProcessText.Quote("a\"b") != "\"a\\\"b\"")
                throw new InvalidOperationException("Command line quoting mishandles a trailing backslash or quote.");
            Console.WriteLine("PASS update seams are pinned, future stamps expire, versions are canonical and quoting survives a trailing backslash (13 selected regressions)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_CACHE", oldCache);
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_API", oldApi);
            Directory.Delete(root, true);
        }
    }

    private static async Task ReplyAsync(TcpListener listener, string notes)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (await reader.ReadLineAsync() is { Length: > 0 }) { }
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "v99.0.0", body = notes });
        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(body);
    }

    private static void Check(UpdateStatus status, string asset)
    {
        if (!status.InstallerUrl.EndsWith("/" + asset, StringComparison.Ordinal))
            throw new InvalidOperationException("Public update status selected the wrong installer: " + status.InstallerUrl);
    }
}
