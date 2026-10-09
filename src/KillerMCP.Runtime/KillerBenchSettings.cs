using System.Text.Json;

namespace KillerMCP.Runtime;

/// <summary>Which KillerBench repair tiers an MCP client may request. Off unless the user opts in.</summary>
public enum KillerBenchRepairLevel
{
    Off,
    Safe,
    All,
}

/// <summary>
/// KillerBench repair settings that KillerMCP honors. KillerBench owns the user's choice: it writes
/// %LOCALAPPDATA%\KillerBench\agent-repairs.json from the KillerMCP section of its About window, and
/// KillerMCP only reads it. KillerMCP writes the lock after an uncertain result, and the user clears
/// it from the same About section. The action log stays with the other KillerMCP state.
/// </summary>
public static class KillerBenchSettings
{
    private const int FileLimit = 16_384;

    public static string Folder => Environment.GetEnvironmentVariable("KILLERMCP_STATE_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP");

    public static string BenchFolder => Environment.GetEnvironmentVariable("KILLERBENCH_STATE_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerBench");

    public static string SettingsPath => Path.Combine(BenchFolder, "agent-repairs.json");
    public static string LockPath => Path.Combine(BenchFolder, "agent-repair-lock.json");
    public static string AuditPath => Path.Combine(Folder, "killerbench-actions.jsonl");

    public static KillerBenchRepairLevel ReadLevel()
    {
        try
        {
            var file = new FileInfo(SettingsPath);
            if (!file.Exists || file.Length > FileLimit) return KillerBenchRepairLevel.Off;
            using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("level", out var value) && value.ValueKind == JsonValueKind.String)
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

    /// <summary>Writes the same file KillerBench writes. KillerMCP itself never calls this; checks use it.</summary>
    public static void WriteLevel(KillerBenchRepairLevel level)
    {
        Directory.CreateDirectory(BenchFolder);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { level = Name(level) }) + Environment.NewLine);
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
        Directory.CreateDirectory(BenchFolder);
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

public sealed record RepairLock(string Action, string Reason, DateTimeOffset LockedAtUtc, int? ExitCode, string? Title = null);
