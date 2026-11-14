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
            var response = Task.Run(async () =>
            {
                using var connection = await listener.AcceptTcpClientAsync();
                await using var stream = connection.GetStream();
                var request = new byte[8192];
                int length = 0;
                while (length < request.Length)
                {
                    await stream.ReadExactlyAsync(request.AsMemory(length++, 1));
                    if (length >= 4 && request[length - 4] == '\r' && request[length - 3] == '\n' && request[length - 2] == '\r' && request[length - 1] == '\n') break;
                }
                byte[] body = Encoding.UTF8.GetBytes("{\"tag_name\":\"v99.0.0\",\"body\":\"fixture\"}");
                byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
            });
            var fresh = await UpdateChecker.CheckForUpdateAsync("0.9.4");
            await response.WaitAsync(TimeSpan.FromSeconds(5));
            Check(fresh, actualAsset);
            if (!fresh.UpdateAvailable || fresh.Cached || fresh.Unavailable) throw new InvalidOperationException("Fresh successful status path was not selected.");
            if (!UpdateChecker.Instruction(fresh).Contains("/" + actualAsset, StringComparison.Ordinal)) throw new InvalidOperationException("Update instructions use a different setup flavor.");
            Console.WriteLine($"PASS public update status flavor across initial/off/cached/fresh/error paths ({actualAsset}, 9 selected regressions)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_CACHE", oldCache);
            Environment.SetEnvironmentVariable("KILLERMCP_UPDATE_API", oldApi);
            Directory.Delete(root, true);
        }
    }

    private static void Check(UpdateStatus status, string asset)
    {
        if (!status.InstallerUrl.EndsWith("/" + asset, StringComparison.Ordinal))
            throw new InvalidOperationException("Public update status selected the wrong installer: " + status.InstallerUrl);
    }
}
