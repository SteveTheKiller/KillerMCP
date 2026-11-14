using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

internal static class InstallerProcessTreeReviewChecks
{
    internal static bool Serve(string[] args)
    {
        if (args.Length > 0 && args[0] == "--tree-child") { Thread.Sleep(Timeout.Infinite); return true; }
        if (args.Length > 1 && args[0] == "--tree-parent")
        {
            using var child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName, "--tree-child") { UseShellExecute = false, CreateNoWindow = true })!;
            File.WriteAllText(args[1] + ".tmp", child.Id + "," + child.StartTime.ToUniversalTime().Ticks);
            File.Move(args[1] + ".tmp", args[1]);
            Thread.Sleep(Timeout.Infinite);
            return true;
        }
        return false;
    }

    internal static void Run(string installer, string scratch)
    {
        Directory.CreateDirectory(scratch);
        var cleanup = Assembly.LoadFrom(installer).GetType("KillerMCP.Setup.Program", true)!.GetMethod("KillTree", BindingFlags.Static | BindingFlags.NonPublic)!;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string marker = Path.Combine(scratch, "child-" + Guid.NewGuid().ToString("N") + ".txt");
            using var parent = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName, "--tree-parent \"" + marker + "\"") { UseShellExecute = false, CreateNoWindow = true })!;
            long parentStart = parent.StartTime.ToUniversalTime().Ticks;
            int childId = 0; long childStart = 0;
            Process? unrelated = null; long unrelatedStart = 0;
            try
            {
                var wait = Stopwatch.StartNew();
                while (!File.Exists(marker) && wait.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(10);
                if (!File.Exists(marker)) throw new InvalidOperationException("Owned child fixture did not start.");
                string[] identity = File.ReadAllText(marker).Split(',');
                childId = int.Parse(identity[0]); childStart = long.Parse(identity[1]);
                if (attempt == 0)
                {
                    using var command = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + parent.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                    var output = command.StandardOutput.ReadToEndAsync(); var error = command.StandardError.ReadToEndAsync();
                    if (!command.WaitForExit(15000)) throw new InvalidOperationException("Owned taskkill probe timed out.");
                    Console.WriteLine("TASKKILL exit=" + command.ExitCode + " stdout=" + output.Result.Trim() + " stderr=" + error.Result.Trim());
                    Console.WriteLine("TASKKILL parentAlive=" + Alive(parent.Id, parentStart) + " childAlive=" + Alive(childId, childStart));
                }
                else
                {
                    unrelated = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName, "--tree-child") { UseShellExecute = false, CreateNoWindow = true })!;
                    unrelatedStart = unrelated.StartTime.ToUniversalTime().Ticks;
                    cleanup.Invoke(null, new object[] { parent });
                    wait.Restart();
                    while ((Alive(parent.Id, parentStart) || Alive(childId, childStart)) && wait.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(20);
                    if (Alive(parent.Id, parentStart) || Alive(childId, childStart)) throw new InvalidOperationException("Native installer timeout cleanup left an owned configurator descendant alive.");
                    if (!Alive(unrelated.Id, unrelatedStart)) throw new InvalidOperationException("Cleanup terminated an unrelated owned fixture.");
                    Console.WriteLine("PASS native installer timeout removes owned parent/child and preserves unrelated process (2 selected regressions)");
                }
            }
            finally
            {
                KillOwned(childId, childStart);
                KillOwned(parent.Id, parentStart);
                if (unrelated != null) { KillOwned(unrelated.Id, unrelatedStart); unrelated.Dispose(); }
                Console.WriteLine("FIXTURE cleanup parentAlive=" + Alive(parent.Id, parentStart) + " childAlive=" + Alive(childId, childStart));
            }
        }
    }

    private static bool Alive(int id, long start)
    {
        if (id <= 0) return false;
        try { using var process = Process.GetProcessById(id); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == start; }
        catch (ArgumentException) { return false; }
    }
    private static void KillOwned(int id, long start)
    {
        if (!Alive(id, start)) return;
        using var process = Process.GetProcessById(id);
        if (process.StartTime.ToUniversalTime().Ticks != start) return;
        process.Kill(); process.WaitForExit(3000);
    }
}
