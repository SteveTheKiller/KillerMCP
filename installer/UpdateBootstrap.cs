using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace KillerMCP.Setup
{
    internal static class UpdateBootstrap
    {
        private const string ReleaseApi = "https://api.github.com/repos/SteveTheKiller/KillerMCP/releases/latest";
        private const string ReleaseBase = "https://github.com/SteveTheKiller/KillerMCP/releases/download/";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "KillerMCP Update Check";

        internal static string RunKeyPath(string destination) =>
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KILLERMCP_TEST_INSTALL_ROOT"))
                ? RunKey
                : @"Software\KillerMCP\InstallerTests\" + Path.GetFileName(Path.GetDirectoryName(destination)) + @"\Run";

        internal static void Register(string destination)
        {
            string command = Program.Quote(destination + "-Setup.exe") + " /check-update";
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath(destination))
                ?? throw new InvalidOperationException("Could not register the KillerMCP update check."))
            {
                key.SetValue(RunValue, command);
            }
        }

        internal static void Unregister(string destination)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath(destination), true))
            {
                string expected = Program.Quote(destination + "-Setup.exe") + " /check-update";
                if (string.Equals(key?.GetValue(RunValue) as string, expected, StringComparison.OrdinalIgnoreCase))
                    key!.DeleteValue(RunValue, false);
            }
        }

        internal static int Check(string destination)
        {
            bool isolated = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KILLERMCP_TEST_INSTALL_ROOT"));
            string behavior = UpdatePreferences.Read(UpdatePreferences.PathFor(destination, isolated));
            if (behavior == "off") return 0;
            string host = Path.Combine(destination, "KillerMCP.exe");
            if (!File.Exists(host)) return 0;
            string? currentText = FileVersionInfo.GetVersionInfo(host).FileVersion;
            if (!Version.TryParse(currentText, out Version? current)) return 0;

            string? latestText;
            try
            {
                using (var client = Client(TimeSpan.FromSeconds(8)))
                {
                    string endpoint = Environment.GetEnvironmentVariable("KILLERMCP_TEST_UPDATE_API") ?? ReleaseApi;
                    string json = client.GetStringAsync(endpoint).GetAwaiter().GetResult();
                    var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    latestText = (release?["tag_name"] as string)?.TrimStart('v', 'V');
                }
            }
            catch { return 0; }
            if (!Version.TryParse(latestText, out Version? latest) || latest <= current) return 0;
            try
            {
                string notifier = Path.Combine(destination, "KillerMCP.Notify.exe");
                if (File.Exists(notifier))
                {
                    string arguments = "--installed " + Program.Quote(currentText!) +
                        " --latest " + Program.Quote(latestText!) +
                        " --installer " + Program.Quote(ReleaseBase + "v" + latestText + "/KillerMCP-Setup.exe") +
                        " --release " + Program.Quote("https://github.com/SteveTheKiller/KillerMCP/releases/tag/v" + latestText) +
                        " --behavior " + Program.Quote(behavior);
                    Process.Start(new ProcessStartInfo(notifier, arguments) { UseShellExecute = true });
                    return 0;
                }
                if (behavior == "check" && !SetupDialog.ConfirmUpdate(latestText!)) return 0;
                string installer = DownloadAndVerify(latestText!);
                if (behavior == "download" && !SetupDialog.ConfirmUpdate(latestText!)) return 0;
                int parent = Process.GetCurrentProcess().Id;
                Process.Start(new ProcessStartInfo(installer, "/silent /notify /wait-for " + parent) { UseShellExecute = true });
                return 0;
            }
            catch (Exception exception)
            {
                SetupDialog.ShowFailure("The signed update could not be installed: " + exception.Message);
                return 1;
            }
        }

        private static string DownloadAndVerify(string version)
        {
            string baseUrl = ReleaseBase + "v" + version + "/";
            byte[] installer;
            string sums;
            using (var client = Client(TimeSpan.FromMinutes(3)))
            {
                installer = client.GetByteArrayAsync(baseUrl + "KillerMCP-Setup.exe").GetAwaiter().GetResult();
                sums = client.GetStringAsync(baseUrl + "SHA256SUMS.txt").GetAwaiter().GetResult();
            }
            string? line = sums.Replace("\r", "").Split('\n').FirstOrDefault(value => value.EndsWith("  KillerMCP-Setup.exe", StringComparison.OrdinalIgnoreCase));
            if (line == null) throw new InvalidDataException("The release checksum is missing.");
            string expected = line.Split(' ')[0];
            string actual;
            using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(installer)).Replace("-", "");
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The release checksum does not match.");
            string path = KillerMCP.Updates.DownloadedSetups.NewPath(Path.GetTempPath(), version);
            File.WriteAllBytes(path, installer);
            if (!KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(path))
            {
                File.Delete(path);
                throw new InvalidDataException("The update is not signed by the KillerMCP publisher, or its signature could not be checked.");
            }
            return path;
        }

        private static HttpClient Client(TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KillerMCP-Setup/" + Program.CurrentVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

    }
}
