using System.Text.Json;
using System.Text.RegularExpressions;

namespace KillerMCP;

public enum KillerBenchTier
{
    /// <summary>Low-impact, verifiable changes. Allowed when the user sets repairs to safe or all.</summary>
    A,
    /// <summary>Disruptive changes. Allowed only at all, and each one needs a person to approve it on the desktop.</summary>
    B,
}

/// <summary>A fully validated repair request: the exact KillerBench arguments for preview, execution and recovery.</summary>
public sealed record KillerBenchRepairPlan(
    string Action,
    string Title,
    KillerBenchTier Tier,
    string Effects,
    string Target,
    IReadOnlyList<string> PreviewArguments,
    IReadOnlyList<string> ExecuteArguments,
    IReadOnlyList<string>? RecoverArguments,
    int PreviewSeconds,
    int ExecuteSeconds,
    bool Elevate = true);

internal sealed record RepairDefinition(
    string Name,
    string Title,
    KillerBenchTier? Tier,
    string Effects,
    string[] Options,
    Func<JsonElement, Func<CancellationToken, Task<JsonElement?>>, CancellationToken, Task<(KillerBenchRepairPlan? Plan, string? Error)>> Build);

/// <summary>
/// The only KillerBench changes an MCP client can request. Anything not listed here, including BitLocker,
/// previous Windows removal, offline drive repair, profile and package removal, decluttering and computer
/// rename, is refused because it is not in the catalog.
/// </summary>
internal static partial class KillerBenchRepairs
{
    public static readonly string[] NeverThroughMcp =
    [
        "BitLocker encryption, decryption, protectors and recovery backup",
        "previous Windows installation removal",
        "offline or scheduled filesystem repair",
        "profile and app package removal",
        "Outlook reset and Microsoft 365 app removal",
        "decluttering, power plan and hibernation changes",
        "computer rename",
    ];

    private static readonly string[] AutoTierAContracts =
    [
        "ExactServiceState", "DeliveryOptimizationState", "DhcpAdapterState", "DnsLookupState",
        "PausedQueueState", "PrinterOfflineState", "TimeSynchronizationState", "DefenderSignatureFreshness",
    ];

    private static readonly string[] AutoTierBContracts =
    [
        "ServicingHealth", "ComponentCleanupRecommendation", "WmiRepositoryConsistency",
        "WindowsRecoveryEnvironmentState", "PrintSpoolDirectoryState",
    ];

    private static readonly Dictionary<string, string> CleanupTargets = new(StringComparer.Ordinal)
    {
        ["user_temp"] = "--user-temp",
        ["windows_temp"] = "--windows-temp",
        ["system_temp"] = "--system-temp",
        ["shader_caches"] = "--shader-caches",
        ["browser_caches"] = "--browser-caches",
        ["office_caches"] = "--office-caches",
        ["thumbnail_cache"] = "--thumbnail-cache",
        ["icon_cache"] = "--icon-cache",
        ["error_reports"] = "--error-reports",
        ["crash_dumps"] = "--crash-dumps",
        ["windows_minidumps"] = "--windows-minidumps",
        ["delivery_optimization_cache"] = "--delivery-optimization-cache",
        ["recycle_bin"] = "--recycle-bin",
    };

