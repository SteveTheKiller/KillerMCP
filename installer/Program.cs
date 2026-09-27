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
using System.Windows.Forms;
using Microsoft.Win32;

namespace KillerMCP.Setup
{
    internal static class Program
    {
        private const string PayloadName = "KillerMCP.payload.zip";
        private const string TestRootVariable = "KILLERMCP_TEST_INSTALL_ROOT";
        private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\KillerMCP";

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
            bool isolatedTest = !string.IsNullOrWhiteSpace(testRoot);
            string destination = isolatedTest
                ? ValidateTestRoot(testRoot!)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KillerMCP");
            bool connectCodex = !isolatedTest ||
                string.Equals(Environment.GetEnvironmentVariable("KILLERMCP_TEST_REGISTER_CODEX"), "1", StringComparison.Ordinal);
            bool connectClaude = !isolatedTest ||
                string.Equals(Environment.GetEnvironmentVariable("KILLERMCP_TEST_REGISTER_CLAUDE"), "1", StringComparison.Ordinal);
            string? cursorConfiguration = CursorConfigurationPath(isolatedTest);
            string? copilotConfiguration = CopilotConfigurationPath(isolatedTest);
            string? geminiConfiguration = GeminiConfigurationPath(isolatedTest);
            string? windsurfConfiguration = WindsurfConfigurationPath(isolatedTest);
            string? claudeDesktopConfiguration = ClaudeDesktopConfigurationPath(isolatedTest);
            if (isolatedTest && connectCodex)
            {
                string? codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (string.IsNullOrWhiteSpace(codexHome))
                    throw new InvalidOperationException("An isolated Codex home is required for the registration test.");
                ValidateTestRoot(codexHome);
            }
            if (isolatedTest && connectClaude)
            {
                string? claudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrWhiteSpace(claudeHome))
                    throw new InvalidOperationException("An isolated Claude configuration is required for the registration test.");
                ValidateTestRoot(claudeHome);
            }

            bool silent = args.Any(arg => string.Equals(arg, "/silent", StringComparison.OrdinalIgnoreCase));
            bool uninstall = args.Any(arg => string.Equals(arg, "/uninstall", StringComparison.OrdinalIgnoreCase));
            if (uninstall && !silent && MessageBox.Show(
                "Remove KillerMCP and its agent connections?", "KillerMCP Setup",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 0;
            if (silent || uninstall)
            {
                try
                {
                    if (uninstall)
                    {
                        ValidateSetupRegistration(destination);
                        Uninstall(destination, connectCodex, connectClaude, cursorConfiguration,
                            copilotConfiguration, geminiConfiguration, windsurfConfiguration,
                            claudeDesktopConfiguration);
                        RemoveInstalledApp(destination);
                    }
                    else
                    {
                        Install(destination);
                        RegisterInstalledApp(destination);
                        if (connectCodex) RegisterCodex(destination);
                        if (connectClaude) RegisterClaudeCode(destination);
                        if (cursorConfiguration != null) RegisterJsonClient(destination, cursorConfiguration, "Cursor");
                        if (copilotConfiguration != null) RegisterJsonClient(destination, copilotConfiguration, "GitHub Copilot");
                        if (geminiConfiguration != null) RegisterJsonClient(destination, geminiConfiguration, "Gemini CLI");
                        if (windsurfConfiguration != null) RegisterJsonClient(destination, windsurfConfiguration, "Windsurf");
                        if (claudeDesktopConfiguration != null) RegisterJsonClient(destination, claudeDesktopConfiguration, "Claude Desktop");
                    }
                    return 0;
                }
                catch (Exception error)
                {
                    if (silent) Console.Error.WriteLine("KillerMCP setup failed: " + error.Message);
                    else MessageBox.Show(error.Message, "KillerMCP Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }

            return InstallerWizard.Run(destination, () =>
            {
                Install(destination);
                RegisterInstalledApp(destination);
                if (connectCodex) RegisterCodex(destination);
                if (connectClaude) RegisterClaudeCode(destination);
                if (cursorConfiguration != null) RegisterJsonClient(destination, cursorConfiguration, "Cursor");
                if (copilotConfiguration != null) RegisterJsonClient(destination, copilotConfiguration, "GitHub Copilot");
                if (geminiConfiguration != null) RegisterJsonClient(destination, geminiConfiguration, "Gemini CLI");
                if (windsurfConfiguration != null) RegisterJsonClient(destination, windsurfConfiguration, "Windsurf");
                if (claudeDesktopConfiguration != null) RegisterJsonClient(destination, claudeDesktopConfiguration, "Claude Desktop");
            });
        }

        private static string ValidateTestRoot(string value)
        {
            string root = Path.GetFullPath(value);
            string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), temporary.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The test install directory must be inside the temporary directory.");
            return root;
        }

        internal static string Install(string destination)
        {
            ValidateSetupRegistration(destination);
            string parent = Path.GetDirectoryName(destination)
                ?? throw new InvalidOperationException("The installation directory is unavailable.");
            Directory.CreateDirectory(parent);
            string staging = destination + ".staging-" + Guid.NewGuid().ToString("N");
            string backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
            bool movedPrevious = false;
            try
            {
                if (Directory.Exists(destination)) ValidateInstalledEntries(destination, out _, out _);
                ExtractAndVerify(staging);
                if (Directory.Exists(destination))
                {
                    Directory.Move(destination, backup);
                    movedPrevious = true;
                }
                try { Directory.Move(staging, destination); }
                catch
                {
                    if (movedPrevious) Directory.Move(backup, destination);
                    throw;
                }
                if (movedPrevious) Uninstall(backup, false, false, null, null, null, null, null);
                return destination;
            }
            finally
            {
                if (Directory.Exists(staging)) DeleteInstallDirectory(staging, parent);
            }
        }

        private static void ExtractAndVerify(string staging)
        {
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName)
                ?? throw new InvalidOperationException("The installer payload is missing."))
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                Directory.CreateDirectory(staging);
                string root = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string target = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The package contains an unsafe path.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? staging);
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
            }

