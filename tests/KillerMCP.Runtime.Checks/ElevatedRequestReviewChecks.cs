using System.Diagnostics;
using System.Text.Json;
using KillerMCP;

internal static class ElevatedRequestReviewChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "killermcp-elevated-request-" + Guid.NewGuid().ToString("N"));
        var planted = Path.Combine(root, "KillerBench", "killerbench-cli.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(planted)!);
        File.WriteAllText(planted, "planted");
        var previousCli = Environment.GetEnvironmentVariable("KILLERBENCH_CLI");
        try
        {
            Refused(() => ElevatedRepairBridge.Prepare("format_disk", "{}", root), "an action outside the catalog");
            Refused(() => ElevatedRepairBridge.Prepare("dns_cache_flush", "{\"host\":\"-execute\"}", root), "options the catalog rejects");
            Refused(() => ElevatedRepairBridge.Prepare("privacy_setting", "{\"key\":\"camera-access\",\"state\":\"off\"}", root), "a repair that never runs elevated");
            Refused(() => ElevatedRepairBridge.Prepare("dns_cache_flush", "{\"host\":\"example.com\"}", root), "a KillerBench CLI in a user-writable folder");
            Environment.SetEnvironmentVariable("KILLERBENCH_CLI", planted);
            Refused(() => ElevatedRepairBridge.Prepare("dns_cache_flush", "{\"host\":\"example.com\"}", root), "a CLI chosen through KILLERBENCH_CLI");
            Environment.SetEnvironmentVariable("KILLERBENCH_CLI", previousCli);
            Refused(() => ElevatedRepairBridge.TrustedExecutable(planted), "a user-owned executable");
            ElevatedRepairBridge.TrustedExecutable(Path.Combine(Environment.SystemDirectory, "cmd.exe")).Dispose();
            Console.WriteLine("PASS: the elevated helper rebuilds repairs from the catalog and trusts only an admin-owned CLI.");

            var scrubbed = ElevatedRepairBridge.ChildEnvironment(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DOTNET_STARTUP_HOOKS"] = "C:\\x.dll", ["COR_PROFILER"] = "{0}", ["COR_ENABLE_PROFILING"] = "1",
                ["DOTNET_ROOT"] = "C:\\x", ["CORECLR_PROFILER_PATH"] = "C:\\x.dll", ["PATH"] = "C:\\Windows",
            });
            foreach (var name in new[] { "DOTNET_STARTUP_HOOKS", "COR_PROFILER", "COR_ENABLE_PROFILING", "DOTNET_ROOT", "CORECLR_PROFILER_PATH" })
                if (!scrubbed.TryGetValue(name, out var value) || value is not null) throw new Exception("The elevated child keeps " + name + ".");
            if (scrubbed.ContainsKey("PATH")) throw new Exception("The elevated child environment removed an unrelated variable.");
            Console.WriteLine("PASS: runtime injection variables are cleared for the elevated child.");

            using var options = JsonDocument.Parse("{\"host\":\"example.com\"}");
            var (plan, _) = await KillerBenchRepairs.Find("dns_cache_flush")!.Build(options.RootElement, CancellationToken.None);
            string? sent = null;
            try
            {
                await ElevatedRepairBridge.RunAsync(plan!, CancellationToken.None, start =>
                {
                    sent = start.ArgumentList.Last();
                    throw new System.ComponentModel.Win32Exception(1223);
                });
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (RepairConsentException) { }
            using var request = JsonDocument.Parse(Convert.FromBase64String(sent ?? throw new Exception("The bridge did not build a request.")));
            var names = request.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Contains("Executable") || names.Contains("Arguments") || names.Contains("TimeoutSeconds"))
                throw new Exception("The elevated request still carries an executable, arguments or a timeout: " + string.Join(",", names));
            if (request.RootElement.GetProperty("Action").GetString() != "dns_cache_flush") throw new Exception("The elevated request lost its action.");
            Console.WriteLine("PASS: the elevated request carries only an action name and options.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERBENCH_CLI", previousCli);
            Directory.Delete(root, true);
        }
    }

    private static void Refused(Action action, string what)
    {
        try { action(); }
        catch (RepairTrustException) { return; }
        throw new Exception("The elevated helper accepted " + what + ".");
    }
}
