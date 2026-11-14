using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using KillerMCP;

internal static class ElevatedOutputReviewChecks
{
    public static bool Serve(string[] args)
    {
        if (!args.SequenceEqual(["--repair-output-fixture"])) return false;
        Console.Write("{\"outcome\":\"Failed\"}");
        Console.Out.Flush();
        Thread.Sleep(1200);
        return true;
    }

    public static async Task RunAsync()
    {
        var victim = Path.Combine(Path.GetTempPath(), "killermcp-output-victim-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(victim, "untouched");
        KillerBenchAdapter.ElevatedProcessStartForChecks = start =>
        {
            start.UseShellExecute = false;
            start.Verb = "";
            var output = Regex.Match(start.Arguments, "1>\\\"([^\\\"]+)\\\"");
            if (output.Success)
            {
                if (!CreateHardLink(output.Groups[1].Value, victim, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            return Process.Start(start)!;
        };
        try
        {
            var method = typeof(KillerBenchAdapter).GetMethod("DefaultElevated", BindingFlags.Static | BindingFlags.NonPublic)!;
            var run = await (Task<KillerBenchRun>)method.Invoke(null, new object[] { Environment.ProcessPath!, new[] { "--repair-output-fixture" }, TimeSpan.FromSeconds(10), CancellationToken.None })!;
            if (File.ReadAllText(victim) != "untouched") throw new Exception("Elevated output followed an attacker-created hard link and overwrote another file.");
            if (!run.Output.Contains("Failed", StringComparison.Ordinal)) throw new Exception("Elevated result content changed.");
            Console.WriteLine("PASS: elevated result cannot be replaced through a user-writable output file.");
        }
        finally { KillerBenchAdapter.ElevatedProcessStartForChecks = null; File.Delete(victim); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);
}
