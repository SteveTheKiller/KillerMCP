using System.Reflection;
using System.Text.Json;
using KillerMCP;

internal static class RepairTrustReviewChecks
{
    public static bool Serve(string[] args)
    {
        if (args.Length is 2 or 3 && args[0] == "--confirm-repair")
        {
            if (Environment.GetEnvironmentVariable("KILLERMCP_TRUST_MARK") is { } mark) File.WriteAllText(mark, "forged consent");
            return true;
        }
        return false;
    }

    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "killermcp-trust-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("KILLERMCP_STATE_DIR");
        IDisposable? scope = null;
        try
        {
            Environment.SetEnvironmentVariable("KILLERMCP_STATE_DIR", root);
            scope = typeof(KillerBenchSettings).GetMethod("UseTestFolders", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, new object[] { root, Path.Combine(root, "bench") }) as IDisposable;
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "killerbench-settings-approved.json"), JsonSerializer.Serialize(new { settingsSha256 = "forged-test-hash" }));
            if (KillerBenchSettings.IsApproved("forged-test-hash")) throw new Exception("A forged approval file authorizes repairs.");
            scope?.Dispose(); scope = null;
            if (string.Equals(KillerBenchSettings.Folder, root, StringComparison.OrdinalIgnoreCase)) throw new Exception("Production state location can be redirected through the environment.");
            var notifier = Environment.GetEnvironmentVariable("KILLERMCP_NOTIFIER");
            var marker = Path.Combine(root, "fake-consent.txt");
            try
            {
                Environment.SetEnvironmentVariable("KILLERMCP_NOTIFIER", Environment.ProcessPath);
                Environment.SetEnvironmentVariable("KILLERMCP_TRUST_MARK", marker);
                var confirmation = new KillerBenchConfirmation("fixture", "Fixture", "Safe", "No change", "fixture", "fixture preview");
                var confirm = typeof(KillerBenchAdapter).GetMethod("DefaultConfirm", BindingFlags.NonPublic | BindingFlags.Static)!;
                var task = (Task<bool>)confirm.Invoke(null, new object?[] { null, confirmation, CancellationToken.None })!;
                if (task.GetAwaiter().GetResult() || File.Exists(marker)) throw new Exception("An environment-selected notifier forged consent.");
                var unproven = (Task<bool>)confirm.Invoke(null, new object?[] { Environment.ProcessPath!, confirmation, CancellationToken.None })!;
                if (unproven.GetAwaiter().GetResult()) throw new Exception("Notifier exit code zero authorized a repair without a verified consent receipt.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("KILLERMCP_NOTIFIER", notifier);
                Environment.SetEnvironmentVariable("KILLERMCP_TRUST_MARK", null);
            }
            Console.WriteLine("PASS: forged approval ignored and production state environment override ignored.");
        }
        finally
        {
            scope?.Dispose();
            Environment.SetEnvironmentVariable("KILLERMCP_STATE_DIR", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
