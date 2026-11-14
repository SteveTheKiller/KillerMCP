using System;
using System.Diagnostics;
using System.IO;
using KillerMCP.Setup;

internal static class InstallerPathReviewChecks
{
    internal static void Run(string scratch)
    {
        string root = Path.Combine(Path.GetFullPath(scratch), "paths-" + Guid.NewGuid().ToString("N"));
        string physical = Path.Combine(root, "physical");
        string relocated = Path.Combine(root, "relocated-profile");
        Directory.CreateDirectory(Path.Combine(physical, "AppData", "Local", "Programs", "KillerMCP"));
        using (var junction = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c mklink /J \"" + relocated + "\" \"" + physical + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
        {
            junction!.WaitForExit();
            if (junction.ExitCode != 0) throw new InvalidOperationException("The profile junction fixture could not be created.");
        }
        try
        {
            string install = Path.Combine(relocated, "AppData", "Local", "Programs", "KillerMCP");
            ManifestPaths.EnsureSafeRoot(install);
            if (ManifestPaths.Resolve(install, "KillerMCP.exe") != Path.Combine(install, "KillerMCP.exe"))
                throw new InvalidOperationException("A relocated profile installation path was refused.");
            try { ManifestPaths.EnsureSafeRoot(relocated); throw new InvalidOperationException("A linked installation root was accepted."); }
            catch (IOException) { }
            Console.WriteLine("PASS profile ancestor junction allowed; linked installation root refused (2 selected regressions)");
        }
        finally
        {
            Directory.Delete(relocated);
            Directory.Delete(root, true);
        }
    }
}
