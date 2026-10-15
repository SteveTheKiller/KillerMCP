using System.Net.Http.Headers;
using System.Text.Json;

namespace KillerMCP;

internal sealed record UpdateStatus(
    string InstalledVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    DateTimeOffset? CheckedAt,
    bool Cached,
    string InstallerUrl,
    string ReleaseUrl,
    bool Unavailable = false);

internal static class UpdateChecker
{
    private const string ReleaseApi = "https://api.github.com/repos/SteveTheKiller/KillerMCP/releases/latest";
    private const string InstallerUrl = "https://github.com/SteveTheKiller/KillerMCP/releases/latest/download/KillerMCP-Setup.exe";
    private const string ReleaseUrl = "https://github.com/SteveTheKiller/KillerMCP/releases/latest";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan NotificationLifetime = TimeSpan.FromDays(1);
    private const string NotificationMutexName = @"Local\KillerMCP-UpdateNotification";

    public static async Task<UpdateStatus> CheckForUpdateAsync(string currentVersion)
    {
        if (ReadBehavior() == "off")
        {
            return new UpdateStatus(currentVersion, null, false, null, false, InstallerUrl, ReleaseUrl, true);
        }
        var cachePath = GetCachePath();
        var cached = await ReadCacheAsync(cachePath, currentVersion).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KillerMCP", currentVersion));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var endpoint = Environment.GetEnvironmentVariable("KILLERMCP_UPDATE_API") ?? ReleaseApi;
            using var response = await client.GetAsync(endpoint).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var release = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var latestVersion = release.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            if (!TryVersion(latestVersion, out _))
            {
                throw new InvalidDataException("The release version is invalid.");
            }

            var checkedAt = DateTimeOffset.UtcNow;
            var directory = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(new Cache(latestVersion!, checkedAt)) + Environment.NewLine).ConfigureAwait(false);
            return MakeStatus(currentVersion, latestVersion!, checkedAt, false);
        }
        catch
        {
            return new UpdateStatus(currentVersion, null, false, null, false, InstallerUrl, ReleaseUrl, true);
        }
    }

    public static string Instruction(UpdateStatus status) => status.UpdateAvailable
        ? $" KillerMCP {status.LatestVersion} is available. This computer has {status.InstalledVersion}. The user can download the signed installer from {status.InstallerUrl}. Do not download or install it without the user's approval."
        : string.Empty;

    public static void StartMonitoring(string currentVersion, UpdateStatus initialStatus, Action<UpdateStatus> onUpdate)
    {
        ShowNotification(initialStatus);
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(CacheLifetime).ConfigureAwait(false);
                if (ReadBehavior() == "off") continue;
                UpdateStatus status = await CheckForUpdateAsync(currentVersion).ConfigureAwait(false);
                onUpdate(status);
                ShowNotification(status);
            }
        });
    }

    internal static int? CompareVersions(string left, string right)
    {
        if (!TryVersion(left, out var leftVersion) || !TryVersion(right, out var rightVersion))
        {
            return null;
        }

        return leftVersion.CompareTo(rightVersion);
    }

    private static async Task<UpdateStatus?> ReadCacheAsync(string path, string currentVersion)
    {
        try
        {
            var cache = JsonSerializer.Deserialize<Cache>(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            if (cache is null || DateTimeOffset.UtcNow - cache.CheckedAt >= CacheLifetime || !TryVersion(cache.LatestVersion, out _))
            {
                return null;
            }

            return MakeStatus(currentVersion, cache.LatestVersion, cache.CheckedAt, true);
        }
        catch
        {
            return null;
        }
    }

    private static UpdateStatus MakeStatus(string currentVersion, string latestVersion, DateTimeOffset checkedAt, bool cached) => new(
        currentVersion,
        latestVersion,
        CompareVersions(currentVersion, latestVersion) == -1,
        checkedAt,
        cached,
        InstallerUrl,
        ReleaseUrl);

    private static string GetCachePath()
    {
        var configured = Environment.GetEnvironmentVariable("KILLERMCP_UPDATE_CACHE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP", "update-status.json");
    }

    private static void ShowNotification(UpdateStatus status)
    {
        if (!OperatingSystem.IsWindows() || !status.UpdateAvailable || string.IsNullOrWhiteSpace(status.LatestVersion)) return;
        Mutex? gate = null;
        bool owned = false;
        try
        {
            string behavior = ReadBehavior();
            if (behavior == "off") return;
            string notifier = Environment.GetEnvironmentVariable("KILLERMCP_NOTIFIER")
                ?? Path.Combine(AppContext.BaseDirectory, "KillerMCP.Notify.exe");
            if (!File.Exists(notifier)) return;

            // Agent clients often start several hosts at once. Only one of them may read the state,
            // claim the notification and launch the notifier, or every host opens its own window.
            gate = new Mutex(false, NotificationMutexName);
            try { owned = gate.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) return;

            string directory = Path.GetDirectoryName(GetCachePath())!;
            Directory.CreateDirectory(directory);
            string statePath = Path.Combine(directory, "update-notification.json");
            string? previous = null;
            try { previous = File.ReadAllText(statePath); } catch { }
            NotificationState? state = null;
            try { state = previous is null ? null : JsonSerializer.Deserialize<NotificationState>(previous); } catch { }
            if (state?.LatestVersion == status.LatestVersion && DateTimeOffset.UtcNow - state.ShownAt < NotificationLifetime) return;

            File.WriteAllText(statePath, JsonSerializer.Serialize(new NotificationState(status.LatestVersion, DateTimeOffset.UtcNow)) + Environment.NewLine);
            var start = new System.Diagnostics.ProcessStartInfo(notifier)
            {
                UseShellExecute = true,
                Arguments = $"--installed {Quote(status.InstalledVersion)} --latest {Quote(status.LatestVersion)} --installer {Quote(status.InstallerUrl)} --release {Quote(status.ReleaseUrl)} --behavior {Quote(behavior)}"
            };
            try { System.Diagnostics.Process.Start(start); }
            catch
            {
                // The claim must not hide the next attempt when the notifier never started.
                if (previous is null) File.Delete(statePath);
                else File.WriteAllText(statePath, previous);
                throw;
            }
        }
        catch { }
        finally
        {
            if (owned) gate!.ReleaseMutex();
            gate?.Dispose();
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    private static string ReadBehavior()
    {
        try
        {
            string path = Path.Combine(Path.GetDirectoryName(GetCachePath())!, "update-preferences.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            string? behavior = document.RootElement.GetProperty("Behavior").GetString()?.ToLowerInvariant();
            if (behavior is "off" or "check" or "download" or "install") return behavior;
        }
        catch { }
        return "check";
    }

    private static bool TryVersion(string? value, out Version version) => Version.TryParse(value?.Trim().TrimStart('v', 'V'), out version!);

    private sealed record Cache(string LatestVersion, DateTimeOffset CheckedAt);
    private sealed record NotificationState(string LatestVersion, DateTimeOffset ShownAt);
}
