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
        using var acquired = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        Exception? workerError = null;
        var owner = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(false, @"Global\KillerTools-Install-KillerMCP");
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Another installer owns the fixture mutex.");
                acquired.Set();
                release.WaitOne();
                mutex.ReleaseMutex();
            }
            catch (Exception exception) { workerError = exception; acquired.Set(); }
        });
        owner.Start();
        acquired.WaitOne();
        try
        {
            if (workerError != null) throw workerError;
            try
            {
                Invoke("Uninstall", Path.Combine(scratch, "absent-" + Guid.NewGuid().ToString("N")), false);
                throw new InvalidOperationException("Uninstall ignored the active install lease.");
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1618) { }
        }
        finally { release.Set(); owner.Join(); }
        Console.WriteLine("PASS uninstall honors the install lease before touching files (1 selected regression)");
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
