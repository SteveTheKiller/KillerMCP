using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

/// <summary>Which KillerBench repair tiers an MCP client may request. Off unless the user opts in.</summary>
public enum KillerBenchRepairLevel
{
    Off,
    Safe,
    All,
}

/// <summary>
/// Local KillerMCP settings and repair state for KillerBench. Both files live beside the other
/// KillerMCP state in %LOCALAPPDATA%\KillerMCP and are only changed by the user through
/// KillerMCP.Configure or by the repair lock that an uncertain outcome sets.
/// </summary>
public static class KillerBenchSettings
{
    public const string SettingName = "killerBenchRepairs";

    public static string Folder => Environment.GetEnvironmentVariable("KILLERMCP_STATE_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP");

    public static string SettingsPath => Path.Combine(Folder, "settings.json");
    public static string LockPath => Path.Combine(Folder, "killerbench-repair-lock.json");
    public static string AuditPath => Path.Combine(Folder, "killerbench-actions.jsonl");

    public static KillerBenchRepairLevel ReadLevel()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (document.RootElement.TryGetProperty(SettingName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return Parse(value.GetString()) ?? KillerBenchRepairLevel.Off;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return KillerBenchRepairLevel.Off;
    }

    public static KillerBenchRepairLevel? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "off" => KillerBenchRepairLevel.Off,
        "safe" => KillerBenchRepairLevel.Safe,
        "all" => KillerBenchRepairLevel.All,
        _ => null,
    };

    public static string Name(KillerBenchRepairLevel level) => level switch
    {
        KillerBenchRepairLevel.Safe => "safe",
        KillerBenchRepairLevel.All => "all",
        _ => "off",
    };

    /// <summary>Writes the level and preserves every other setting already in the file.</summary>
    public static void WriteLevel(KillerBenchRepairLevel level)
    {
        Directory.CreateDirectory(Folder);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            root = new JsonObject();
        }
        root[SettingName] = Name(level);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(temporary, SettingsPath, true);
    }

    public static RepairLock? ReadLock()
    {
        try
        {
            return JsonSerializer.Deserialize<RepairLock>(File.ReadAllText(LockPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void WriteLock(RepairLock repairLock)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(LockPath, JsonSerializer.Serialize(repairLock) + Environment.NewLine);
    }

    public static bool ClearLock()
    {
        try
        {
            if (!File.Exists(LockPath)) return false;
            File.Delete(LockPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Audit(object entry)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(AuditPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("KillerMCP could not write its KillerBench action log.");
        }
    }
}

public sealed record RepairLock(string Action, string Reason, DateTimeOffset LockedAtUtc, int? ExitCode);
