using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
            if (!SetupDialog.ConfirmUpdate(latestText!)) return 0;

            try
            {
                string installer = DownloadAndVerify(latestText!);
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
            string path = Path.Combine(Path.GetTempPath(), "KillerMCP-Setup-" + version + "-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllBytes(path, installer);
            if (!HasTrustedSignature(path))
            {
                File.Delete(path);
                throw new InvalidDataException("The update does not have a trusted signature.");
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

        internal static bool HasTrustedSignature(string path)
        {
            var file = new WinTrustFileInfo(path);
            IntPtr filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            IntPtr dataPointer = IntPtr.Zero;
            IntPtr actionPointer = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(file, filePointer, false);
                dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
                Marshal.StructureToPtr(new WinTrustData(filePointer), dataPointer, false);
                actionPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Guid>());
                Marshal.StructureToPtr(new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), actionPointer, false);
                return WinVerifyTrust(IntPtr.Zero, actionPointer, dataPointer) == 0;
            }
            finally
            {
                if (actionPointer != IntPtr.Zero) Marshal.FreeHGlobal(actionPointer);
                if (dataPointer != IntPtr.Zero) Marshal.FreeHGlobal(dataPointer);
                Marshal.DestroyStructure<WinTrustFileInfo>(filePointer);
                Marshal.FreeHGlobal(filePointer);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr hwnd, IntPtr actionId, IntPtr trustData);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint Size;
            [MarshalAs(UnmanagedType.LPWStr)] public string Path;
            public IntPtr Handle;
            public IntPtr Subject;
            public WinTrustFileInfo(string path) { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(); Path = path; Handle = IntPtr.Zero; Subject = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
            public WinTrustData(IntPtr file)
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>();
                PolicyCallbackData = IntPtr.Zero; SipClientData = IntPtr.Zero;
                UiChoice = 2; RevocationChecks = 0; UnionChoice = 1; FileInfo = file;
                StateAction = 0; StateData = IntPtr.Zero; UrlReference = IntPtr.Zero;
                ProviderFlags = 0x00000010; UiContext = 0;
            }
        }
    }
}
