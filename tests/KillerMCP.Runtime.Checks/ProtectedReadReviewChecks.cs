using KillerMCP;

internal static class ProtectedReadReviewChecks
{
    public static void Run()
    {
        var method = typeof(AgentPaths).GetMethod("ReadRefusal");
        if (method is null) throw new Exception("No settings read boundary exists.");
        foreach (var folder in new[] { KillerBenchSettings.Folder, KillerBenchSettings.BenchFolder })
        foreach (var path in new[] { folder, Path.Combine(folder, "config.json"), Path.Combine(folder, "..", Path.GetFileName(folder), "config.json") })
            if (method.Invoke(null, new object[] { path }) is null) throw new Exception("Settings read accepted: " + path);
        if (method.Invoke(null, new object[] { Path.Combine(Path.GetTempPath(), "ordinary.txt") }) is not null)
            throw new Exception("Ordinary read refused.");
        // Well-known credential stores are never readable, with no setting to allow them.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var path in new[]
        {
            Path.Combine(home, ".ssh", "id_ed25519"), Path.Combine(home, ".SSH"), Path.Combine(home, ".aws", "credentials"),
            Path.Combine(home, ".azure", "msal_token_cache.json"), Path.Combine(home, ".kube", "config"), Path.Combine(home, ".docker", "config.json"),
            Path.Combine(home, ".gnupg", "private-keys-v1.d"), Path.Combine(home, ".config", "gcloud", "credentials.db"), Path.Combine(home, ".config", "gh", "hosts.yml"),
            Path.Combine(home, ".git-credentials"), Path.Combine(home, "_netrc"), Path.Combine(home, ".npmrc"), Path.Combine(home, ".pypirc"),
            Path.Combine(home, ".claude", ".credentials.json"), Path.Combine(home, ".codex", "auth.json"), Path.Combine(home, ".gemini", "oauth_creds.json"),
            Path.Combine(roaming, "Microsoft", "Credentials", "x"), Path.Combine(local, "Microsoft", "Credentials", "x"), Path.Combine(roaming, "Microsoft", "Protect", "x"),
            Path.Combine(local, "Microsoft", "Vault", "x"), Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Login Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data", "Local State"), Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data", "x"),
            Path.Combine(roaming, "Mozilla", "Firefox", "Profiles", "a.default", "logins.json"), Path.Combine(roaming, "Opera Software", "Opera Stable", "Login Data"),
            Path.Combine(Path.GetTempPath(), "project", ".env"), Path.Combine(Path.GetTempPath(), "project", ".env.production"), Path.Combine(Path.GetTempPath(), "project", ".ENV.local"),
            // Agent and MCP client settings carry server tokens in their env blocks.
            Path.Combine(home, ".claude.json"), Path.Combine(roaming, "Claude", "claude_desktop_config.json"), Path.Combine(home, ".cursor", "mcp.json"),
            Path.Combine(home, ".codeium", "windsurf", "mcp_config.json"), Path.Combine(home, ".vault-token"),
            Path.Combine(home, ".terraform.d", "credentials.tfrc.json"), Path.Combine(roaming, "NuGet", "NuGet.Config"),
        })
            if (method.Invoke(null, new object[] { path }) is null) throw new Exception("Credential read accepted: " + path);
        foreach (var path in new[] { Path.Combine(Path.GetTempPath(), "project", ".env.example"), Path.Combine(Path.GetTempPath(), "project", "notes.env.txt"), Path.Combine(home, ".sshrc-notes.txt"), Path.Combine(home, "Documents", "aws.txt") })
            if (method.Invoke(null, new object[] { path }) is not null) throw new Exception("Ordinary read refused as a credential: " + path);
        if (!AgentPaths.IsCredentialLocation(Path.Combine(home, ".ssh", "known_hosts")) || AgentPaths.IsCredentialLocation(Path.Combine(home, "Documents", "x.txt")))
            throw new Exception("Search result filter does not match the credential list.");
        // A folder walk that would pass through a credential store is refused at its root, so listings such as
        // KillerBench's large files and duplicates never name or hash what is inside one.
        foreach (var root in new[] { roaming, Path.Combine(roaming, "Microsoft"), Path.Combine(home, ".config") })
            if (AgentPaths.RecursiveReadRefusal(root) is null) throw new Exception("Folder walk through a credential store accepted: " + root);
        if (AgentPaths.RecursiveReadRefusal(Path.GetTempPath()) is not null) throw new Exception("Ordinary folder walk refused.");
        Console.WriteLine("PASS: MCP and Bench settings and credential store reads blocked, ordinary reads allowed.");
    }
}
