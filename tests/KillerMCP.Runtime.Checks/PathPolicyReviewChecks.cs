using KillerMCP;

internal static class PathPolicyReviewChecks
{
    public static void Run()
    {
        var failures = new List<string>();
        foreach (var path in new[] { "Z:file", @"\x\y", @"C:..\x", @"C:\file.pdf:stream", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP:stream") })
            if (AgentPaths.AccessRefusal(path) is null) failures.Add("Accepted non-file path: " + path);
        foreach (var path in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "probe.txt"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "probe.txt"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "probe.txt"),
        })
            if (AgentPaths.WriteRefusal(path) is null) failures.Add("Accepted executable startup/install destination: " + path);
        if (AgentPaths.AccessRefusal(@"\\not-allowlisted.invalid\share\file.txt") is null) failures.Add("Accepted unlisted server name.");
        if (AgentPaths.AccessRefusal(Path.Combine(Path.GetTempPath(), "ordinary.txt")) is not null) failures.Add("Ordinary absolute path refused.");

        // A share on this PC reaches the local disk by another name, so it must not walk around the folder guards.
        var own = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(item => item.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(item => item.GetIPProperties().UnicastAddresses).Select(item => item.Address)
            .FirstOrDefault(address => AgentPaths.IsLocalNetwork(address) && !System.Net.IPAddress.IsLoopback(address));
        var hosts = new List<string> { "127.0.0.1", "127.1", "127.0.0.2", "[::1]", "localhost", Environment.MachineName };
        if (own is not null) hosts.Add(own.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + own + "]" : own.ToString());
        foreach (var local in new[]
        {
            KillerBenchSettings.Folder, KillerBenchSettings.BenchFolder,
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        })
        foreach (var host in hosts)
        foreach (var prefix in new[] { @"\\", @"\\?\UNC\" })
        {
            var unc = prefix + host + @"\" + char.ToUpperInvariant(local[0]) + @"$" + local[2..] + @"\probe.txt";
            if (AgentPaths.ReadRefusal(unc) is null) failures.Add("Read accepted a path back to this PC: " + unc);
            if (AgentPaths.WriteRefusal(unc) is null) failures.Add("Write accepted a path back to this PC: " + unc);
        }

        // A path component the account cannot inspect is refused rather than assumed safe.
        if (OperatingSystem.IsWindows()) Unreadable(failures);

        // App processes start in their own empty folder, never the client's working folder.
        var run = ProcessRunner.RunAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "cd"], TimeSpan.FromSeconds(20), 4096).GetAwaiter().GetResult();
        var folder = run.StandardOutput.Trim();
        if (string.Equals(folder.TrimEnd('\\'), Environment.CurrentDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(folder).StartsWith("KillerMCP-run-", StringComparison.Ordinal))
            failures.Add("An app process started in the client's working folder: " + folder);
        else if (Directory.Exists(folder)) failures.Add("An app working folder was left behind: " + folder);
        // Servers KillerMCP contacts for the user must be public names, never this PC or a private network.
        foreach (var host in new[] { "127.0.0.1", "169.254.169.254", "10.0.0.5", "[::1]", "localhost", "tsa.local", "intranet", Environment.MachineName, "8.8.8.8" })
            if (AgentPaths.IsPublicHostAsync(host, CancellationToken.None).GetAwaiter().GetResult()) failures.Add("Public host check accepted " + host);
        foreach (var host in new[] { "8.8.8.8", "203.0.113.7", "1.1.1.1" })
            if (AgentPaths.ResolvesOnlyLocalAsync(host, CancellationToken.None).GetAwaiter().GetResult()) failures.Add("Local host check accepted " + host);
        foreach (var host in new[] { "127.0.0.1", "192.168.1.1", "100.64.0.1", "localhost" })
            if (!AgentPaths.ResolvesOnlyLocalAsync(host, CancellationToken.None).GetAwaiter().GetResult()) failures.Add("Local host check refused " + host);
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS: absolute paths, alternate streams, startup/install writes, unlisted UNC names, paths back to this PC and unreadable components.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void Unreadable(List<string> failures)
    {
        var locked = Path.Combine(Path.GetTempPath(), "killermcp-denied-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(locked, "inner"));
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.ReadAttributes
            | System.Security.AccessControl.FileSystemRights.ListDirectory, System.Security.AccessControl.InheritanceFlags.ContainerInherit
            | System.Security.AccessControl.InheritanceFlags.ObjectInherit, System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Deny);
        var info = new DirectoryInfo(locked);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(deny);
        info.SetAccessControl(acl);
        try
        {
            var denied = Path.Combine(locked, "inner", "probe.txt");
            var probe = false;
            try { File.GetAttributes(Path.Combine(locked, "inner")); }
            catch (UnauthorizedAccessException) { probe = true; }
            if (!probe) Console.WriteLine("SKIP access-denied component: this account can still read the denied folder.");
            else if (AgentPaths.AccessRefusal(denied) is null) failures.Add("A path through a folder the account cannot inspect was accepted.");
        }
        finally
        {
            acl = info.GetAccessControl();
            acl.RemoveAccessRule(deny);
            info.SetAccessControl(acl);
            Directory.Delete(locked, true);
        }
    }
}
