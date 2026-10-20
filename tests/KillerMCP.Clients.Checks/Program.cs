using System.Text.Json.Nodes;
using KillerMCP.Clients;

var fakeCodexState = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_CODEX_STATE");
if (!string.IsNullOrWhiteSpace(fakeCodexState))
{
    var command = Environment.GetCommandLineArgs().Skip(1).ToArray();
    // Stands in for a Codex that stops responding, so setup's time limit on client configuration can be checked.
    if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_CODEX_HANG") == "1") Thread.Sleep(Timeout.Infinite);
    if (command.SequenceEqual(["mcp", "get", "killermcp", "--json"]))
    {
        if (!File.Exists(fakeCodexState)) { Console.Error.Write("No MCP server named killermcp"); return 1; }
        Console.Write(JsonNode.Parse(File.ReadAllText(fakeCodexState))!.ToJsonString());
        return 0;
    }
    if (command.Length >= 5 && command[..4].SequenceEqual(["mcp", "add", "killermcp", "--"]))
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_FAKE_CODEX_FAIL_ADD") == "1") { Console.Error.Write("Fixture add failure"); return 1; }
        File.WriteAllText(fakeCodexState, new JsonObject { ["transport"] = new JsonObject { ["type"] = "stdio", ["command"] = command[4], ["args"] = new JsonArray(command.Skip(5).Select(value => JsonValue.Create(value)).ToArray()) } }.ToJsonString());
        return 0;
    }
    if (command.SequenceEqual(["mcp", "remove", "killermcp"]))
    {
        File.Delete(fakeCodexState);
        return 0;
    }
    Console.Error.Write("Unknown fake Codex command");
    return 2;
}

