using System.Security.Cryptography;
using System.Text.Json;

namespace KillerMCP;

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
    private static readonly AsyncLocal<(string Mcp, string Bench)?> TestFolders = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ApprovedSettings = new(StringComparer.Ordinal);

    public static string Folder => TestFolders.Value?.Mcp
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP");

    public static string BenchFolder => TestFolders.Value?.Bench
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerBench");

    public static string SettingsPath => Path.Combine(BenchFolder, "agent-repairs.json");
    public static string LockPath => Path.Combine(BenchFolder, "agent-repair-lock.json");
    public static string AuditPath => Path.Combine(Folder, "killerbench-actions.jsonl");
    public static string ApprovalPath => Path.Combine(Folder, "killerbench-settings-approved.json");

    public static KillerBenchRepairLevel ReadLevel() => ReadSettings().Level;

    /// <summary>The repair level and the SHA-256 of the setting file it came from, read in one pass.
    /// The hash is null when the file is missing or unreadable.</summary>
    public static (KillerBenchRepairLevel Level, string? Hash) ReadSettings()
    {
        try
        {
            var file = new FileInfo(SettingsPath);
            if (!file.Exists || file.Length > FileLimit) return (KillerBenchRepairLevel.Off, null);
            var bytes = File.ReadAllBytes(file.FullName);
            if (bytes.Length > FileLimit) return (KillerBenchRepairLevel.Off, null);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("level", out var value) && value.ValueKind == JsonValueKind.String)
            {
                return (Parse(value.GetString()) ?? KillerBenchRepairLevel.Off, hash);
            }
            return (KillerBenchRepairLevel.Off, hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return (KillerBenchRepairLevel.Off, null);
    }

    /// <summary>Approval belongs to this running host, never to an editable settings file.</summary>
    public static bool IsApproved(string hash) => ApprovedSettings.ContainsKey(hash);

    /// <summary>Records desktop consent for the current settings only for this host's lifetime.</summary>
    public static void Approve(string hash) => ApprovedSettings.TryAdd(hash, 0);

    internal static IDisposable UseTestFolders(string mcp, string bench)
    {
        var previous = TestFolders.Value;
        TestFolders.Value = (mcp, bench);
        return new TestFolderScope(previous);
    }

    private sealed class TestFolderScope((string Mcp, string Bench)? previous) : IDisposable
    {
        public void Dispose() => TestFolders.Value = previous;
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

    /// <summary>The lock, or null only when there is no lock file. A lock file that exists but cannot be
    /// read or understood still locks repairs.</summary>
    public static RepairLock? ReadLock()
    {
        try
        {
            // File.Exists answers false when it cannot look, so the lock is probed directly and only a lock that
            // is certainly absent counts as none.
            if ((File.GetAttributes(LockPath) & FileAttributes.Directory) != 0)
                return new RepairLock("unknown", $"a folder is where the repair lock file belongs. Delete the folder {LockPath} to clear it", DateTimeOffset.UtcNow, null, "An earlier repair");
            var file = new FileInfo(LockPath);
            if (file.Length <= FileLimit
                && JsonSerializer.Deserialize<RepairLock>(File.ReadAllText(LockPath)) is { Action.Length: > 0, Reason.Length: > 0 } repairLock)
                return repairLock;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
        }
        return new RepairLock("unknown", "the repair lock file could not be read", DateTimeOffset.UtcNow, null, "An earlier repair");
    }

    public static void WriteLock(RepairLock repairLock)
    {
        Directory.CreateDirectory(BenchFolder);
        var temporary = LockPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(repairLock) + Environment.NewLine);
            File.Move(temporary, LockPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static string RepairHoldPath => Path.Combine(Folder, "killerbench-repair.hold");

    /// <summary>
    /// Claims the one repair that may run on this PC at a time, across every KillerMCP host. Returns
    /// null when another host holds it. Disposing the result releases it, and Windows releases it if
    /// the host ends.
    /// </summary>
    public static IDisposable? HoldRepair()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            return new FileStream(RepairHoldPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
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