    public static IReadOnlyList<RepairDefinition> Catalog { get; } =
    [
        Simple("dhcp_renew", "Renew missing or expired DHCP leases", KillerBenchTier.A,
            "Renews only connected physical DHCP adapters that lack a usable IPv4 address or hold an expired lease. May change the leased address and briefly interrupt connections.",
            [], _ => (["dhcp-renew"], ["dhcp-renew-recover"], "This PC's DHCP adapters", null)),
        Simple("dns_cache_flush", "Flush one hostname from the DNS cache", KillerBenchTier.A,
            "Flushes the local DNS cache only when a lookup for this exact hostname failed with HostNotFound or NoData, then verifies the hostname again.",
            ["host"], input => Host(input, "host", out var host) ? (["dns-cache", host!], ["dns-cache-recover"], host!, null) : Fail("host must be one hostname")),
        Simple("printer_repair", "Resume paused queues and clear offline mode", KillerBenchTier.A,
            "Can start the enabled Print Spooler, resume an exact paused queue that has waiting jobs, and clear offline mode. Clearing offline mode can release waiting jobs.",
            ["queues"], input => Queues(input, out var queues) ? (["printer-repair", .. queues.SelectMany(queue => new[] { "--queue", queue })], null, string.Join(", ", queues), null) : Fail("queues must list 1 to 8 local printer queue names")),
        Simple("print_job_cancel", "Cancel one stuck print job", KillerBenchTier.A,
            "Cancels only the selected job when Windows reports it as Error or Blocked.",
            ["queue", "jobId"], input => Queue(input, "queue", out var queue) && Integer(input, "jobId", 1, int.MaxValue, out var job) ? (["print-job-cancel", queue!, job.ToString()], ["print-job-cancel-recover"], $"{queue} job {job}", null) : Fail("queue and jobId are required")),
        Simple("default_printer", "Repair a missing or conflicting default printer", KillerBenchTier.A,
            "Sets the selected local queue as the default only when the current default is missing or conflicting.",
            ["queue"], input => Queue(input, "queue", out var queue) ? (["default-printer-set", queue!], ["default-printer-set-recover"], queue!, null) : Fail("queue must be one local printer queue name")),
        Simple("restore_point", "Create one System Restore point", KillerBenchTier.A,
            "Creates one restore point after fresh eligibility checks. Windows may limit how often restore points are created.",
            ["description", "type"], input =>
            {
                if (!SafeText(input, "description", 64, out var description)) return Fail("description must be 1 to 64 plain characters");
                var type = Optional(input, "type") ?? "ModifySettings";
                if (type is not ("ModifySettings" or "ApplicationInstall" or "DeviceDriverInstall")) return Fail("type must be ModifySettings, ApplicationInstall or DeviceDriverInstall");
                return (["restore-point", description!, type], ["restore-point-recover"], description!, null);
            }),
        Simple("privacy_setting", "Change one privacy setting for this account", KillerBenchTier.A,
            "Changes one supported setting for the account running KillerBench after a policy check, and keeps an exact-value undo record. Settings owned by policy are left alone.",
            ["key", "state"], input =>
            {
                if (!Key(input, "key", out var key)) return Fail("key must be a supported KillerBench privacy key such as camera-access");
                var state = Optional(input, "state");
                if (state is not ("on" or "off")) return Fail("state must be on or off");
                return (["privacy-setting", key!, state], ["privacy-setting-recover", key!], $"{key} {state}", null);
            }),
        Simple("privacy_undo", "Undo one verified privacy change", KillerBenchTier.A,
            "Restores the exact previous value recorded in a verified KillerBench privacy journal.",
            ["key", "journal"], input =>
            {
                if (!Key(input, "key", out var key)) return Fail("key must be a supported KillerBench privacy key");
                if (!Journal(input, "journal", out var journal)) return Fail("journal must be an existing verified journal under %LOCALAPPDATA%\\KillerBench");
                return (["privacy-setting-undo", key!, journal!], ["privacy-setting-recover", key!], key!, null);
            }),
        new("recipe_repair", "Run selected KillerBench repair recipes", null,
            "Runs a full diagnosis limited to the selected recipes, then repairs only what fresh evidence proves. Service recipes are safe tier; servicing, WMI, recovery environment, component cleanup and spool directory recipes are the disruptive tier.",
            ["recipes"], RecipePlan),
        Simple("system_image_repair", "Repair Windows with DISM or SFC", KillerBenchTier.B,
            "DISM can replace component-store content and download repair files from configured sources. SFC replaces damaged protected files. Both can take a long time, and a restart may be needed.",
            ["tool"], input => Optional(input, "tool") is "dism" or "sfc" ? (["repair", Optional(input, "tool")!], null, Optional(input, "tool")!.ToUpperInvariant(), 10800) : Fail("tool must be dism or sfc")),
        Simple("update_install", "Install selected Windows updates", KillerBenchTier.B,
            "Installs the exact updates selected from discovery. Installation can take a long time, may need a restart, and cannot be undone by KillerBench.",
            ["updates", "drivers", "noSoftware", "allowRestartRequired"], input =>
            {
                if (!UpdateIds(input, out var updates)) return Fail("updates must list 1 to 20 GUID:REVISION identities from update discovery");
                var args = new List<string> { "update-plan" };
                foreach (var update in updates) args.AddRange(["--select", update]);
                if (Flag(input, "drivers")) args.Add("--drivers");
                if (Flag(input, "noSoftware")) args.Add("--no-software");
                if (Flag(input, "allowRestartRequired")) args.Add("--allow-restart-required");
                args.Add("--json");
                return (args.ToArray(), ["update-recover"], $"{updates.Count} update(s)", 3600);
            }),
        Simple("update_store_reset", "Reset the Windows Update data store", KillerBenchTier.B,
            "Resets the Windows Update data store after a fresh search proves it is needed, keeps the old store, and verifies a new search.",
            ["source", "drivers", "noSoftware"], input =>
            {
                var source = Optional(input, "source") ?? "Configured";
                if (source is not ("Configured" or "WindowsUpdate" or "MicrosoftUpdate")) return Fail("source must be Configured, WindowsUpdate or MicrosoftUpdate");
                var args = new List<string> { "update-store-reset", "--source", source };
                if (Flag(input, "drivers")) args.Add("--drivers");
                if (Flag(input, "noSoftware")) args.Add("--no-software");
                return (args.ToArray(), ["update-store-reset-recover"], source, 1800);
            }),
        Simple("volume_optimize", "Optimize, retrim or defragment one volume", KillerBenchTier.B,
            "Runs the selected Windows optimization on one exact healthy volume. Defragmentation can take hours and adds disk load.",
            ["drive", "mode"], input =>
            {
                if (!Drive(input, "drive", out var drive)) return Fail("drive must look like C:");
                var mode = Optional(input, "mode") ?? "Recommended";
                if (mode is not ("Recommended" or "ReTrim" or "Defragment")) return Fail("mode must be Recommended, ReTrim or Defragment");
                return (["volume-optimize", drive!, mode], ["volume-optimize-recover"], $"{drive} {mode}", 7200);
            }),
        Simple("search_index_rebuild", "Rebuild the Windows Search index", KillerBenchTier.B,
            "Rebuilds the Windows Search index, keeps the prior index and verifies the fresh one. Search results are incomplete until indexing finishes.",
            [], _ => (["search-index-rebuild"], ["search-index-rebuild-recover"], "Windows Search", 1800)),
        Simple("firewall_enable", "Turn on Windows Firewall profiles", KillerBenchTier.B,
            "Turns on the selected unmanaged firewall profiles. Existing rules take effect and can interrupt network or remote access.",
            ["profiles"], input => Profiles(input, out var profiles) ? (["firewall-enable", .. profiles.SelectMany(profile => new[] { "--profile", profile })], ["firewall-enable-recover"], string.Join(", ", profiles), null) : Fail("profiles must list Domain, Private or Public")),
        Simple("defender_scan", "Run one Microsoft Defender scan", KillerBenchTier.B,
            "Starts one quick or full Microsoft Defender scan. A full scan can take hours and slows the computer while it runs.",
            ["scan"], input => Optional(input, "scan") is "Quick" or "Full" ? (["defender-scan", Optional(input, "scan")!], ["defender-scan-recover"], Optional(input, "scan")!, 3600) : Fail("scan must be Quick or Full")),
        Simple("bits_retry", "Retry one background transfer", KillerBenchTier.B,
            "Retries one exact BITS job after fresh owner and state checks. KillerBench does not claim the owning application completed.",
            ["jobId"], input => Guid(input, "jobId", out var job) ? (["bits-retry", job!], ["bits-retry-recover", job!], job!, null) : Fail("jobId must be a BITS job GUID")),
        Simple("time_configure", "Change the Windows Time source", KillerBenchTier.B,
            "Changes the Windows Time source to the domain hierarchy or to the listed peers, then verifies synchronization. The clock can change.",
            ["source", "peers"], input =>
            {
                var source = Optional(input, "source");
                if (source is not ("DomainHierarchy" or "CustomPeers")) return Fail("source must be DomainHierarchy or CustomPeers");
                var args = new List<string> { "time-configure", "--source", source };
                if (source == "CustomPeers")
                {
                    if (!Hosts(input, "peers", 5, out var peers)) return Fail("CustomPeers needs 1 to 5 peer hostnames");
                    foreach (var peer in peers) args.AddRange(["--peer", peer]);
                }
                return (args.ToArray(), null, source, null);
            }),
        Simple("cleanup", "Permanently remove selected temporary files and caches", KillerBenchTier.B,
            "Permanently deletes the previewed files in the selected targets. Deletion bypasses the Recycle Bin and cannot be undone.",
            ["targets", "days", "maxFiles"], input =>
            {
                if (!input.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() is < 1 or > 13) return Fail("targets must list 1 to 13 cleanup targets");
                var args = new List<string> { "cleanup" };
                foreach (var target in targets.EnumerateArray())
                {
                    if (target.ValueKind != JsonValueKind.String || !CleanupTargets.TryGetValue(target.GetString()!, out var flag)) return Fail("Unknown cleanup target: " + target);
                    if (!args.Contains(flag)) args.Add(flag);
                }
                if (!Integer(input, "days", 0, 3650, out var days, 7)) return Fail("days must be 0 to 3650");
                if (!Integer(input, "maxFiles", 1, 10000, out var maxFiles, 500)) return Fail("maxFiles must be 1 to 10000");
                args.AddRange(["--days", days.ToString(), "--max-files", maxFiles.ToString(), "--json"]);
                return (args.ToArray(), null, string.Join(", ", targets.EnumerateArray().Select(target => target.GetString())), 1800);
            }),
    ];