var root = Path.Combine(Path.GetTempPath(), "killermcp-clients-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "KillerMCP.exe" : "KillerMCP");
    var configuration = Path.Combine(root, "client", "mcp.json");
    Directory.CreateDirectory(Path.GetDirectoryName(configuration)!);
    File.WriteAllText(configuration, """
        {
          "preservedSetting": "keep",
          "mcpServers": {
            "existing": {
              "command": "existing-command",
              "args": ["existing-argument"]
            }
          }
        }
        """);

    Equal("KillerMCP was added to Cursor.", ClientRegistration.RegisterJsonClient(configuration, "Cursor", executable));
    var saved = Read(configuration);
    Equal("keep", saved["preservedSetting"]!.GetValue<string>());
    Equal("existing-command", saved["mcpServers"]!["existing"]!["command"]!.GetValue<string>());
    Equal(executable, saved["mcpServers"]!["killermcp"]!["command"]!.GetValue<string>());
    Equal(0, saved["mcpServers"]!["killermcp"]!["args"]!.AsArray().Count);

    Equal("Cursor already has the correct KillerMCP connection.", ClientRegistration.RegisterJsonClient(configuration, "Cursor", executable));
    saved["mcpServers"]!["killermcp"] = new JsonObject { ["command"] = Path.Combine(root, OperatingSystem.IsWindows() ? "node.exe" : "node"), ["args"] = new JsonArray(Path.Combine(root, "killermcp.mjs")) };
    File.WriteAllText(configuration, saved.ToJsonString());
    Equal("KillerMCP was upgraded in Cursor.", ClientRegistration.RegisterJsonClient(configuration, "Cursor", executable));
    Equal(executable, Read(configuration)["mcpServers"]!["killermcp"]!["command"]!.GetValue<string>());
    saved = Read(configuration);
    saved["mcpServers"]!["killermcp"]!["command"] = Path.Combine(root, "different-host");
    File.WriteAllText(configuration, saved.ToJsonString());
    Throws<InvalidOperationException>(() => ClientRegistration.RegisterJsonClient(configuration, "Cursor", executable));
    Equal(Path.Combine(root, "different-host"), Read(configuration)["mcpServers"]!["killermcp"]!["command"]!.GetValue<string>());

    var claude = Path.Combine(root, "claude.json");
    ClientRegistration.RegisterJsonClient(claude, "Claude Code", executable, includeStdioType: true);
    Equal("stdio", Read(claude)["mcpServers"]!["killermcp"]!["type"]!.GetValue<string>());
    Equal(true, ClientRegistration.RemoveJsonClient(claude, "Claude Code", executable, includeStdioType: true));
    Equal(false, Read(claude)["mcpServers"]!.AsObject().ContainsKey("killermcp"));
    Equal(false, ClientRegistration.RemoveJsonClient(Path.Combine(root, "missing.json"), "Cursor", executable));

    var malformed = Path.Combine(root, "malformed.json");
    File.WriteAllText(malformed, "{");
    Throws<InvalidDataException>(() => ClientRegistration.RegisterJsonClient(malformed, "Cursor", executable));

    var packagedClaude = Path.Combine(root, "packaged-claude");
    var packagedConfiguration = Path.Combine(packagedClaude, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", "claude_desktop_config.json");
    Directory.CreateDirectory(Path.GetDirectoryName(packagedConfiguration)!);
    File.WriteAllText(packagedConfiguration, "{}");
    var packagedEnvironment = new Dictionary<string, string?>
    {
        ["PATH"] = string.Empty,
        ["USERPROFILE"] = root,
        ["APPDATA"] = Path.Combine(root, "uninitialized-roaming"),
        ["LOCALAPPDATA"] = packagedClaude,
        ["ProgramFiles"] = root
    };
    var packagedClient = ClientConfiguration.JsonClients(packagedEnvironment).Single(client => client.Name == "Claude Desktop");
    Equal(packagedConfiguration, packagedClient.Path);
    Directory.CreateDirectory(Path.Combine(root, "uninitialized-roaming", "Claude"));
    Equal(packagedConfiguration, ClientConfiguration.JsonClients(packagedEnvironment).Single(client => client.Name == "Claude Desktop").Path);
    File.Delete(packagedConfiguration);
    Equal(Path.Combine(root, "uninitialized-roaming", "Claude", "claude_desktop_config.json"), ClientConfiguration.JsonClients(packagedEnvironment).Single(client => client.Name == "Claude Desktop").Path);

    var clientNames = new[] { "CLAUDE", "CURSOR", "COPILOT", "GEMINI", "WINDSURF", "CLAUDE_DESKTOP" };
    var codexState = Path.Combine(root, "codex-state.json");
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_CODEX_STATE", codexState);
    var environment = new Dictionary<string, string?> { ["PATH"] = string.Empty, ["CODEX_CLI_PATH"] = Environment.ProcessPath, ["USERPROFILE"] = root, ["APPDATA"] = root, ["LOCALAPPDATA"] = root, ["ProgramFiles"] = root };
    var legacyCommand = Path.Combine(root, OperatingSystem.IsWindows() ? "node.exe" : "node");
    var legacyScript = Path.Combine(root, "killermcp.mjs");
    File.WriteAllText(codexState, new JsonObject { ["transport"] = new JsonObject { ["type"] = "stdio", ["command"] = legacyCommand, ["args"] = new JsonArray(legacyScript) } }.ToJsonString());
    foreach (var name in clientNames)
    {
        var path = Path.Combine(root, name, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var legacyRegistration = new JsonObject { ["command"] = legacyCommand, ["args"] = new JsonArray(legacyScript) };
        if (name == "CLAUDE") legacyRegistration["type"] = "stdio";
        File.WriteAllText(path, new JsonObject { ["preservedSetting"] = "keep", ["mcpServers"] = new JsonObject { ["killermcp"] = legacyRegistration } }.ToJsonString());
        environment[$"KILLERMCP_TEST_{name}_CONFIG"] = path;
    }
    var registered = ClientConfiguration.RegisterAll(executable, environment);
    Equal(7, registered.Count);
    Equal(executable, Read(codexState)["transport"]!["command"]!.GetValue<string>());
    foreach (var name in clientNames)
    {
        var path = environment[$"KILLERMCP_TEST_{name}_CONFIG"]!;
        Equal("keep", Read(path)["preservedSetting"]!.GetValue<string>());
        var serverName = name == "CLAUDE_DESKTOP" ? "KillerMCP" : "killermcp";
        Equal(executable, Read(path)["mcpServers"]![serverName]!["command"]!.GetValue<string>());
        if (name == "CLAUDE_DESKTOP") Equal(false, Read(path)["mcpServers"]!.AsObject().ContainsKey("killermcp"));
    }
    Equal(7, ClientConfiguration.RemoveAll(executable, environment).Count);
    Equal(false, File.Exists(codexState));
    foreach (var name in clientNames) Equal(false, Read(environment[$"KILLERMCP_TEST_{name}_CONFIG"]!)["mcpServers"]!.AsObject().ContainsKey(name == "CLAUDE_DESKTOP" ? "KillerMCP" : "killermcp"));

    // Registration is all or nothing. A conflict in any client leaves every client untouched, and a
    // write that fails partway puts back the clients already changed, so no client is left pointing
    // at a KillerMCP that setup then removes.
    string ConfigPath(string name) => environment[$"KILLERMCP_TEST_{name}_CONFIG"]!;
    foreach (var name in clientNames) File.WriteAllText(ConfigPath(name), new JsonObject { ["preservedSetting"] = "keep" }.ToJsonString());
    File.WriteAllText(ConfigPath("CURSOR"), new JsonObject { ["mcpServers"] = new JsonObject { ["killermcp"] = new JsonObject { ["command"] = Path.Combine(root, "different-host"), ["args"] = new JsonArray() } } }.ToJsonString());
    var untouched = clientNames.ToDictionary(name => name, name => File.ReadAllText(ConfigPath(name)));
    Throws<InvalidOperationException>(() => ClientConfiguration.RegisterAll(executable, environment));
    foreach (var name in clientNames) Equal(untouched[name], File.ReadAllText(ConfigPath(name)));
    Equal(false, File.Exists(codexState));
    File.WriteAllText(ConfigPath("CURSOR"), new JsonObject { ["preservedSetting"] = "keep" }.ToJsonString());
    var windsurf = ConfigPath("WINDSURF");
    File.Delete(windsurf);
    Directory.CreateDirectory(windsurf);
    untouched = clientNames.Where(name => name != "WINDSURF").ToDictionary(name => name, name => File.ReadAllText(ConfigPath(name)));
    Throws<Exception>(() => ClientConfiguration.RegisterAll(executable, environment));
    foreach (var name in untouched.Keys) Equal(untouched[name], File.ReadAllText(ConfigPath(name)));
    Equal(false, File.Exists(codexState));
    Directory.Delete(windsurf);
    File.WriteAllText(windsurf, "{}");

    // Uninstall removes KillerMCP from every client it can and reports the ones it could not change.
    ClientConfiguration.RegisterAll(executable, environment);
    File.WriteAllText(ConfigPath("GEMINI"), "{ not json");
    var removedWithWarning = ClientConfiguration.RemoveAll(executable, environment);
    Equal(1, removedWithWarning.Count(message => message.StartsWith("Warning: Gemini CLI", StringComparison.Ordinal)));
    foreach (var name in clientNames.Where(name => name != "GEMINI")) Equal(false, Read(ConfigPath(name))["mcpServers"]!.AsObject().ContainsKey(name == "CLAUDE_DESKTOP" ? "KillerMCP" : "killermcp"));
    Equal(false, File.Exists(codexState));
    Equal("{ not json", File.ReadAllText(ConfigPath("GEMINI")));

    // An empty settings file is an empty object and trailing commas are accepted. A file with comments
    // is never rewritten, because rewriting would drop them; setup names the exact entry to add instead.
    var empty = Path.Combine(root, "empty.json");
    File.WriteAllText(empty, "  \r\n");
    Equal("KillerMCP was added to Cursor.", ClientRegistration.RegisterJsonClient(empty, "Cursor", executable));
    Equal(executable, Read(empty)["mcpServers"]!["killermcp"]!["command"]!.GetValue<string>());
    var trailing = Path.Combine(root, "trailing.json");
    File.WriteAllText(trailing, "{ \"theme\": \"dark\", \"mcpServers\": { },\n}");
    Equal("KillerMCP was added to Cursor.", ClientRegistration.RegisterJsonClient(trailing, "Cursor", executable));
    Equal("dark", Read(trailing)["theme"]!.GetValue<string>());
    var commented = Path.Combine(root, "commented.json");
    var commentedText = "{\n  // keep this note\n  \"theme\": \"dark\"\n}";
    File.WriteAllText(commented, commentedText);
    var commentedError = Caught<CommentedSettingsException>(() => ClientRegistration.RegisterJsonClient(commented, "Gemini CLI", executable));
    Equal(true, commentedError.Message.Contains(JsonValue.Create(executable)!.ToJsonString(), StringComparison.Ordinal));
    Equal(commentedText, File.ReadAllText(commented));
    File.WriteAllText(ConfigPath("GEMINI"), commentedText);
    var withComments = ClientConfiguration.RegisterAll(executable, environment);
    Equal(1, withComments.Count(message => message.StartsWith("Warning: Gemini CLI", StringComparison.Ordinal)));
    Equal(commentedText, File.ReadAllText(ConfigPath("GEMINI")));
    Equal(executable, Read(ConfigPath("CURSOR"))["mcpServers"]!["killermcp"]!["command"]!.GetValue<string>());
    Equal(executable, Read(codexState)["transport"]!["command"]!.GetValue<string>());
    Equal(0, ClientConfiguration.RemoveAll(executable, environment).Count(message => message.StartsWith("Warning: Gemini CLI", StringComparison.Ordinal)));
    File.WriteAllText(ConfigPath("GEMINI"), "{\n  // keep this note\n  \"mcpServers\": { \"killermcp\": { \"command\": " + JsonValue.Create(executable)!.ToJsonString() + ", \"args\": [] } }\n}");
    Equal(1, ClientConfiguration.RemoveAll(executable, environment).Count(message => message.StartsWith("Warning: Gemini CLI", StringComparison.Ordinal)));
    Equal(true, File.ReadAllText(ConfigPath("GEMINI")).Contains("// keep this note", StringComparison.Ordinal));

    // Other apps' entries keep their characters instead of being rewritten as escape sequences.
    var characters = Path.Combine(root, "characters.json");
    File.WriteAllText(characters, "{ \"prompt\": \"<a & b> café + 'quote'\" }");
    ClientRegistration.RegisterJsonClient(characters, "Cursor", executable);
    Equal(true, File.ReadAllText(characters).Contains("<a & b> café + 'quote'", StringComparison.Ordinal));

    // A client that saves its settings while KillerMCP is changing them keeps its change.
    var busy = Path.Combine(root, "busy.json");
    File.WriteAllText(busy, "{ \"theme\": \"dark\" }");
    var saves = 0;
    ClientRegistration.BeforeReplace = path => { if (saves++ == 0) File.WriteAllText(path, "{ \"theme\": \"light\", \"recent\": [1] }"); };
    try { ClientRegistration.RegisterJsonClient(busy, "Claude Code", executable, includeStdioType: true); }
    finally { ClientRegistration.BeforeReplace = null; }
    Equal("light", Read(busy)["theme"]!.GetValue<string>());
    Equal(1, Read(busy)["recent"]!.AsArray().Count);
    Equal(executable, Read(busy)["mcpServers"]!["killermcp"]!["command"]!.GetValue<string>());

    // A settings file that is a link is changed where it points, and stays a link.
    var linkedTarget = Path.Combine(root, "dotfiles", "mcp.json");
    Directory.CreateDirectory(Path.GetDirectoryName(linkedTarget)!);
    File.WriteAllText(linkedTarget, "{}");
    var link = Path.Combine(root, "linked-mcp.json");
    var linked = true;
    try { File.CreateSymbolicLink(link, linkedTarget); }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { linked = false; Console.WriteLine("SKIP settings link check: this account cannot create symbolic links"); }
    if (linked)
    {
        ClientRegistration.RegisterJsonClient(link, "Cursor", executable);
        Equal(true, new FileInfo(link).LinkTarget is not null);
        Equal(executable, Read(linkedTarget)["mcpServers"]!["killermcp"]!["command"]!.GetValue<string>());
    }

    // Codex installed with npm puts codex.cmd on PATH and the program it starts in the package. That program
    // is used, and when it cannot be found setup says Codex was left unchanged instead of skipping it silently.
    var emptyProfile = Path.Combine(root, "empty-profile");
    Directory.CreateDirectory(emptyProfile);
    var npm = Path.Combine(root, "npm");
    // The package can carry a program for each Windows architecture; the one for this PC is used even
    // when the other is newer.
    var arm = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64;
    string Vendored(string triple) => Path.Combine(npm, "node_modules", "@openai", "codex", "vendor", triple, "codex", "codex.exe");
    var vendored = Vendored(arm ? "aarch64-pc-windows-msvc" : "x86_64-pc-windows-msvc");
    var otherArchitecture = Vendored(arm ? "x86_64-pc-windows-msvc" : "aarch64-pc-windows-msvc");
    Directory.CreateDirectory(Path.GetDirectoryName(vendored)!);
    Directory.CreateDirectory(Path.GetDirectoryName(otherArchitecture)!);
    File.WriteAllText(Path.Combine(npm, "codex.cmd"), "@echo off");
    File.WriteAllText(vendored, "fixture");
    File.WriteAllText(otherArchitecture, "fixture");
    File.SetLastWriteTimeUtc(otherArchitecture, File.GetLastWriteTimeUtc(vendored).AddHours(1));
    var npmEnvironment = new Dictionary<string, string?> { ["PATH"] = npm, ["USERPROFILE"] = emptyProfile, ["APPDATA"] = emptyProfile, ["LOCALAPPDATA"] = emptyProfile, ["ProgramFiles"] = emptyProfile };
    if (OperatingSystem.IsWindows())
    {
        Equal<string?>(vendored, ClientConfiguration.FindCodex(npmEnvironment));
        File.Delete(vendored);
        Equal<string?>(null, ClientConfiguration.FindCodex(npmEnvironment));
        File.Delete(otherArchitecture);
        Equal(1, ClientConfiguration.RegisterAll(executable, npmEnvironment).Count(message => message.StartsWith("Warning: Codex", StringComparison.Ordinal) && message.Contains("codex mcp add killermcp", StringComparison.Ordinal)));
        Equal(1, ClientConfiguration.RemoveAll(executable, npmEnvironment).Count(message => message.StartsWith("Warning: Codex", StringComparison.Ordinal)));
    }

    // A Codex program that cannot be started is an error setup can show, not a crash.
    var brokenCodex = Path.Combine(root, "broken-codex.exe");
    File.WriteAllText(brokenCodex, "not a program");
    var brokenEnvironment = new Dictionary<string, string?>(npmEnvironment) { ["PATH"] = string.Empty, ["CODEX_CLI_PATH"] = brokenCodex };
    Equal(true, Caught<InvalidOperationException>(() => ClientConfiguration.RegisterAll(executable, brokenEnvironment)).Message.Contains("Codex could not be started", StringComparison.Ordinal));

    // When Codex cannot take the new connection and the old one cannot be put back either, the error says so.
    File.WriteAllText(codexState, new JsonObject { ["transport"] = new JsonObject { ["type"] = "stdio", ["command"] = legacyCommand, ["args"] = new JsonArray(legacyScript) } }.ToJsonString());
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_CODEX_FAIL_ADD", "1");
    try
    {
        var codexOnly = new Dictionary<string, string?>(npmEnvironment) { ["PATH"] = string.Empty, ["CODEX_CLI_PATH"] = Environment.ProcessPath };
        var lost = Caught<InvalidOperationException>(() => ClientConfiguration.RegisterAll(executable, codexOnly));
        Equal(true, lost.Message.Contains("could not be restored", StringComparison.Ordinal) && lost.Message.Contains("codex mcp add killermcp", StringComparison.Ordinal));
    }
    finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_CODEX_FAIL_ADD", null); }
    Console.WriteLine("PASS native KillerMCP client registration");

    // Setup and the update window install an update only when it is signed by the KillerMCP publisher.
    // Another publisher's trusted signature is not enough.
    if (OperatingSystem.IsWindows())
    {
        var otherPublisher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (File.Exists(otherPublisher))
        {
            var otherSigner = KillerMCP.Signing.ReleaseSignature.SignedBy(otherPublisher);
            Equal(true, otherSigner is not null && otherSigner != KillerMCP.Signing.ReleaseSignature.Publisher);
            Equal(false, KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(otherPublisher));
        }
        var unsigned = Path.Combine(root, "unsigned-setup.exe");
        File.WriteAllText(unsigned, "not signed");
        Equal<string?>(null, KillerMCP.Signing.ReleaseSignature.SignedBy(unsigned));
        Equal(false, KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(unsigned));
        var released = Environment.GetEnvironmentVariable("KILLERMCP_SIGNED_SETUP");
        if (!string.IsNullOrWhiteSpace(released)) Equal(true, KillerMCP.Signing.ReleaseSignature.IsKillerMcpRelease(released));
        // The publisher's name alone is not enough: the certificate must also come from the publisher's
        // certificate authority.
        using (var key = System.Security.Cryptography.RSA.Create(2048))
        {
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=" + KillerMCP.Signing.ReleaseSignature.Publisher + ", O=Open Source Developer, C=US", key, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            using var lookalike = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            Equal(false, KillerMCP.Signing.ReleaseSignature.IsPublisherCertificate(lookalike));
        }
        Console.WriteLine("PASS update publisher check");
    }

    // Each update download is a new file in the temporary folder. Copies left by updates that already ran
    // are removed before the next download; other files are left alone.
    var downloads = Path.Combine(root, "downloads");
    Directory.CreateDirectory(downloads);
    var oldDownload = Path.Combine(downloads, "KillerMCP-Setup-0.6.0-old.exe");
    var recentDownload = Path.Combine(downloads, "KillerMCP-Setup-0.6.1-recent.exe");
    var otherFile = Path.Combine(downloads, "KillerMCP-Setup.exe");
    foreach (var file in new[] { oldDownload, recentDownload, otherFile }) File.WriteAllText(file, "fixture");
    File.SetLastWriteTimeUtc(oldDownload, DateTime.UtcNow.AddDays(-2));
    File.SetLastWriteTimeUtc(otherFile, DateTime.UtcNow.AddDays(-2));
    var nextDownload = KillerMCP.Updates.DownloadedSetups.NewPath(downloads, "0.7.0");
    Equal(false, File.Exists(oldDownload));
    Equal(true, File.Exists(recentDownload) && File.Exists(otherFile));
    Equal(downloads, Path.GetDirectoryName(nextDownload));
    Equal(true, Path.GetFileName(nextDownload).StartsWith("KillerMCP-Setup-0.7.0-", StringComparison.Ordinal));
    Console.WriteLine("PASS downloaded update cleanup");

    // The update window keeps the other keys in the preferences file it shares with setup and replaces the
    // file whole; a file it cannot read is treated as the default choice.
    var preferences = Path.Combine(root, "preferences", "update-preferences.json");
    Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
    File.WriteAllText(preferences, "{ \"Behavior\": \"check\", \"Other\": \"keep\" }");
    KillerMCP.Notify.BehaviorPreference.Write(preferences, "download");
    Equal("keep", Read(preferences)["Other"]!.GetValue<string>());
    Equal("download", KillerMCP.Notify.BehaviorPreference.Read(preferences));
    Equal(1, Directory.GetFiles(Path.GetDirectoryName(preferences)!).Length);
    File.WriteAllText(preferences, "[]");
    Equal("check", KillerMCP.Notify.BehaviorPreference.Read(preferences));
    KillerMCP.Notify.BehaviorPreference.Write(preferences, "off");
    Equal("off", KillerMCP.Notify.BehaviorPreference.Read(preferences));
    Console.WriteLine("PASS update preference file");
}
finally
{
    Environment.SetEnvironmentVariable("KILLERMCP_FAKE_CODEX_STATE", null);
    Directory.Delete(root, true);
}
return 0;

static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }
}

static T Caught<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