            string manifestPath = Path.Combine(staging, "manifest.json");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            Manifest manifest = serializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("The package manifest is invalid.");
            if (manifest.files == null || manifest.files.Count == 0 ||
                !manifest.files.ContainsKey("node.exe") || !manifest.files.ContainsKey("killermcp.mjs"))
                throw new InvalidDataException("The package does not contain a complete runtime.");

            string[] actual = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                .Select(path => path.Substring(staging.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'))
                .Where(name => name != "manifest.json")
                .ToArray();
            if (actual.Length != manifest.files.Count || actual.Any(name => !manifest.files.ContainsKey(name)))
                throw new InvalidDataException("The package file list differs from its manifest.");
            foreach (KeyValuePair<string, ManifestFile> item in manifest.files)
            {
                string path = Path.Combine(staging, item.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path) || new FileInfo(path).Length != item.Value.bytes ||
                    !string.Equals(Sha256(path), item.Value.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Package verification failed for " + item.Key);
            }
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static string SetupCopyPath(string destination) => destination + "-Setup.exe";

        private static string InstalledAppRegistryPath(string destination)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestRootVariable)))
                return UninstallRegistryPath;
            return @"Software\KillerMCP\InstallerTests\" + Path.GetFileName(Path.GetDirectoryName(destination));
        }

        private static void ValidateSetupRegistration(string destination)
        {
            string setup = SetupCopyPath(destination);
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(InstalledAppRegistryPath(destination)))
            {
                if (key == null)
                {
                    if (File.Exists(setup))
                        throw new InvalidOperationException("An unregistered KillerMCP setup file already exists.");
                    return;
                }
                if (!File.Exists(setup) ||
                    !string.Equals(key.GetValue("UninstallString") as string, Quote(setup) + " /uninstall", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(key.GetValue("SetupSha256") as string, Sha256(setup), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The installed KillerMCP setup file or uninstall entry has changed.");
            }
        }

        private static void RegisterInstalledApp(string destination)
        {
            string setup = SetupCopyPath(destination);
            string source = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(setup), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, setup, true);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(InstalledAppRegistryPath(destination))
                ?? throw new InvalidOperationException("Could not create the KillerMCP uninstall entry."))
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
        }

        private static void RemoveInstalledApp(string destination)
        {
            string setup = SetupCopyPath(destination);
            if (File.Exists(setup))
            {
                if (string.Equals(Path.GetFullPath(Assembly.GetExecutingAssembly().Location),
                    Path.GetFullPath(setup), StringComparison.OrdinalIgnoreCase))
                {
                    string script = "Wait-Process -Id " + Process.GetCurrentProcess().Id +
                        " -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '" +
                        setup.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue";
                    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                    using (Process? helper = Process.Start(new ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
                    { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        if (helper == null) throw new InvalidOperationException("Could not remove the setup file after uninstall.");
                    }
                }
                else File.Delete(setup);
            }
            Registry.CurrentUser.DeleteSubKeyTree(InstalledAppRegistryPath(destination), false);
        }

        private static void DeleteInstallDirectory(string path, string parent)
        {
            string allowed = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(path);
            if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing to remove a directory outside the installation parent.");
            Directory.Delete(target, recursive: true);
        }

        internal static void Uninstall(string destination, bool disconnectCodex, bool disconnectClaude,
            string? cursorConfiguration, string? copilotConfiguration, string? geminiConfiguration,
            string? windsurfConfiguration, string? claudeDesktopConfiguration)
        {
            if (!Directory.Exists(destination)) return;
            ValidateInstalledEntries(destination, out List<string> files, out List<string> directories);
            if (disconnectCodex) RemoveCodexRegistration(destination);
            if (disconnectClaude) RemoveClaudeRegistration(destination);
            if (cursorConfiguration != null) RemoveJsonClientRegistration(destination, cursorConfiguration, "Cursor");
            if (copilotConfiguration != null) RemoveJsonClientRegistration(destination, copilotConfiguration, "GitHub Copilot");
            if (geminiConfiguration != null) RemoveJsonClientRegistration(destination, geminiConfiguration, "Gemini CLI");
            if (windsurfConfiguration != null) RemoveJsonClientRegistration(destination, windsurfConfiguration, "Windsurf");
            if (claudeDesktopConfiguration != null) RemoveJsonClientRegistration(destination, claudeDesktopConfiguration, "Claude Desktop");
            foreach (string file in files) File.Delete(file);
            foreach (string directory in directories.OrderByDescending(path => path.Length)) Directory.Delete(directory);
            Directory.Delete(destination);
        }

        private static void ValidateInstalledEntries(string destination, out List<string> files, out List<string> directories)
        {
            string manifestPath = Path.Combine(destination, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("The folder does not contain a KillerMCP installation manifest.");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            Manifest manifest = serializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidDataException("The installation manifest is invalid.");
            if (manifest.files == null || !manifest.files.ContainsKey("node.exe") ||
                !manifest.files.ContainsKey("killermcp.mjs"))
                throw new InvalidDataException("The installation manifest does not identify KillerMCP.");
            files = new List<string>();
            directories = new List<string>();
            CollectInstalledEntries(destination, files, directories);
            string[] actual = files
                .Select(path => path.Substring(destination.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'))
                .Where(name => name != "manifest.json")
                .ToArray();
            if (actual.Length != manifest.files.Count || actual.Any(name => !manifest.files.ContainsKey(name)))
                throw new InvalidOperationException("The installation folder contains files outside the KillerMCP manifest.");
            var expectedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in manifest.files.Keys)
            {
                string? parent = Path.GetDirectoryName(name.Replace('/', Path.DirectorySeparatorChar));
                while (!string.IsNullOrEmpty(parent))
                {
                    expectedDirectories.Add(parent.Replace('\\', '/'));
                    parent = Path.GetDirectoryName(parent);
                }
            }
            if (directories.Any(path => !expectedDirectories.Contains(
                path.Substring(destination.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'))))
                throw new InvalidOperationException("The installation folder contains directories outside the KillerMCP manifest.");
            foreach (KeyValuePair<string, ManifestFile> item in manifest.files)
            {
                string path = Path.Combine(destination, item.Key.Replace('/', Path.DirectorySeparatorChar));
                if (item.Value == null || !File.Exists(path) || new FileInfo(path).Length != item.Value.bytes ||
                    !string.Equals(Sha256(path), item.Value.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The installation contains a modified runtime file: " + item.Key);
            }
        }

        private static void CollectInstalledEntries(string directory, List<string> files, List<string> directories)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The installation folder contains a linked directory.");
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("The installation folder contains a linked file or directory.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    CollectInstalledEntries(entry, files, directories);
                    directories.Add(entry);
                }
                else files.Add(entry);
            }
        }

        private static void RemoveCodexRegistration(string destination)
        {
            string? codex = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (codex == null || !File.Exists(codex)) codex = FindOnPath("codex.exe");
            if (codex == null) return;
            int existing = Run(codex, "mcp get killermcp --json", out string configuration, out string error);
            if (existing != 0)
            {
                if (error.IndexOf("No MCP server named", StringComparison.OrdinalIgnoreCase) >= 0) return;
                throw new InvalidOperationException("Codex could not read its MCP configuration: " + error.Trim());
            }
            if (!MatchesCodexRegistration(configuration, Path.Combine(destination, "node.exe"),
                Path.Combine(destination, "killermcp.mjs")))
                throw new InvalidOperationException("Codex has a different killermcp connection. It was left unchanged.");
            if (Run(codex, "mcp remove killermcp", out _, out error) != 0)
                throw new InvalidOperationException("Codex could not remove its KillerMCP connection: " + error.Trim());
        }

        private static string ClaudeConfigurationPath()
        {
            string? custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            string directory = string.IsNullOrWhiteSpace(custom)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : custom;
            return Path.Combine(directory, ".claude.json");
        }

        private static Dictionary<string, object>? ClaudeServerConfiguration()
        {
            string path = ClaudeConfigurationPath();
            if (!File.Exists(path)) return null;
            var root = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }
                .DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>
                ?? throw new InvalidDataException("Claude Code configuration is invalid.");
            if (!root.TryGetValue("mcpServers", out object? serversValue)) return null;
            var servers = serversValue as Dictionary<string, object>
                ?? throw new InvalidDataException("Claude Code MCP configuration is invalid.");
            if (!servers.TryGetValue("killermcp", out object? value)) return null;
            return value as Dictionary<string, object>
                ?? throw new InvalidDataException("The Claude Code killermcp entry is invalid.");
        }

        private static bool MatchesClaudeRegistration(Dictionary<string, object> configuration, string destination)
        {
            if (!configuration.TryGetValue("type", out object? type) ||
                !configuration.TryGetValue("command", out object? command)) return false;
            var args = configuration.TryGetValue("args", out object? value) ? value as object[] : null;
            return string.Equals(type as string, "stdio", StringComparison.Ordinal) &&
                string.Equals(command as string, Path.Combine(destination, "node.exe"), StringComparison.OrdinalIgnoreCase) &&
                args?.Length == 1 && string.Equals(args[0] as string,
                    Path.Combine(destination, "killermcp.mjs"), StringComparison.OrdinalIgnoreCase);
        }

        private static void RemoveClaudeRegistration(string destination)
        {
            Dictionary<string, object>? existing = ClaudeServerConfiguration();
            if (existing == null) return;
            if (!MatchesClaudeRegistration(existing, destination))
                throw new InvalidOperationException("Claude Code has a different killermcp connection. It was left unchanged.");
            string? claude = Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH");
            if (claude == null || !File.Exists(claude)) claude = FindOnPath("claude.exe");
            if (claude == null)
                throw new InvalidOperationException("Claude Code is needed to remove its KillerMCP connection.");
            if (Run(claude, "mcp remove --scope user killermcp", out _, out string error) != 0 ||
                ClaudeServerConfiguration() != null)
                throw new InvalidOperationException("Claude Code could not remove its KillerMCP connection: " + error.Trim());
        }

        internal static string RegisterClaudeCode(string destination)
        {
            string? claude = Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH");
            if (claude == null || !File.Exists(claude)) claude = FindOnPath("claude.exe");
            if (claude == null) return "Claude Code was not found on this computer.";
            Dictionary<string, object>? existing = ClaudeServerConfiguration();
            if (existing != null)
            {
                if (!MatchesClaudeRegistration(existing, destination))
                    throw new InvalidOperationException("Claude Code already has a different killermcp connection. Its settings were left unchanged.");
                return "Claude Code already has the correct KillerMCP connection.";
            }
            string command = "mcp add --scope user --transport stdio killermcp -- " +
                Quote(Path.Combine(destination, "node.exe")) + " " + Quote(Path.Combine(destination, "killermcp.mjs"));
            if (Run(claude, command, out _, out string error) != 0 ||
                !MatchesClaudeRegistration(ClaudeServerConfiguration()
                    ?? throw new InvalidOperationException("Claude Code did not save the KillerMCP connection."), destination))
                throw new InvalidOperationException("Claude Code could not add the KillerMCP connection: " + error.Trim());
            return "KillerMCP was added to Claude Code.";
        }

        private static string? CursorConfigurationPath(bool isolatedTest)
        {
            string? testPath = Environment.GetEnvironmentVariable("KILLERMCP_TEST_CURSOR_CONFIG");
            if (!string.IsNullOrWhiteSpace(testPath))
            {
                string path = Path.GetFullPath(testPath);
                ValidateTestRoot(Path.GetDirectoryName(path)
                    ?? throw new InvalidOperationException("The isolated Cursor configuration path is invalid."));
                return path;
            }
            if (isolatedTest) return null;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configurationDirectory = Path.Combine(profile, ".cursor");
            string localPrograms = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            bool installed = FindOnPath("cursor.exe") != null || Directory.Exists(configurationDirectory) ||
                File.Exists(Path.Combine(localPrograms, "Programs", "Cursor", "Cursor.exe")) ||
                File.Exists(Path.Combine(programFiles, "Cursor", "Cursor.exe"));
            return installed ? Path.Combine(configurationDirectory, "mcp.json") : null;
        }

        private static string? CopilotConfigurationPath(bool isolatedTest)
        {
            string? testPath = Environment.GetEnvironmentVariable("KILLERMCP_TEST_COPILOT_CONFIG");
            if (!string.IsNullOrWhiteSpace(testPath)) return ValidateTestConfigurationPath(testPath, "GitHub Copilot");
            if (isolatedTest) return null;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configurationDirectory = Path.Combine(profile, ".copilot");
            bool installed = FindOnPath("copilot.exe") != null || FindOnPath("code.exe") != null ||
                Directory.Exists(configurationDirectory);
            return installed ? Path.Combine(configurationDirectory, "mcp-config.json") : null;
        }

        private static string? GeminiConfigurationPath(bool isolatedTest)
        {
            string? testPath = Environment.GetEnvironmentVariable("KILLERMCP_TEST_GEMINI_CONFIG");
            if (!string.IsNullOrWhiteSpace(testPath)) return ValidateTestConfigurationPath(testPath, "Gemini CLI");
            if (isolatedTest) return null;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configurationDirectory = Path.Combine(profile, ".gemini");
            bool installed = FindOnPath("gemini.exe") != null || Directory.Exists(configurationDirectory);
            return installed ? Path.Combine(configurationDirectory, "settings.json") : null;
        }

        private static string? WindsurfConfigurationPath(bool isolatedTest)
        {
            string? testPath = Environment.GetEnvironmentVariable("KILLERMCP_TEST_WINDSURF_CONFIG");
            if (!string.IsNullOrWhiteSpace(testPath)) return ValidateTestConfigurationPath(testPath, "Windsurf");
            if (isolatedTest) return null;

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configurationDirectory = Path.Combine(profile, ".codeium", "windsurf");
            string localPrograms = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            bool installed = FindOnPath("windsurf.exe") != null || Directory.Exists(configurationDirectory) ||
                File.Exists(Path.Combine(localPrograms, "Programs", "Windsurf", "Windsurf.exe"));
            return installed ? Path.Combine(configurationDirectory, "mcp_config.json") : null;
        }

        private static string? ClaudeDesktopConfigurationPath(bool isolatedTest)
        {
            string? testPath = Environment.GetEnvironmentVariable("KILLERMCP_TEST_CLAUDE_DESKTOP_CONFIG");
            if (!string.IsNullOrWhiteSpace(testPath)) return ValidateTestConfigurationPath(testPath, "Claude Desktop");
            if (isolatedTest) return null;

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configurationDirectory = Path.Combine(roaming, "Claude");
            string localPrograms = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            bool installed = Directory.Exists(configurationDirectory) ||
                File.Exists(Path.Combine(localPrograms, "Programs", "Claude", "Claude.exe")) ||
                File.Exists(Path.Combine(localPrograms, "AnthropicClaude", "claude.exe")) ||
                File.Exists(Path.Combine(programFiles, "Claude", "Claude.exe"));
            return installed ? Path.Combine(configurationDirectory, "claude_desktop_config.json") : null;
        }

        private static string ValidateTestConfigurationPath(string value, string clientName)
        {
            string path = Path.GetFullPath(value);
            ValidateTestRoot(Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The isolated " + clientName + " configuration path is invalid."));
            return path;
        }

        private static Dictionary<string, object> ReadJsonClientConfiguration(string path,
            string clientName, out Dictionary<string, object> servers)
        {
            Dictionary<string, object> root;
            if (File.Exists(path))
            {
                root = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }
                    .DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>
                    ?? throw new InvalidDataException(clientName + " MCP configuration is invalid.");
            }
            else root = new Dictionary<string, object>();

            if (root.TryGetValue("mcpServers", out object? value))
            {
                servers = value as Dictionary<string, object>
                    ?? throw new InvalidDataException(clientName + " MCP server configuration is invalid.");
            }
            else
            {
                servers = new Dictionary<string, object>();
                root["mcpServers"] = servers;
            }
            return root;
        }

        private static bool MatchesJsonClientRegistration(object value, string destination)
        {
            var configuration = value as Dictionary<string, object>;
            if (configuration == null || !configuration.TryGetValue("command", out object? command)) return false;
            var args = configuration.TryGetValue("args", out object? argsValue) ? argsValue as object[] : null;
            return string.Equals(command as string, Path.Combine(destination, "node.exe"), StringComparison.OrdinalIgnoreCase) &&
                args?.Length == 1 && string.Equals(args[0] as string,
                    Path.Combine(destination, "killermcp.mjs"), StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteJsonConfiguration(string path, Dictionary<string, object> root)
        {
            string directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The MCP configuration directory is unavailable.");
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                string json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(root);
                File.WriteAllText(temporary, json + Environment.NewLine, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void RemoveJsonClientRegistration(string destination, string path, string clientName)
        {
            if (!File.Exists(path)) return;
            Dictionary<string, object> root = ReadJsonClientConfiguration(path, clientName,
                out Dictionary<string, object> servers);
            if (!servers.TryGetValue("killermcp", out object? existing)) return;
            if (!MatchesJsonClientRegistration(existing, destination))
                throw new InvalidOperationException(clientName + " has a different killermcp connection. It was left unchanged.");
            servers.Remove("killermcp");
            WriteJsonConfiguration(path, root);
        }

        internal static string RegisterJsonClient(string destination, string path, string clientName)
        {
            Dictionary<string, object> root = ReadJsonClientConfiguration(path, clientName,
                out Dictionary<string, object> servers);
            if (servers.TryGetValue("killermcp", out object? existing))
            {
                if (!MatchesJsonClientRegistration(existing, destination))
                    throw new InvalidOperationException(clientName + " already has a different killermcp connection. Its settings were left unchanged.");
                return clientName + " already has the correct KillerMCP connection.";
            }
            servers["killermcp"] = new Dictionary<string, object>
            {
                ["command"] = Path.Combine(destination, "node.exe"),
                ["args"] = new object[] { Path.Combine(destination, "killermcp.mjs") },
            };
            WriteJsonConfiguration(path, root);
            return "KillerMCP was added to " + clientName + ".";
        }

        internal static string RegisterCodex(string destination)
        {
            string? codex = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (codex == null || !File.Exists(codex)) codex = FindOnPath("codex.exe");
            if (codex == null) return "Runtime installed. Codex was not found on this computer.";
            string node = Path.Combine(destination, "node.exe");
            string server = Path.Combine(destination, "killermcp.mjs");
            int existing = Run(codex, "mcp get killermcp --json", out string configuration, out string error);
            if (existing == 0)
            {
                if (!MatchesCodexRegistration(configuration, node, server))
                    throw new InvalidOperationException("Codex already has a different killermcp connection. Its settings were left unchanged.");
                return "Runtime installed. Codex already has the correct KillerMCP connection.";
            }
            if (error.IndexOf("No MCP server named", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Codex could not read its MCP configuration: " + error.Trim());
            int added = Run(codex, "mcp add killermcp -- " + Quote(node) + " " + Quote(server), out _, out error);
            if (added != 0) throw new InvalidOperationException("Codex could not add the KillerMCP connection.");
            if (Run(codex, "mcp get killermcp --json", out configuration, out _) != 0 ||
                !MatchesCodexRegistration(configuration, node, server))
                throw new InvalidOperationException("Codex did not save the expected KillerMCP connection.");
            return "Runtime installed and added to Codex. Open a new chat to use the tools.";
        }

        private static bool MatchesCodexRegistration(string json, string node, string server)
        {
            try
            {
                var config = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                var transport = config?["transport"] as Dictionary<string, object>;
                var args = transport?["args"] as object[];
                return string.Equals(transport?["type"] as string, "stdio", StringComparison.Ordinal) &&
                    string.Equals(transport?["command"] as string, node, StringComparison.OrdinalIgnoreCase) &&
                    args?.Length == 1 && string.Equals(args[0] as string, server, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static string? FindOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                try
                {
                    string path = Path.Combine(directory.Trim('"'), executable);
                    if (File.Exists(path)) return Path.GetFullPath(path);
                }
                catch { }
            }
            return null;
        }

        private static int Run(string executable, string arguments, out string output, out string error)
        {
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }))
            {
                if (process == null) { output = string.Empty; error = "Process did not start."; return 1; }
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private sealed class Manifest
        {
            public Dictionary<string, ManifestFile>? files { get; set; }
        }

        private sealed class ManifestFile
        {
            public long bytes { get; set; }
            public string? sha256 { get; set; }
        }

    }
}
