using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
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
            KillerFamily.Accessibility.AccessibleControls.Initialize();
            string? testRoot = Environment.GetEnvironmentVariable(TestRootVariable);
            bool isolated = !string.IsNullOrWhiteSpace(testRoot);
            string destination = isolated ? ValidateTestRoot(testRoot!) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KillerMCP");
            bool silent = args.Any(value => value.Equals("/silent", StringComparison.OrdinalIgnoreCase));
            bool notify = args.Any(value => value.Equals("/notify", StringComparison.OrdinalIgnoreCase));
            bool uninstall = args.Any(value => value.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            bool checkUpdate = args.Any(value => value.Equals("/check-update", StringComparison.OrdinalIgnoreCase));
            try
            {
                // An update can inherit the installed host's working directory. Windows keeps
                // that directory open even after every host exits, preventing its replacement.
                Environment.CurrentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
                int waitIndex = Array.FindIndex(args, value => value.Equals("/wait-for", StringComparison.OrdinalIgnoreCase));
                if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out int parentId))
                {
                    try { using (Process parent = Process.GetProcessById(parentId)) parent.WaitForExit(30000); }
                    catch (ArgumentException) { }
                }
                if (checkUpdate) return UpdateBootstrap.Check(destination);
                if (uninstall)
                {
                    TaskbarIdentity.UseUninstall();
                    if (!silent && !SetupDialog.ConfirmUninstall()) return 0;
                    IReadOnlyList<string> warnings = Uninstall(destination, removeRegistration: true);
                    RemoveInstalledApp(destination);
                    foreach (string warning in warnings) Console.Error.WriteLine(warning);
                    if (!silent && warnings.Count > 0) SetupDialog.ShowFailure(ClientWarningText(warnings));
                    return 0;
                }
                if (silent)
                {
                    // An unsigned or tampered setup must not install itself unattended.
                    if (!isolated && !IsTrustedSetup(Assembly.GetExecutingAssembly().Location))
                    {
                        Console.Error.WriteLine("Silent install refused: setup has no valid Authenticode signature.");
                        return KillerFamily.Deployment.InstallerExitCodes.SecurityRejected;
                    }
                    if (!HasRuntime10(isolated)) return MissingRuntimeExitCode;
                    int? detached = ClientJobEscape.RunOutsideClientJob(args);
                    if (detached.HasValue) return detached.Value;
                    Install(destination);
                    if (notify) NotifySuccess(destination);
                    return 0;
                }
                int? detachedWizard = ClientJobEscape.RunOutsideClientJob(args);
                if (detachedWizard.HasValue) return detachedWizard.Value;
                TaskbarIdentity.UseInstaller();
                return InstallerWizard.Run(destination, isolated);
            }
            catch (Exception exception)
            {
                if (silent) Console.Error.WriteLine("KillerMCP setup failed: " + exception.Message);
                // An update started from the update window or update check has no one reading its console.
                if (!silent || notify) SetupDialog.ShowFailure(exception.Message);
                return KillerFamily.Deployment.InstallerExitCodes.FromException(exception);
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
            using var installLease = KillerFamily.Deployment.InstallerExitCodes.Acquire("KillerMCP");
            ValidateSetupRegistration(destination);
            string parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("The installation directory is unavailable.");
            Directory.CreateDirectory(parent);
            string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
            bool movedPrevious = false;
            try
            {
                RemoveInterruptedRemovals(destination);
                if (Directory.Exists(destination) && !ContainsFiles(destination)) Directory.Delete(destination, true);
                if (Directory.Exists(destination))
                {
                    // A folder that an interrupted uninstall left partly removed is still replaced,
                    // as long as every file in it is one KillerMCP installed.
                    ValidateInstalled(destination, allowLegacy: true, allowMissing: true);
                }
                ExtractAndVerify(staging);
                if (Directory.Exists(destination))
                {
                    // Agent clients start a closed host again on their own, so hosts are closed right before the
                    // folder moves, and once more if one started again in between.
                    CloseRunningProcesses(destination);
                    try { Directory.Move(destination, backup); }
                    catch (IOException) { CloseRunningProcesses(destination); Directory.Move(destination, backup); }
                    movedPrevious = true;
                }
                try { Directory.Move(staging, destination); }
                catch { if (movedPrevious) Directory.Move(backup, destination); throw; }
                try
                {
                    // Clients with commented settings are left for the user, with the exact entry to add.
                    InstallWarnings = Configure(destination, "register").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith("Warning: ", StringComparison.Ordinal)).ToArray();
                    foreach (string warning in InstallWarnings) Console.Error.WriteLine(warning);
                }
                catch
                {
                    DeleteVerifiedInstallation(destination);
                    if (movedPrevious) Directory.Move(backup, destination);
                    throw;
                }
                RegisterInstalledApp(destination);
                UpdateBootstrap.Register(destination);
                // The new version is in place and registered. A backup that cannot be removed now is left for
                // the next install to clear instead of failing an install that succeeded.
                if (movedPrevious)
                {
                    try { DeleteVerifiedInstallation(backup, allowLegacy: true, allowMissing: true); }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is InvalidOperationException) { }
                }
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        // Returns the agent clients that still list KillerMCP because they could not be changed.
        internal static IReadOnlyList<string> Uninstall(string destination, bool removeRegistration)
        {
            var warnings = new List<string>();
            RemoveInterruptedRemovals(destination);
            if (!Directory.Exists(destination)) return warnings;
            if (!ContainsFiles(destination)) { Directory.Delete(destination, true); return warnings; }
            ValidateInstalled(destination, allowMissing: true);
            if (removeRegistration)
            {
                if (File.Exists(Path.Combine(destination, "KillerMCP.Configure.exe")))
                    warnings.AddRange(Configure(destination, "unregister").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(line => line.StartsWith("Warning: ", StringComparison.Ordinal)));
                else warnings.Add("Warning: KillerMCP.Configure.exe is missing, so agent clients still list KillerMCP. Remove the killermcp entry from their MCP settings.");
            }
            // Agent clients keep the host running; deleting around it would strand a partial folder.
            CloseRunningProcesses(destination);
            DeleteVerifiedInstallation(destination, allowMissing: true);
            return warnings;
        }

        internal static IReadOnlyList<string> InstallWarnings { get; private set; } = Array.Empty<string>();

        internal static IReadOnlyList<string> UninstallRegistered(string destination)
        {
            IReadOnlyList<string> warnings = Uninstall(destination, removeRegistration: true);
            RemoveInstalledApp(destination);
            return warnings;
        }

        internal static string ClientWarningText(IReadOnlyList<string> warnings) =>
            "KillerMCP was removed, but these agent clients could not be updated:" + Environment.NewLine + string.Join(Environment.NewLine, warnings.Select(warning => warning.Substring("Warning: ".Length)));

        internal static string? InstalledVersion(string destination)
        {
            if (!Directory.Exists(destination) || !ContainsFiles(destination)) return null;
            ValidateInstalled(destination, allowLegacy: true, allowMissing: true);
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath(destination)))
            {
                string? version = key?.GetValue("DisplayVersion") as string;
                if (!string.IsNullOrWhiteSpace(version)) return version;
            }
            string executable = Path.Combine(destination, "KillerMCP.exe");
            return File.Exists(executable) ? FileVersionInfo.GetVersionInfo(executable).FileVersion : "installed";
        }

        private static string Configure(string destination, string action)
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
                // Both streams are read at once, so a configurator that fills one cannot stall on the other
                // and the time limit holds while it is still writing.
                Task<string> outputRead = process.StandardOutput.ReadToEndAsync();
                Task<string> errorRead = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(ConfigureTimeout()))
                {
                    KillTree(process);
                    throw new TimeoutException("KillerMCP client configuration did not finish in time. Close agent clients such as Codex and run setup again.");
                }
                process.WaitForExit();
                // A program the configurator started can keep its output open after it exits.
                Task.WaitAll(new Task[] { outputRead, errorRead }, 5000);
                string output = outputRead.Status == TaskStatus.RanToCompletion ? outputRead.Result : string.Empty;
                string error = errorRead.Status == TaskStatus.RanToCompletion ? errorRead.Result : string.Empty;
                if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
                return output;
            }
        }

        // Above the configurator's own limits for each Codex call, so only a configurator that has stopped
        // responding reaches it. An isolated test install can shorten it.
        private static int ConfigureTimeout()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestRootVariable))
                && int.TryParse(Environment.GetEnvironmentVariable("KILLERMCP_TEST_CONFIGURE_TIMEOUT_MS"), out int test) && test > 0) return test;
            return 150000;
        }

        // The configurator runs Codex, which has to stop with it.
        private static void KillTree(Process process)
        {
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true };
                using (Process? killer = Process.Start(start)) killer?.WaitForExit(10000);
            }
            catch (System.ComponentModel.Win32Exception) { }
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception exception) when (exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception) { }
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

        // allowMissing accepts a folder that lost some listed files to an interrupted removal.
        // Every file still present has to be listed and match its hash either way.
        private static Manifest ValidateInstalled(string destination, bool allowLegacy = false, bool allowMissing = false)
        {
            string manifestPath = Path.Combine(destination, "manifest.json");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            Manifest manifest = serializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("The installation manifest is invalid.");
            Dictionary<string, ManifestFile> files = manifest.files ?? throw new InvalidDataException("The installation manifest does not list any files.");
            bool native = files.ContainsKey("KillerMCP.exe") && files.ContainsKey("KillerMCP.Configure.exe");
            bool legacy = files.ContainsKey("node.exe") && files.ContainsKey("killermcp.mjs") && files.ContainsKey("killertools.mjs");
            if (!native && !(allowLegacy && legacy)) throw new InvalidDataException("The package does not contain a supported KillerMCP runtime.");
            string[] actual = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Select(path => path.Substring(destination.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/')).Where(name => name != "manifest.json").ToArray();
            if (actual.Any(name => !files.ContainsKey(name))) throw new InvalidOperationException("The installation folder contains files outside the KillerMCP manifest.");
            foreach (KeyValuePair<string, ManifestFile> item in files)
            {
                string path = Path.Combine(destination, item.Key.Replace('/', Path.DirectorySeparatorChar));
                if (allowMissing && !File.Exists(path)) continue;
                if (!File.Exists(path) || new FileInfo(path).Length != item.Value.bytes || !Sha256(path).Equals(item.Value.sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package verification failed for " + item.Key);
            }
            return manifest;
        }

        // The folder is renamed before anything is deleted. A file that is still in use makes the rename
        // fail with every file in place, and a removal that stops partway leaves only the renamed folder,
        // which the next install or uninstall finishes removing.
        private static void DeleteVerifiedInstallation(string destination, bool allowLegacy = false, bool allowMissing = false)
        {
            ValidateInstalled(destination, allowLegacy, allowMissing);
            string removing = destination + ".removing-" + Guid.NewGuid().ToString("N");
            Directory.Move(destination, removing);
            Manifest manifest;
            try { manifest = ValidateInstalled(removing, allowLegacy, allowMissing); }
            catch { Directory.Move(removing, destination); throw; }
            DeleteListedFiles(removing, manifest);
        }

        private static void DeleteListedFiles(string folder, Manifest manifest)
        {
            foreach (string name in manifest.files!.Keys)
            {
                string path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) File.Delete(path);
            }
            File.Delete(Path.Combine(folder, "manifest.json"));
            foreach (string directory in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(value => value.Length)) Directory.Delete(directory);
            Directory.Delete(folder);
        }

        // Finishes removals that an earlier setup started but could not complete, and removes backups an
        // earlier upgrade could not delete. A folder that holds anything KillerMCP did not install is left alone.
        private static void RemoveInterruptedRemovals(string destination)
        {
            string? parent = Path.GetDirectoryName(destination);
            if (parent == null || !Directory.Exists(parent)) return;
            string name = Path.GetFileName(destination);
            foreach (string folder in Directory.GetDirectories(parent, name + ".removing-*").Concat(Directory.GetDirectories(parent, name + ".previous-*")))
            {
                try
                {
                    if (!ContainsFiles(folder)) { Directory.Delete(folder, true); continue; }
                    DeleteListedFiles(folder, ValidateInstalled(folder, allowLegacy: true, allowMissing: true));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is InvalidOperationException) { }
            }
        }

        private static bool ContainsFiles(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();

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
                if (!File.Exists(setup))
                {
                    if (key == null || IsOrphanedEntry(destination, setup, key)) return;
                    throw new InvalidOperationException("The installed KillerMCP setup file is missing, and its uninstall entry names a different folder. Remove the entry under HKCU\\" + RegistryPath(destination) + " and run setup again.");
                }
                if (key != null && string.Equals(key.GetValue("SetupSha256") as string, Sha256(setup), StringComparison.OrdinalIgnoreCase)) return;
                if (!IsRecoverableSetup(destination, setup, key)) throw new InvalidOperationException(key == null ? "An unregistered KillerMCP setup file already exists." : "The installed KillerMCP setup file or uninstall entry has changed.");
            }
        }

        // The setup copy beside the folder can be deleted or quarantined while its uninstall entry stays. When
        // the entry is for this folder and the folder holds only KillerMCP files, setup installs again and
        // writes a new copy and entry instead of refusing.
        private static bool IsOrphanedEntry(string destination, string setup, RegistryKey key)
        {
            try
            {
                string? location = key.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location) || !Path.GetFullPath(location).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(key.GetValue("UninstallString") as string, Quote(setup) + " /uninstall", StringComparison.OrdinalIgnoreCase)) return false;
                if (Directory.Exists(destination) && ContainsFiles(destination)) ValidateInstalled(destination, allowLegacy: true, allowMissing: true);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException || exception is InvalidDataException || exception is InvalidOperationException)
            {
                return false;
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
                bool sameEntry = Path.GetFullPath(key.GetValue("InstallLocation") as string ?? string.Empty).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(key.GetValue("UninstallString") as string, Quote(setup) + " /uninstall", StringComparison.OrdinalIgnoreCase);
                if (!sameEntry
                    || !Version.TryParse(CurrentVersion, out Version? currentVersion)
                    || !Version.TryParse(information.FileVersion, out Version? setupVersion)
                    || setupVersion.Revision != 0) return false;
                if (Version.TryParse(key.GetValue("DisplayVersion") as string, out Version? registeredVersion)
                    && registeredVersion < currentVersion
                    && setupVersion.Major == currentVersion.Major
                    && setupVersion.Minor == currentVersion.Minor
                    && setupVersion.Build >= registeredVersion.Build
                    && setupVersion.Build <= currentVersion.Build
                    && (setupVersion.Build == currentVersion.Build || IsTrustedSetup(setup))) return true;
                return IsStaleRegistration(destination, setup, setupVersion, currentVersion);
            }
            catch { return false; }
        }

        // Two installs that overlapped before setup took its single-install lease could leave the
        // entry naming an older release than the verified files and setup copy beside it. When the
        // setup copy is the signed release of the installed host, only the entry is stale, so the
        // upgrade rewrites it instead of refusing.
        private static bool IsStaleRegistration(string destination, string setup, Version setupVersion, Version currentVersion)
        {
            string host = Path.Combine(destination, "KillerMCP.exe");
            var setupRelease = new Version(setupVersion.Major, setupVersion.Minor, setupVersion.Build);
            return File.Exists(host)
                && Version.TryParse(FileVersionInfo.GetVersionInfo(host).FileVersion, out Version? hostVersion)
                && hostVersion == setupVersion
                && setupRelease <= currentVersion
                && IsTrustedSetup(setup);
        }

        private static bool IsTrustedSetup(string setup) => KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(setup);

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
