namespace KillerMCP.Services.Mcp;

internal static class AppCatalog
{
    public static Task<IReadOnlyList<AppAdapter>> LoadAsync()
    {
        var appAdapters = new List<AppAdapter>();
        var killerNotesPath = AppDiscovery.Discover("killernotes");
        var killendarPath = AppDiscovery.Discover("killendar");
        var killerShellPath = AppDiscovery.Discover("killershell");
        var killerScanPath = AppDiscovery.Discover("killerscan");
        var killerPdfPath = AppDiscovery.Discover("killerpdf");
        var killerBenchPath = AppDiscovery.Discover("killerbench");
        var killerShotPath = AppDiscovery.Discover("killershot");
        // Each app is asked for its commands in parallel while requests are already being answered, and an
        // app that hangs or cannot start is left out instead of delaying initialize or ending the server.
        // tools/list and tools/call wait for this; the list keeps the same app order as before.
        return Task.Run<IReadOnlyList<AppAdapter>>(async () =>
        {
            var loads = await Task.WhenAll(
                LoadApp("KillerNotes", () => KillerNotesAdapter.CreateAsync(killerNotesPath)),
                LoadApp("Killendar", async () => new[] { await KillendarAdapter.CreateAsync(killendarPath), await KillendarAdapter.CreateAppointmentAsync(killendarPath) }.OfType<AppAdapter>().ToList()),
                LoadApp("KillerShell", () => KillerShellAdapter.CreateAsync(killerShellPath)),
                LoadApp("KillerScan", () => KillerScanAdapter.CreateAsync(killerScanPath)),
                LoadApp("KillerPDF", () => KillerPdfAdapter.CreateAsync(killerPdfPath)),
                LoadApp("KillerBench", () => KillerBenchAdapter.CreateAsync(killerBenchPath, killerNotesPath)),
                LoadApp("KillerShot", () => KillerShotAdapter.CreateAsync(killerShotPath)),
                LoadApp("cross-app tools", () => Task.FromResult<IReadOnlyList<AppAdapter>>(IntegrationAdapter.Create(killerNotesPath, killendarPath, killerShellPath, killerScanPath, killerPdfPath)))).ConfigureAwait(false);
            foreach (var list in loads) appAdapters.AddRange(list);
            return appAdapters;
        });
    }

    private static async Task<IReadOnlyList<AppAdapter>> LoadApp(string app, Func<Task<IReadOnlyList<AppAdapter>>> create)
    {
        try
        {
            // The apps asked for their commands end with the host, even one that hangs.
            return await AppProcesses.StopWithHost(create).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"KillerMCP left out {app} because it did not answer: {exception.Message}");
            return [];
        }
    }
}
