using KillerMCP;
using System.Diagnostics;
using System.Text.Json;

internal static class IntegrationPdfReviewChecks
{
    public static bool Serve(string[] args)
    {
        if (args.Length == 2 && args[0] == "--pdf-review-writer")
        {
            using var browserLock = new FileStream(Path.Combine(Path.GetDirectoryName(args[1])!, "browser.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            Thread.Sleep(2400);
            File.WriteAllText(args[1], "%PDF-1.7\npartial");
            Thread.Sleep(900);
            File.AppendAllText(args[1], "\ncompleted\n%%EOF\n");
            Thread.Sleep(1500);
            return true;
        }
        var output = args.FirstOrDefault(value => value.StartsWith("--print-to-pdf=", StringComparison.Ordinal));
        if (output is null) return false;
        var html = File.ReadAllText(new Uri(args[^1]).LocalPath);
        if (html.Contains("pdf-fixture-never")) { Thread.Sleep(800); return true; }
        if (html.Contains("pdf-fixture-failed")) { Environment.ExitCode = 7; return true; }
        if (html.Contains("pdf-fixture-fast")) { File.WriteAllText(output[15..], "%PDF-1.7\ncompleted\n%%EOF\n"); return true; }
        var child = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        child.ArgumentList.Add("--pdf-review-writer");
        child.ArgumentList.Add(output[15..]);
        Process.Start(child)!.Dispose();
        return true;
    }

    public static async Task RunAsync()
    {
        var root = Environment.GetEnvironmentVariable("KILLERMCP_PDF_CHECK_ROOT")
            ?? throw new InvalidOperationException("Set KILLERMCP_PDF_CHECK_ROOT to an owned scratch folder.");
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, Guid.NewGuid().ToString("N") + ".pdf");
        var tool = IntegrationAdapter.Create(null, null, null, null, null, new IntegrationPdfOptions
        {
            EdgePath = Environment.ProcessPath!, TemporaryRoot = root, Timeout = TimeSpan.FromSeconds(20)
        }).Single();
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { content = "fixture", output }));
        var result = await tool.CallAsync(input.RootElement, CancellationToken.None);
        if (result.IsError || !File.Exists(output) || !File.ReadAllText(output).Contains("completed\n%%EOF"))
            throw new InvalidOperationException("Detached PDF writer was not awaited: " + result.Text);
        Console.WriteLine("PASS detached PDF writer completion");
        if (Directory.EnumerateDirectories(root, "killermcp-integration-*").Any())
            throw new InvalidOperationException("The browser shutdown lock leaked a temporary folder.");
        Console.WriteLine("PASS browser shutdown cleanup");
        var atomicOutput = Path.Combine(root, Guid.NewGuid().ToString("N") + ".pdf");
        using var atomicInput = JsonDocument.Parse(JsonSerializer.Serialize(new { content = "fixture", output = atomicOutput }));
        var pending = tool.CallAsync(atomicInput.RootElement, CancellationToken.None);
        bool premature = false;
        while (!pending.IsCompleted)
        {
            if (File.Exists(atomicOutput) && !File.ReadAllText(atomicOutput).Contains("completed\n%%EOF")) { premature = true; break; }
            await Task.Delay(10);
        }
        var published = await pending;
        if (premature || published.IsError || !File.ReadAllText(atomicOutput).Contains("completed\n%%EOF"))
            throw new InvalidOperationException("The destination became visible before PDF creation finished.");
        Console.WriteLine("PASS destination appears only after PDF completion");
        var staleRoot = Path.Combine(root, "sweep");
        Directory.CreateDirectory(staleRoot);
        var stale = Path.Combine(staleRoot, "killermcp-integration-" + Guid.NewGuid().ToString("N"));
        var recent = Path.Combine(staleRoot, "killermcp-integration-" + Guid.NewGuid().ToString("N"));
        var unrelated = Path.Combine(staleRoot, "killermcp-integration-important");
        foreach (var folder in new[] { stale, recent, unrelated }) { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "keep.txt"), "fixture"); }
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-2));
        await IntegrationAdapter.SweepStaleTemporaryFoldersAsync(staleRoot);
        if (Directory.Exists(stale) || !Directory.Exists(recent) || !Directory.Exists(unrelated))
            throw new InvalidOperationException("The stale sweep did not remove only the old owned folder.");
        Console.WriteLine("PASS scoped stale temporary-folder sweep");
        var locked = Path.Combine(staleRoot, "killermcp-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(locked);
        using (var lease = new FileStream(Path.Combine(locked, ".killermcp-active"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Directory.SetLastWriteTimeUtc(locked, DateTime.UtcNow.AddDays(-2));
            await IntegrationAdapter.SweepStaleTemporaryFoldersAsync(staleRoot);
            if (!Directory.Exists(locked)) throw new InvalidOperationException("The sweep removed an active folder.");
        }
        Console.WriteLine("PASS active temporary-folder lease preservation");
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var keptFile = Path.Combine(outside, "keep.txt");
        File.WriteAllText(keptFile, "keep");
        var linked = Path.Combine(staleRoot, "killermcp-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(linked);
        var junction = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        junction.ArgumentList.Add("/c"); junction.ArgumentList.Add("mklink"); junction.ArgumentList.Add("/J");
        junction.ArgumentList.Add(Path.Combine(linked, "profile")); junction.ArgumentList.Add(outside);
        using (var command = Process.Start(junction)!)
        {
            await command.WaitForExitAsync();
            if (command.ExitCode != 0) throw new InvalidOperationException("Could not construct the scoped junction fixture: " + await command.StandardError.ReadToEndAsync());
        }
        Directory.SetLastWriteTimeUtc(linked, DateTime.UtcNow.AddDays(-2));
        await IntegrationAdapter.SweepStaleTemporaryFoldersAsync(staleRoot);
        if (!Directory.Exists(linked) || File.ReadAllText(keptFile) != "keep") throw new InvalidOperationException("The sweep followed a junction.");
        Console.WriteLine("PASS junction and outside-file preservation");

        async Task<AppCallResult> Call(string name, string content, IntegrationPdfOptions options, CancellationToken token = default)
        {
            var selected = IntegrationAdapter.Create(null, null, null, null, null, options).Single();
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { content, output = Path.Combine(root, name + ".pdf") }));
            return await selected.CallAsync(document.RootElement, token);
        }
        var deadlineTimer = Stopwatch.StartNew();
        var expired = await Call("deadline", "pdf-fixture-never", new IntegrationPdfOptions { EdgePath = Environment.ProcessPath!, TemporaryRoot = root, Timeout = TimeSpan.FromMilliseconds(1200) });
        if (!expired.IsError || !expired.Text.Contains("timed out") || deadlineTimer.Elapsed >= TimeSpan.FromMilliseconds(1800) || File.Exists(Path.Combine(root, "deadline.pdf")))
            throw new InvalidOperationException("The launcher and PDF wait did not share one deadline.");
        Console.WriteLine("PASS total render deadline");
        var copyFailed = await Call("failed-copy", "pdf-fixture-fast", new IntegrationPdfOptions
        {
            EdgePath = Environment.ProcessPath!, TemporaryRoot = root,
            CopyPdf = async (input, destination, token) => { await destination.WriteAsync(new byte[] { 1, 2, 3 }, token); throw new IOException("Injected storage failure after partial copy"); }
        });
        if (!copyFailed.IsError || File.Exists(Path.Combine(root, "failed-copy.pdf")) || Directory.EnumerateFiles(root, ".killermcp-pdf-*.tmp").Any())
            throw new InvalidOperationException("A failed copy left a destination or sibling temporary file.");
        Console.WriteLine("PASS failed-copy destination and sibling cleanup");
        var raced = await Call("raced", "pdf-fixture-fast", new IntegrationPdfOptions
        {
            EdgePath = Environment.ProcessPath!, TemporaryRoot = root,
            CopyPdf = async (input, destination, token) => { await File.WriteAllTextAsync(Path.Combine(root, "raced.pdf"), "existing destination", token); await input.CopyToAsync(destination, token); }
        });
        if (!raced.IsError || File.ReadAllText(Path.Combine(root, "raced.pdf")) != "existing destination" || Directory.EnumerateFiles(root, ".killermcp-pdf-*.tmp").Any())
            throw new InvalidOperationException("Publication overwrote a destination created while rendering.");
        Console.WriteLine("PASS concurrent destination preservation");
        using var cancellation = new CancellationTokenSource();
        try
        {
            await Call("cancelled-copy", "pdf-fixture-fast", new IntegrationPdfOptions
            {
                EdgePath = Environment.ProcessPath!, TemporaryRoot = root,
                CopyPdf = async (input, destination, token) => { await destination.WriteAsync(new byte[] { 1, 2, 3 }, token); cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            }, cancellation.Token);
            throw new InvalidOperationException("A canceled PDF call returned success.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        if (File.Exists(Path.Combine(root, "cancelled-copy.pdf")) || Directory.EnumerateFiles(root, ".killermcp-pdf-*.tmp").Any())
            throw new InvalidOperationException("Cancellation left a partial destination or sibling file.");
        Console.WriteLine("PASS cancellation cleanup");
        var failed = await Call("failed-launcher", "pdf-fixture-failed", new IntegrationPdfOptions { EdgePath = Environment.ProcessPath!, TemporaryRoot = root });
        if (!failed.IsError || File.Exists(Path.Combine(root, "failed-launcher.pdf"))) throw new InvalidOperationException("A failed browser reported PDF success.");
        Console.WriteLine("PASS browser failure refusal");
        var existing = await Call("raced", "pdf-fixture-fast", new IntegrationPdfOptions { EdgePath = Environment.ProcessPath!, TemporaryRoot = root });
        if (!existing.IsError || File.ReadAllText(Path.Combine(root, "raced.pdf")) != "existing destination") throw new InvalidOperationException("An existing destination was not refused intact.");
        Console.WriteLine("PASS existing destination refusal");
        Console.WriteLine("Integration PDF review: 12 checks passed");
    }

    public static async Task RunRealAsync()
    {
        var root = Environment.GetEnvironmentVariable("KILLERMCP_PDF_CHECK_ROOT")
            ?? throw new InvalidOperationException("Set KILLERMCP_PDF_CHECK_ROOT to an owned scratch folder.");
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, Guid.NewGuid().ToString("N") + ".pdf");
        var compatibility = Environment.GetEnvironmentVariable("__COMPAT_LAYER");
        try
        {
            Environment.SetEnvironmentVariable("__COMPAT_LAYER", "DetectorsAppHealth");
            var tool = IntegrationAdapter.Create(null, null, null, null, null, new IntegrationPdfOptions { TemporaryRoot = root }).Single();
            using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { content = "PDF compatibility regression", format = "text", output }));
            var result = await tool.CallAsync(input.RootElement, CancellationToken.None);
            if (result.IsError || !File.Exists(output) || new FileInfo(output).Length < 100)
                throw new InvalidOperationException("PDF creation under DetectorsAppHealth failed: " + result.Text);
            if (Environment.GetEnvironmentVariable("__COMPAT_LAYER") != "DetectorsAppHealth")
                throw new InvalidOperationException("PDF creation changed the host compatibility setting.");
            if (Directory.EnumerateDirectories(root, "killermcp-integration-*").Any())
                throw new InvalidOperationException("Real Edge left a temporary folder after PDF creation.");
            Console.WriteLine("PASS PDF creation under DetectorsAppHealth");
        }
        finally
        {
            Environment.SetEnvironmentVariable("__COMPAT_LAYER", compatibility);
        }
    }
}
