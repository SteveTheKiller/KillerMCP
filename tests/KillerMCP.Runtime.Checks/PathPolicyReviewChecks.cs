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
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS: absolute paths, alternate streams, startup/install writes and unlisted UNC names.");
    }
}