    public static RepairDefinition? Find(string? name) => Catalog.FirstOrDefault(definition => string.Equals(definition.Name, name, StringComparison.Ordinal));

    public static bool CanRecover(string name) => name is not ("printer_repair" or "recipe_repair" or "system_image_repair" or "time_configure" or "cleanup");

    public static IEnumerable<string> RecoverCapable() => Catalog.Where(definition => definition.Name is not ("printer_repair" or "recipe_repair" or "system_image_repair" or "time_configure" or "cleanup")).Select(definition => definition.Name);

    private static RepairDefinition Simple(string name, string title, KillerBenchTier tier, string effects, string[] options, Func<JsonElement, (string[]? Preview, string[]? Recover, string? Target, int? Seconds)> build) =>
        new(name, title, tier, effects, options, (input, _, _) =>
        {
            if (!Allowed(input, options)) return Task.FromResult<(KillerBenchRepairPlan?, string?)>((null, "Unknown option. Allowed options: " + (options.Length == 0 ? "none" : string.Join(", ", options))));
            var (preview, recover, target, seconds) = build(input);
            if (preview is null) return Task.FromResult<(KillerBenchRepairPlan?, string?)>((null, target));
            return Task.FromResult<(KillerBenchRepairPlan?, string?)>((new KillerBenchRepairPlan(name, title, tier, effects, target!, preview, [.. preview, "--execute"], recover, Math.Min(seconds ?? 300, 900), seconds ?? 600, !name.StartsWith("privacy_", StringComparison.Ordinal)), null));
        });

