using KillerMCP.Clients;

if (args.Length != 2 || args[0] is not ("register" or "unregister") || !Path.IsPathFullyQualified(args[1]))
{
    Console.Error.WriteLine("Usage: KillerMCP.Configure register|unregister <absolute KillerMCP executable path>");
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
