using System.Security.Principal;
using KillerMCP.Configuration;

if (args.Length != 2 || args[0] is not ("register" or "unregister") || !Path.IsPathFullyQualified(args[1]))
{
    Console.Error.WriteLine("Usage: KillerMCP.Configure register|unregister <absolute KillerMCP executable path>");
    return 2;
}

// Client configs live in the signed-in user's profile. An administrator or SYSTEM run would write them with a
// token that can follow links a standard user planted, so the configurator refuses and explains instead.
// A test run that redirects the client config files writes only into its own scratch files, as isolated setup does.
bool isolated = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KILLERMCP_TEST_CLAUDE_CONFIG"));
if (OperatingSystem.IsWindows())
using (var identity = WindowsIdentity.GetCurrent())
{
    if (!isolated && identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
    {
        Console.Error.WriteLine("KillerMCP installs for your own Windows account. Run setup again without administrator rights, from your normal account.");
        return 11;
    }
}

// Registration names only the KillerMCP.exe installed beside this configurator, so a caller cannot point every
// agent client at some other program.
string beside = Path.Combine(AppContext.BaseDirectory, "KillerMCP.exe");
if (args[0] == "register" && !string.Equals(Path.GetFullPath(args[1]), Path.GetFullPath(beside), StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("KillerMCP.Configure registers only the KillerMCP.exe installed beside it.");
    return 2;
}

try
{
    ConfiguratorLifetime.Establish();
    var messages = args[0] == "register" ? ClientConfiguration.RegisterAll(args[1]) : ClientConfiguration.RemoveAll(args[1]);
    foreach (var message in messages) Console.WriteLine(message);
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException or TimeoutException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
