using System.Text.Json.Nodes;
using KillerMCP.Clients;

var fakeCodexState = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_CODEX_STATE");
if (!string.IsNullOrWhiteSpace(fakeCodexState))
{
    var command = Environment.GetCommandLineArgs().Skip(1).ToArray();
    if (command.SequenceEqual(["mcp", "get", "killermcp", "--json"]))
    {
        if (!File.Exists(fakeCodexState)) { Console.Error.Write("No MCP server named killermcp"); return 1; }
        Console.Write(JsonNode.Parse(File.ReadAllText(fakeCodexState))!.ToJsonString());
        return 0;
    }
    if (command.Length >= 5 && command[..4].SequenceEqual(["mcp", "add", "killermcp", "--"]))
    {
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
    Console.WriteLine("PASS native KillerMCP client registration");
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