    private static (string[]? Preview, string[]? Recover, string? Target, int? Seconds) Fail(string message) => (null, null, message, null);

    private static async Task<(KillerBenchRepairPlan? Plan, string? Error)> RecipePlan(JsonElement input, Func<CancellationToken, Task<JsonElement?>> recipes, CancellationToken token)
    {
        if (!Allowed(input, "recipes") || !input.TryGetProperty("recipes", out var requested) || requested.ValueKind != JsonValueKind.Array || requested.GetArrayLength() is < 1 or > 8)
        {
            return (null, "recipes must list 1 to 8 recipe IDs from killerbench_repair_actions");
        }
        var ids = new List<string>();
        foreach (var item in requested.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !RecipeId().IsMatch(item.GetString()!)) return (null, "Recipe IDs look like windows.print-spooler");
            if (!ids.Contains(item.GetString()!)) ids.Add(item.GetString()!);
        }
        var catalog = await recipes(token).ConfigureAwait(false);
        if (catalog is not { ValueKind: JsonValueKind.Array } list) return (null, "KillerBench did not return its recipe catalog");
        var byId = list.EnumerateArray().Where(recipe => recipe.TryGetProperty("id", out _)).ToDictionary(recipe => recipe.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var tier = KillerBenchTier.A;
        var titles = new List<string>();
        var effects = new List<string>();
        var pending = new Queue<string>(ids);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            if (!byId.TryGetValue(id, out var recipe)) return (null, "Unknown recipe: " + id);
            var contract = Text(recipe, "recoveryContract");
            if (!Bool(recipe, "canAutoExecute") || Bool(recipe, "requiresExplicitOptions") || Bool(recipe, "diagnosticOnly")) return (null, $"{id} needs its own dedicated action and cannot run as a recipe repair");
            if (AutoTierBContracts.Contains(contract)) tier = KillerBenchTier.B;
            else if (!AutoTierAContracts.Contains(contract)) return (null, $"{id} is not available through KillerMCP");
            if (ids.Contains(id)) titles.Add(Text(recipe, "title") ?? id);
            if (Text(recipe, "effects") is { Length: > 0 } effect) effects.Add(effect);
            if (recipe.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Array)
            {
                foreach (var dependency in dependencies.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)) pending.Enqueue(dependency.GetString()!);
            }
        }
        var joined = string.Join(",", ids);
        return (new KillerBenchRepairPlan("recipe_repair", "Run selected KillerBench repair recipes", tier, string.Join(" ", effects.Distinct()), string.Join(", ", titles),
            ["full-scan", "--recipes", joined, "--json"], ["full-scan", "--recipes", joined, "--auto-repair", "--json"], null, 3900, 14400), null);
    }

    internal static bool Allowed(JsonElement input, params string[] names) => input.ValueKind == JsonValueKind.Object && input.EnumerateObject().All(property => names.Contains(property.Name, StringComparer.Ordinal));
    internal static string? Optional(JsonElement input, string name) => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Flag(JsonElement input, string name) => input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    internal static bool Integer(JsonElement input, string name, int minimum, int maximum, out int value, int? fallback = null)
    {
        value = fallback ?? 0;
        if (!input.TryGetProperty(name, out var element)) return fallback.HasValue;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= minimum && value <= maximum;
    }

    internal static bool Host(JsonElement input, string name, out string? host)
    {
        host = Optional(input, name);
        return host is not null && HostName().IsMatch(host);
    }

    private static bool Hosts(JsonElement input, string name, int maximum, out List<string> hosts)
    {
        hosts = [];
        if (!input.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() < 1 || values.GetArrayLength() > maximum) return false;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !HostName().IsMatch(value.GetString()!)) return false;
            hosts.Add(value.GetString()!);
        }
        return true;
    }

    private static bool Queue(JsonElement input, string name, out string? queue)
    {
        queue = Optional(input, name);
        return queue is not null && QueueName().IsMatch(queue);
    }

    private static bool Queues(JsonElement input, out List<string> queues)
    {
        queues = [];
        if (!input.TryGetProperty("queues", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 8) return false;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !QueueName().IsMatch(value.GetString()!)) return false;
            if (!queues.Contains(value.GetString()!)) queues.Add(value.GetString()!);
        }
        return true;
    }

    private static bool Profiles(JsonElement input, out List<string> profiles)
    {
        profiles = [];
        if (!input.TryGetProperty("profiles", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 3) return false;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not ("Domain" or "Private" or "Public")) return false;
            if (!profiles.Contains(value.GetString()!)) profiles.Add(value.GetString()!);
        }
        return true;
    }

    private static bool UpdateIds(JsonElement input, out List<string> updates)
    {
        updates = [];
        if (!input.TryGetProperty("updates", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 20) return false;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !UpdateIdentity().IsMatch(value.GetString()!)) return false;
            if (!updates.Contains(value.GetString()!)) updates.Add(value.GetString()!);
        }
        return true;
    }

    private static bool Key(JsonElement input, string name, out string? key)
    {
        key = Optional(input, name);
        return key is not null && PrivacyKey().IsMatch(key);
    }

    internal static bool Drive(JsonElement input, string name, out string? drive)
    {
        drive = Optional(input, name);
        if (drive is null || !DriveLetter().IsMatch(drive)) return false;
        drive = char.ToUpperInvariant(drive[0]) + ":";
        return true;
    }

    private static bool Guid(JsonElement input, string name, out string? value)
    {
        value = Optional(input, name);
        return value is not null && System.Guid.TryParse(value, out var parsed) && (value = parsed.ToString("B").ToUpperInvariant()) is not null;
    }

    private static bool SafeText(JsonElement input, string name, int maximum, out string? text)
    {
        text = Optional(input, name)?.Trim();
        return !string.IsNullOrEmpty(text) && text.Length <= maximum && PlainText().IsMatch(text);
    }

    private static bool Journal(JsonElement input, string name, out string? path)
    {
        path = Optional(input, name);
        if (path is null || !Path.IsPathFullyQualified(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !PlainPath().IsMatch(path)) return false;
        var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerBench")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) && (path = full) is not null;
    }

    [GeneratedRegex("^(?=.{1,253}\\z)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\.)*[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\\z", RegexOptions.CultureInvariant)] private static partial Regex HostName();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._()#,+'-]{0,127}\\z", RegexOptions.CultureInvariant)] private static partial Regex QueueName();
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)] private static partial Regex PrivacyKey();
    [GeneratedRegex("^[A-Za-z]:?\\z", RegexOptions.CultureInvariant)] private static partial Regex DriveLetter();
    [GeneratedRegex("^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}:[0-9]{1,6}\\z", RegexOptions.CultureInvariant)] private static partial Regex UpdateIdentity();
    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+){1,8}\\z", RegexOptions.CultureInvariant)] private static partial Regex RecipeId();
    [GeneratedRegex("^[A-Za-z0-9 ._,()'+-]+\\z", RegexOptions.CultureInvariant)] private static partial Regex PlainText();
    [GeneratedRegex("^[A-Za-z]:\\\\[A-Za-z0-9 ._()'+\\\\-]+\\z", RegexOptions.CultureInvariant)] private static partial Regex PlainPath();
}
