using System.Text.Json;
using KillerMCP.Runtime;

internal static class PdfReviewChecks
{
    internal static async Task RunAsync(string executable, string root)
    {
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_HELP_STYLE", "modern");
        try
        {
            var modern = await KillerPdfAdapter.CreateAsync(executable);
            foreach (var name in new[] { "killerpdf_merge", "killerpdf_optimize", "killerpdf_split", "killerpdf_export", "killerpdf_signatures", "killerpdf_preflight", "killerpdf_verify_install" })
                Require(modern.Any(adapter => adapter.Tool.Name == name), "Modern help must expose " + name);
            string source = Path.Combine(root, "modern-help.pdf");
            File.WriteAllText(source, "fixture");
            string? previousNotes = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_NOTES");
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", "1");
            try
            {
                var integrations = IntegrationAdapter.Create(executable, null, null, null, executable).ToDictionary(adapter => adapter.Tool.Name);
                using var text = JsonDocument.Parse(JsonSerializer.Serialize(new { path = source }));
                Require(!(await integrations["killerpdf_save_text_as_note"].CallAsync(text.RootElement, CancellationToken.None)).IsError, "Modern help must allow text export to notes");
                using var report = JsonDocument.Parse(JsonSerializer.Serialize(new { path = source, report = "signatures" }));
                Require(!(await integrations["killerpdf_save_report_as_note"].CallAsync(report.RootElement, CancellationToken.None)).IsError, "Modern help must allow reports saved as notes");
            }
            finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_NOTES", previousNotes); }
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_HELP_STYLE", null); }
        var adapters = (await KillerPdfAdapter.CreateAsync(executable)).ToDictionary(adapter => adapter.Tool.Name);
        var sourcePdf = Path.Combine(root, "review-source.pdf");
        File.WriteAllText(sourcePdf, "fixture");
        using var invisible = JsonDocument.Parse(JsonSerializer.Serialize(new { path = sourcePdf, output = Path.Combine(root, "invisible-signature.pdf"), certificate = "fixture", appearanceCorner = "top-right" }));
        Require((await adapters["killerpdf_sign"].CallAsync(invisible.RootElement, CancellationToken.None)).IsError, "Signature appearance requires a page");
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "document.pdf"), "fixture one");
        File.WriteAllText(Path.Combine(second, "DOCUMENT.PDF"), "fixture two");
        using var duplicateBates = JsonDocument.Parse(JsonSerializer.Serialize(new { inputs = new[] { Path.Combine(first, "document.pdf"), Path.Combine(second, "DOCUMENT.PDF") }, outputFolder = Path.Combine(root, "bates-collision") }));
        Require((await adapters["killerpdf_bates"].CallAsync(duplicateBates.RootElement, CancellationToken.None)).IsError, "Bates must reject case-insensitive duplicate output file names");
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_RENAME", "1");
        try
        {
            string output = Path.Combine(root, "requested-macro-output.pdf");
            using var macro = JsonDocument.Parse(JsonSerializer.Serialize(new { path = sourcePdf, output, macro = "Rename fixture" }));
            var result = await adapters["killerpdf_run_macro"].CallAsync(macro.RootElement, CancellationToken.None);
            Require(!result.IsError && File.ReadAllText(output) == "fixture output", "Renamed macro output must be preserved at the requested path: " + result.Text);
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_RENAME", null); }
        if (Environment.GetEnvironmentVariable("KILLERMCP_CHECK_SECOND_VOLUME") is { Length: > 0 } otherVolume)
        {
            string destination = Path.Combine(otherVolume, "split-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var split = JsonDocument.Parse(JsonSerializer.Serialize(new { path = sourcePdf, outputFolder = destination }));
                var result = await adapters["killerpdf_split"].CallAsync(split.RootElement, CancellationToken.None);
                Require(!result.IsError && File.Exists(Path.Combine(destination, "item-1.pdf")), "Directory output must publish across volume roots: " + result.Text);
            }
            finally { if (Directory.Exists(destination)) Directory.Delete(destination, true); }
        }
        Console.WriteLine("PASS PDF review regressions");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
