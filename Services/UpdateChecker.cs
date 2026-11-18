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
    private static string InstallerUrl => InstallerUrlFor(AppContext.BaseDirectory);
    private const string ReleaseUrl = "https://github.com/SteveTheKiller/KillerMCP/releases/latest";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailedCacheLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NotificationLifetime = TimeSpan.FromDays(1);
    private static readonly string NotificationMutexName = ProcessText.LocalName("KillerMCP-UpdateNotification");
    private static readonly string NoticeOpenedEventName = ProcessText.LocalName("KillerMCP-UpdateNotice-Opened");
    private static readonly TimeSpan NoticeOpenWait = TimeSpan.FromSeconds(20);
    private const int MaximumNotesLength = 20000;

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
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 256 * 1024 };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KillerMCP", currentVersion));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var endpoint = UpdateEndpoint(ProcessText.TestSeam("KILLERMCP_UPDATE_API"));
            using var response = await client.GetAsync(endpoint).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var release = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var latestVersion = release.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            // The release body is the changelog section; the update window lists it as what is new.
            string? notes = release.RootElement.TryGetProperty("body", out JsonElement body) && body.ValueKind == JsonValueKind.String
                ? body.GetString() : null;
            if (notes is { Length: > MaximumNotesLength }) notes = notes[..MaximumNotesLength];
            if (!TryVersion(latestVersion, out var parsedLatest))
            {
                throw new InvalidDataException("The release version is invalid.");
            }
            // Only the canonical form travels on, so odd tag text never reaches a command line or a file name.
            latestVersion = Canonical(parsedLatest);

            var checkedAt = DateTimeOffset.UtcNow;
            await WriteCacheAsync(cachePath, new Cache(latestVersion!, checkedAt, notes)).ConfigureAwait(false);
            return MakeStatus(currentVersion, latestVersion!, checkedAt, false);
        }
        catch
        {
            var checkedAt = DateTimeOffset.UtcNow;
            await WriteCacheAsync(cachePath, new Cache(null, checkedAt, Unavailable: true)).ConfigureAwait(false);
            return new UpdateStatus(currentVersion, null, false, checkedAt, false, InstallerUrl, ReleaseUrl, true);
        }
    }

    public static string Instruction(UpdateStatus status) => status.UpdateAvailable
        ? $" KillerMCP {status.LatestVersion} is available. This computer has {status.InstalledVersion}. The user can download the signed installer from {status.InstallerUrl}. Do not download or install it without the user's approval."
        : string.Empty;

    private static string InstallerUrlFor(string installFolder) => ReleaseUrl + "/download/" + Updates.SetupAsset.For(installFolder);

    public static UpdateStatus InitialStatus(string currentVersion, string? installFolder = null) =>
        new(currentVersion, null, false, null, false, InstallerUrlFor(installFolder ?? AppContext.BaseDirectory), ReleaseUrl, true);

    public static void StartMonitoring(string currentVersion, UpdateStatus initialStatus, Action<UpdateStatus> onUpdate)
    {
        // The notice waits for its window to open, so it runs beside the MCP connection instead of delaying it.
        _ = Task.Run(async () =>
        {
            var first = initialStatus.CheckedAt.HasValue ? initialStatus : await CheckForUpdateAsync(currentVersion).ConfigureAwait(false);
            onUpdate(first);
            ShowNotification(first);
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
            if (cache is null || IsExpired(cache.CheckedAt, cache.Unavailable ? FailedCacheLifetime : CacheLifetime)
                || (!cache.Unavailable && !TryVersion(cache.LatestVersion, out _)))
            {
                return null;
            }

            TryVersion(cache.LatestVersion, out var cachedLatest);
            return cache.Unavailable
                ? new UpdateStatus(currentVersion, null, false, cache.CheckedAt, true, InstallerUrl, ReleaseUrl, true)
                : MakeStatus(currentVersion, Canonical(cachedLatest), cache.CheckedAt, true);
        }
        catch
        {
            return null;
        }
    }

    // A cache that cannot be saved only means the next check fetches again; the release just fetched still counts.
    // The update window reads this file, so it is replaced whole instead of rewritten in place.
    private static async Task WriteCacheAsync(string path, Cache cache)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(cache) + Environment.NewLine).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
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
        // A test cache must be a fully qualified .json file, so the seam cannot aim the cache write at a
        // document or program the user owns.
        if (ProcessText.TestPath("KILLERMCP_UPDATE_CACHE", ".json") is { } configured) return configured;

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
            string notifier = Path.Combine(AppContext.BaseDirectory, "KillerMCP.Notify.exe");
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
            NotificationState? state = null;
            try { state = JsonSerializer.Deserialize<NotificationState>(File.ReadAllText(statePath)); } catch { }
            if (state?.LatestVersion == status.LatestVersion && !IsExpired(state.ShownAt, NotificationLifetime)) return;

            // The update window signals once it has taken the notice. Only then does the notice
            // count as shown; one that closed before opening is offered again by the next check.
            using var opened = new EventWaitHandle(false, EventResetMode.AutoReset, NoticeOpenedEventName);
            opened.Reset();
            string arguments = $"--installed {Quote(status.InstalledVersion)} --latest {Quote(status.LatestVersion)} --installer {Quote(status.InstallerUrl)} --release {Quote(status.ReleaseUrl)} --behavior {Quote(behavior)} --status {Quote(GetCachePath())}";
            // The notice has to outlive this host, which an agent client can close at any time.
            DetachedProcess.Start(notifier, arguments);
            if (!opened.WaitOne(OpenWait())) return;
            File.WriteAllText(statePath, JsonSerializer.Serialize(new NotificationState(status.LatestVersion, DateTimeOffset.UtcNow)) + Environment.NewLine);
        }
        catch { }
        finally
        {
            if (owned) gate!.ReleaseMutex();
            gate?.Dispose();
        }
    }

    private static TimeSpan OpenWait() =>
        int.TryParse(ProcessText.TestSeam("KILLERMCP_NOTICE_WAIT_MS"), out int milliseconds) && milliseconds is > 0 and <= 60000
            ? TimeSpan.FromMilliseconds(milliseconds) : NoticeOpenWait;

    private static string Quote(string value) => ProcessText.Quote(value);

    // A timestamp from the future, from a clock that was wrong and then corrected, counts as expired, so
    // update checks and notices never freeze until that date comes around.
    internal static bool IsExpired(DateTimeOffset stamp, TimeSpan lifetime)
    {
        var now = DateTimeOffset.UtcNow;
        return stamp > now + FutureSkew || now - stamp >= lifetime;
    }

    private static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(5);

    // The release API is GitHub over https. A test may point at its own loopback server; anything else is
    // ignored in favor of the real endpoint.
    internal static string UpdateEndpoint(string? configured)
    {
        if (configured is null || !Uri.TryCreate(configured, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)) return ReleaseApi;
        if (uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)) return configured;
        if ((uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback) return configured;
        return ReleaseApi;
    }

    internal static string Canonical(Version version) => new Version(version.Major, version.Minor, Math.Max(version.Build, 0)).ToString(3);

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

    private sealed record Cache(string? LatestVersion, DateTimeOffset CheckedAt, string? Notes = null, bool Unavailable = false);
    private sealed record NotificationState(string LatestVersion, DateTimeOffset ShownAt);
}
