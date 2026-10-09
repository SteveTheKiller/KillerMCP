using KillerMCP.Clients;
using KillerMCP.Runtime;

if (args.Length is 1 or 2 && args[0] == "bench-repairs") return BenchRepairs(args.Length == 2 ? args[1] : "status");

if (args.Length != 2 || args[0] is not ("register" or "unregister") || !Path.IsPathFullyQualified(args[1]))
{
    Console.Error.WriteLine("Usage: KillerMCP.Configure register|unregister <absolute KillerMCP executable path>");
    Console.Error.WriteLine("       KillerMCP.Configure bench-repairs off|safe|all|status|clear-lock");
    return 2;
}

try
{
    var messages = args[0] == "register" ? ClientConfiguration.RegisterAll(args[1]) : ClientConfiguration.RemoveAll(args[1]);
    foreach (var message in messages) Console.WriteLine(message);
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or TimeoutException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int BenchRepairs(string command)
{
    try
    {
        switch (command)
        {
            case "status":
                Console.WriteLine("KillerBench repairs through KillerMCP: " + KillerBenchSettings.Name(KillerBenchSettings.ReadLevel()));
                if (KillerBenchSettings.ReadLock() is { } repairLock) Console.WriteLine($"Locked since {repairLock.LockedAtUtc.ToLocalTime():g} after {repairLock.Action}: {repairLock.Reason}");
                return 0;
            case "clear-lock":
                Console.WriteLine(KillerBenchSettings.ClearLock() ? "KillerBench repair lock cleared." : "There was no KillerBench repair lock.");
                return 0;
        }
        if (KillerBenchSettings.Parse(command) is not { } level)
        {
            Console.Error.WriteLine("Usage: KillerMCP.Configure bench-repairs off|safe|all|status|clear-lock");
            return 2;
        }
        KillerBenchSettings.WriteLevel(level);
        Console.WriteLine(level switch
        {
            KillerBenchRepairLevel.Safe => "KillerBench repairs set to safe. MCP clients can request low-impact repairs; Windows still asks for administrator approval.",
            KillerBenchRepairLevel.All => "KillerBench repairs set to all. Disruptive repairs also need your approval in a KillerMCP window on this desktop.",
            _ => "KillerBench repairs are off. MCP clients can still run read-only checks.",
        });
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}
