using System;
using System.IO;

namespace KillerMCP.Updates
{
    // Which release setup updates an installation. One that carries its own .NET runtime came from the
    // Microsoft Store setup and updates with it, so it keeps working on a computer without .NET 10.
    internal static class SetupAsset
    {
        internal const string Regular = "KillerMCP-Setup.exe";
        internal const string Store = "KillerMCP-Store.exe";

        internal static string For(string installFolder)
            => File.Exists(Path.Combine(installFolder, "coreclr.dll")) && File.Exists(Path.Combine(installFolder, "System.Private.CoreLib.dll"))
                ? Store : Regular;

        // The SHA256SUMS.txt hash for exactly this file name.
        internal static string? ExpectedHash(string sums, string asset)
        {
            foreach (string line in sums.Replace("\r", "").Split('\n'))
            {
                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*'), asset, StringComparison.OrdinalIgnoreCase)) return parts[0];
            }
            return null;
        }
    }

    // Setup and the update window save each downloaded update as a new file in the temporary folder. A copy
    // older than an hour belongs to an update that already ran, so it is removed before the next download.
    // A copy that is still running cannot be deleted and is left for a later download to remove.
    internal static class DownloadedSetups
    {
        private const string Prefix = "KillerMCP-Setup-";
        private static readonly TimeSpan Age = TimeSpan.FromHours(1);

        internal static string NewPath(string folder, string version)
        {
            RemoveOld(folder);
            return Path.Combine(folder, Prefix + version + "-" + Guid.NewGuid().ToString("N") + ".exe");
        }

        private static void RemoveOld(string folder)
        {
            string[] files;
            try { files = Directory.GetFiles(folder, Prefix + "*.exe"); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { return; }
            DateTime cutoff = DateTime.UtcNow - Age;
            foreach (string file in files)
            {
                if (!Path.GetFileName(file).StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
                try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { }
            }
        }
    }
}
