using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class InstallerLifecycleReviewChecks
{
    private static Type _program = null!;

    internal static void Run(string setup, string scratch)
    {
        _program = Assembly.LoadFrom(setup).GetType("KillerMCP.Setup.Program", true)!;
        // The install lease is a lock file in this account's own KillerMCP folder. A second setup of the same
        // account waits its turn, and the machine-wide name another account could hold no longer matters.
        using (var lease = (IDisposable)Invoke("AcquireInstallLease", null)!)
        {
            try
            {
                Invoke("Uninstall", Path.Combine(scratch, "absent-" + Guid.NewGuid().ToString("N")), false);
                throw new InvalidOperationException("Uninstall ignored the active install lease.");
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1618) { }
        }
        using (var squatted = new Mutex(false, @"Global\KillerTools-Install-KillerMCP"))
        {
            if (!squatted.WaitOne(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Another installer owns the old machine-wide name.");
            try { Invoke("Uninstall", Path.Combine(scratch, "absent-" + Guid.NewGuid().ToString("N")), false); }
            finally { squatted.ReleaseMutex(); }
        }
        Console.WriteLine("PASS uninstall honors the per-user install lease, and the old machine-wide name blocks nothing (2 selected regressions)");
        if (Invoke("ElevationRefusal", true, false) is not string refusal || refusal.IndexOf("administrator", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("Setup run as administrator did not refuse and explain.");
        if (Invoke("ElevationRefusal", false, false) is not null || Invoke("ElevationRefusal", true, true) is not null)
            throw new InvalidOperationException("Setup refused an ordinary or isolated test run.");
        Console.WriteLine("PASS setup run as administrator refuses and explains (3 selected regressions)");
        try { Invoke("EnsureVersionAllowed", "2.0.0", "1.0.0", false); throw new InvalidOperationException("Silent downgrade was allowed."); }
        catch (System.Security.SecurityException) { }
        Invoke("EnsureVersionAllowed", "2.0.0", "1.0.0", true);
        Invoke("EnsureVersionAllowed", "2.0.0", "2.0.0", false);
        Invoke("EnsureVersionAllowed", "2.0.0", "2.0.1", false);
        Console.WriteLine("PASS silent downgrade needs explicit permission; same/newer installs allowed (4 selected regressions)");
        string hold = Path.Combine(scratch, "repair-" + Guid.NewGuid().ToString("N") + ".hold");
        using (var repair = new FileStream(hold, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            try { using var idle = (IDisposable)Invoke("AcquireRepairLease", hold)!; throw new InvalidOperationException("An active repair did not block setup."); }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1618) { }
        }
        File.Delete(hold);
        using (var idle = (IDisposable)Invoke("AcquireRepairLease", hold)!)
        {
            try { using var repair = File.Open(hold, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); throw new InvalidOperationException("A repair started while setup held its lease."); }
            catch (IOException) { }
        }
        if (File.Exists(hold)) throw new InvalidOperationException("Setup left its repair hold file behind.");
        Console.WriteLine("PASS repair blocks setup; setup blocks new repairs until its lease ends (3 selected regressions)");
        var removal = (System.Diagnostics.ProcessStartInfo)Invoke("SelfRemovalProcess", Path.Combine(scratch, "setup's copy.exe"), 12345)!;
        string expectedPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!string.Equals(removal.FileName, expectedPowerShell, StringComparison.OrdinalIgnoreCase) || removal.UseShellExecute || !removal.CreateNoWindow)
            throw new InvalidOperationException("Uninstall self-removal did not use the trusted system PowerShell path.");
        if (!removal.Arguments.Contains("-EncodedCommand")) throw new InvalidOperationException("Uninstall did not encode its self-removal command.");
        Console.WriteLine("PASS self-removal uses full system PowerShell path without launching it (1 selected regression)");
    }

    private static object? Invoke(string method, params object?[] args)
    {
        var entry = _program.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Installer policy seam is missing: " + method);
        try { return entry.Invoke(null, args); }
        catch (TargetInvocationException exception) when (exception.InnerException != null) { throw exception.InnerException; }
    }
}
