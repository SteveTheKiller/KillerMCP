using System.Diagnostics;
using KillerMCP;

internal static class ChildInputReviewChecks
{
    public static bool Serve(string[] args)
    {
        if (args.SequenceEqual(["--review-read-input"]))
        {
            Console.Write(Console.In.ReadToEnd().Length);
            return true;
        }
        return false;
    }

    public static async Task ProbeAsync()
    {
        var result = await ProcessRunner.RunAsync(Environment.ProcessPath!, ["--review-read-input"],
            TimeSpan.FromSeconds(2), 100);
        if (result.ExitCode != 0 || result.StandardOutput != "0")
            throw new InvalidOperationException("An app with no input payload must see immediate EOF.");
        Console.WriteLine("EOF");
    }

    public static async Task RunAsync()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("--review-input-probe");
        using var parent = Process.Start(info)!;
        // Keep the parent's input open, like an MCP client awaiting its next request.
        try
        {
            string? line = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (parent.ExitCode != 0 || line != "EOF")
                throw new InvalidOperationException("The child inherited the open host input: " + await parent.StandardError.ReadToEndAsync());
        }
        finally { if (!parent.HasExited) parent.Kill(true); }
        Console.WriteLine("PASS child receives EOF without a payload (1 selected regression)");
    }
}
