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
        Console.WriteLine("PASS: MCP and Bench settings reads blocked, ordinary reads allowed.");
    }
}
