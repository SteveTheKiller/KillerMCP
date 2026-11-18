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

    // Setup and the update window save each downloaded update in a new, empty folder of its own under
    // %LOCALAPPDATA%\KillerMCP\updates, so nothing another program left in the shared temporary folder sits
    // beside the setup when it runs. A folder older than an hour belongs to an update that already ran, so it
    // is removed before the next download. One whose setup is still running cannot be deleted and is left.
    internal static class DownloadedSetups
    {
        private const string Prefix = "KillerMCP-Setup-";
        private static readonly TimeSpan Age = TimeSpan.FromHours(1);

        internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP", "updates");

        internal static string NewPath(string version) => NewPath(Root, version);

        internal static string NewPath(string root, string version)
        {
            RemoveOld(root);
            string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, Prefix + version + ".exe");
        }

        private static void RemoveOld(string root)
        {
            string[] folders;
            try { folders = Directory.GetDirectories(root); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { return; }
            DateTime cutoff = DateTime.UtcNow - Age;
            foreach (string folder in folders)
            {
                if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
                try
                {
                    // A junction is never followed; only real folders this class made are emptied.
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                    if (Directory.GetLastWriteTimeUtc(folder) >= cutoff) continue;
                    foreach (string file in Directory.GetFiles(folder))
                        if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                    Directory.Delete(folder, false);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { }
            }
        }
    }
}
