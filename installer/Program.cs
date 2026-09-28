using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace KillerMCP.Setup
{
    internal static class Program
    {
        private const string PayloadName = "KillerMCP.payload.zip";
        private const string TestRootVariable = "KILLERMCP_TEST_INSTALL_ROOT";
        private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerMCP";
        internal const int MissingRuntimeExitCode = 10;

        internal static string CurrentVersion
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                return version.Major + "." + version.Minor + "." + version.Build;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string? testRoot = Environment.GetEnvironmentVariable(TestRootVariable);
            bool isolated = !string.IsNullOrWhiteSpace(testRoot);
            string destination = isolated ? ValidateTestRoot(testRoot!) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KillerMCP");
            bool silent = args.Any(value => value.Equals("/silent", StringComparison.OrdinalIgnoreCase));
            bool notify = args.Any(value => value.Equals("/notify", StringComparison.OrdinalIgnoreCase));
            bool uninstall = args.Any(value => value.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            bool checkUpdate = args.Any(value => value.Equals("/check-update", StringComparison.OrdinalIgnoreCase));
            try
            {
                int waitIndex = Array.FindIndex(args, value => value.Equals("/wait-for", StringComparison.OrdinalIgnoreCase));
                if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out int parentId))
                {
                    try { using (Process parent = Process.GetProcessById(parentId)) parent.WaitForExit(30000); }
                    catch (ArgumentException) { }
                }
                if (checkUpdate) return UpdateBootstrap.Check(destination);
                if (uninstall)
                {
                    if (!silent && !SetupDialog.ConfirmUninstall()) return 0;
                    Uninstall(destination, removeRegistration: true);
                    RemoveInstalledApp(destination);
                    return 0;
                }
                if (silent)
                {
                    if (!HasRuntime10(isolated)) return MissingRuntimeExitCode;
                    Install(destination);
                    if (notify) NotifySuccess(destination);
                    return 0;
                }
                return InstallerWizard.Run(destination, isolated);
            }
            catch (Exception exception)
            {
                if (silent) Console.Error.WriteLine("KillerMCP setup failed: " + exception.Message);
                else SetupDialog.ShowFailure(exception.Message);
                return 1;
            }
        }

        internal static bool HasRuntime10(bool isolated)
        {
            if (isolated)
            {
                string? test = Environment.GetEnvironmentVariable("KILLERMCP_TEST_RUNTIME");
                if (test == "0") return false;
                if (test == "1") return true;
            }
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App");
            try { return Directory.Exists(root) && Directory.GetDirectories(root, "10.*").Length > 0; }
            catch { return false; }
        }

        internal static void Install(string destination)
        {
            ValidateSetupRegistration(destination);
            string parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("The installation directory is unavailable.");
            Directory.CreateDirectory(parent);
            string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
            bool movedPrevious = false;
            try
            {
                if (Directory.Exists(destination))
                {
                    ValidateInstalled(destination, allowLegacy: true);
                    CloseRunningProcesses(destination);
                }
                ExtractAndVerify(staging);
                if (Directory.Exists(destination)) { Directory.Move(destination, backup); movedPrevious = true; }
                try { Directory.Move(staging, destination); }
                catch { if (movedPrevious) Directory.Move(backup, destination); throw; }
                try { Configure(destination, "register"); }
                catch
                {
                    DeleteVerifiedInstallation(destination);
                    if (movedPrevious) Directory.Move(backup, destination);
                    throw;
                }
                if (movedPrevious) DeleteVerifiedInstallation(backup, allowLegacy: true);
                RegisterInstalledApp(destination);
                UpdateBootstrap.Register(destination);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        internal static void Uninstall(string destination, bool removeRegistration)
        {
            if (!Directory.Exists(destination)) return;
            ValidateInstalled(destination);
            if (removeRegistration) Configure(destination, "unregister");
            DeleteVerifiedInstallation(destination);
        }

        internal static void UninstallRegistered(string destination)
        {
            Uninstall(destination, removeRegistration: true);
            RemoveInstalledApp(destination);
        }

        internal static string? InstalledVersion(string destination)
        {
            if (!Directory.Exists(destination)) return null;
            ValidateInstalled(destination, allowLegacy: true);
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath(destination)))
            {
                string? version = key?.GetValue("DisplayVersion") as string;
                if (!string.IsNullOrWhiteSpace(version)) return version;
            }
            string executable = Path.Combine(destination, "KillerMCP.exe");
            return File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).FileVersion : "installed";
        }

        private static void Configure(string destination, string action)
        {
            string executable = Path.Combine(destination, "KillerMCP.exe");
            string configurator = Path.Combine(destination, "KillerMCP.Configure.exe");
            var start = new ProcessStartInfo(configurator)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = destination,
                Arguments = action + " " + Quote(executable),
            };
            using (Process? process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("KillerMCP client configuration did not start.");
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new TimeoutException("KillerMCP client configuration timed out."); }
                if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
            }
        }

        private static void NotifySuccess(string destination)
        {
            string notifier = Path.Combine(destination, "KillerMCP.Notify.exe");
            if (!File.Exists(notifier)) return;
            Process.Start(new ProcessStartInfo(notifier, "--updated --version " + Quote(CurrentVersion)) { UseShellExecute = true });
        }

        private static void CloseRunningProcesses(string destination)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            int currentProcessId = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == currentProcessId) continue;
                    string? executable;
                    try { executable = process.MainModule?.FileName; }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(executable) || !Path.GetFullPath(executable).StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        process.Kill();
                        if (!process.WaitForExit(10000)) throw new TimeoutException();
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException("KillerMCP could not close its running host. Close connected agent clients and try again.", exception);
                    }
                }
            }
        }

        private static void ExtractAndVerify(string staging)
        {
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName) ?? throw new InvalidOperationException("The installer payload is missing."))
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                Directory.CreateDirectory(staging);
                string root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string target = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The package contains an unsafe path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? staging);
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                }
            }
            ValidateInstalled(staging);
        }

        private static Manifest ValidateInstalled(string destination, bool allowLegacy = false)
        {
            string manifestPath = Path.Combine(destination, "manifest.json");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            Manifest manifest = serializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("The installation manifest is invalid.");
            Dictionary<string, ManifestFile> files = manifest.files ?? throw new InvalidDataException("The installation manifest does not list any files.");
            bool native = files.ContainsKey("KillerMCP.exe") && files.ContainsKey("KillerMCP.Configure.exe");
            bool legacy = files.ContainsKey("node.exe") && files.ContainsKey("killermcp.mjs") && files.ContainsKey("killertools.mjs");
            if (!native && !(allowLegacy && legacy)) throw new InvalidDataException("The package does not contain a supported KillerMCP runtime.");
            string[] actual = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Select(path => path.Substring(destination.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/')).Where(name => name != "manifest.json").ToArray();
            if (actual.Length != files.Count || actual.Any(name => !files.ContainsKey(name))) throw new InvalidOperationException("The installation folder contains files outside the KillerMCP manifest.");
            foreach (KeyValuePair<string, ManifestFile> item in files)
            {
                string path = Path.Combine(destination, item.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path) || new FileInfo(path).Length != item.Value.bytes || !Sha256(path).Equals(item.Value.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package verification failed for " + item.Key);
            }
            return manifest;
        }

        private static void DeleteVerifiedInstallation(string destination, bool allowLegacy = false)
        {
            Manifest manifest = ValidateInstalled(destination, allowLegacy);
            foreach (string name in manifest.files!.Keys) File.Delete(Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar)));
            File.Delete(Path.Combine(destination, "manifest.json"));
            foreach (string directory in Directory.GetDirectories(destination, "*", SearchOption.AllDirectories).OrderByDescending(value => value.Length)) Directory.Delete(directory);
            Directory.Delete(destination);
        }

        private static string SetupCopyPath(string destination) => destination + "-Setup.exe";
        private static string RegistryPath(string destination) => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestRootVariable)) ? UninstallRegistryPath : @"Software\KillerMCP\InstallerTests\" + Path.GetFileName(Path.GetDirectoryName(destination));

        private static void RegisterInstalledApp(string destination)
        {
            string setup = SetupCopyPath(destination);
            string source = Assembly.GetExecutingAssembly().Location;
            if (!Path.GetFullPath(source).Equals(Path.GetFullPath(setup), StringComparison.OrdinalIgnoreCase)) File.Copy(source, setup, true);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath(destination)) ?? throw new InvalidOperationException("Could not create the KillerMCP uninstall entry."))
            {
                key.SetValue("DisplayName", "KillerMCP");
                key.SetValue("DisplayVersion", CurrentVersion);
                key.SetValue("Publisher", "Steve the Killer");
                key.SetValue("InstallLocation", destination);
                key.SetValue("DisplayIcon", setup + ",0");
                key.SetValue("UninstallString", Quote(setup) + " /uninstall");
                key.SetValue("QuietUninstallString", Quote(setup) + " /silent /uninstall");
                key.SetValue("SetupSha256", Sha256(setup));
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath(destination)) ?? throw new InvalidOperationException("KillerMCP could not verify its uninstall entry."))
            {
                if (!string.Equals(key.GetValue("DisplayVersion") as string, CurrentVersion, StringComparison.Ordinal)
                    || !string.Equals(key.GetValue("SetupSha256") as string, Sha256(setup), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("KillerMCP could not verify its updated uninstall entry.");
                }
            }
        }

        private static void ValidateSetupRegistration(string destination)
        {
            string setup = SetupCopyPath(destination);
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath(destination)))
            {
                if (!File.Exists(setup)) { if (key != null) throw new InvalidOperationException("The installed KillerMCP setup file is missing."); return; }
                if (key != null && string.Equals(key.GetValue("SetupSha256") as string, Sha256(setup), StringComparison.OrdinalIgnoreCase)) return;
                if (!IsRecoverableSetup(destination, setup, key)) throw new InvalidOperationException(key == null ? "An unregistered KillerMCP setup file already exists." : "The installed KillerMCP setup file or uninstall entry has changed.");
            }
        }

        private static bool IsRecoverableSetup(string destination, string setup, RegistryKey? key)
        {
            try
            {
                Manifest manifest = ValidateInstalled(destination, allowLegacy: true);
                Dictionary<string, ManifestFile> files = manifest.files!;
                bool legacy = files.ContainsKey("node.exe") && files.ContainsKey("killermcp.mjs") && files.ContainsKey("killertools.mjs");
                FileVersionInfo information = FileVersionInfo.GetVersionInfo(setup);
                if (!string.Equals(information.ProductName, "KillerMCP-Setup", StringComparison.Ordinal)) return false;
                if (key == null) return legacy;
                return Path.GetFullPath(key.GetValue("InstallLocation") as string ?? string.Empty).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)
                    && Version.TryParse(key.GetValue("DisplayVersion") as string, out Version? registeredVersion)
                    && Version.TryParse(CurrentVersion, out Version? currentVersion)
                    && registeredVersion < currentVersion
                    && Version.TryParse(information.FileVersion, out Version? setupVersion)
                    && setupVersion.Major == currentVersion.Major
                    && setupVersion.Minor == currentVersion.Minor
                    && setupVersion.Build == currentVersion.Build;
            }
            catch { return false; }
        }

        private static void RemoveInstalledApp(string destination)
        {
            string setup = SetupCopyPath(destination);
            UpdateBootstrap.Unregister(destination);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath(destination), false);
            if (!File.Exists(setup)) return;
            if (!Path.GetFullPath(Assembly.GetExecutingAssembly().Location).Equals(Path.GetFullPath(setup), StringComparison.OrdinalIgnoreCase)) { File.Delete(setup); return; }
            string script = "Wait-Process -Id " + Process.GetCurrentProcess().Id + " -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '" + setup.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded) { UseShellExecute = false, CreateNoWindow = true });
        }

        private static string ValidateTestRoot(string value)
        {
            string root = Path.GetFullPath(value);
            string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || root.TrimEnd(Path.DirectorySeparatorChar).Equals(temporary.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The test install directory must be inside the temporary directory.");
            return root;
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }
        internal static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private sealed class Manifest { public Dictionary<string, ManifestFile>? files { get; set; } }
        private sealed class ManifestFile { public long bytes { get; set; } public string? sha256 { get; set; } }

    }
}
