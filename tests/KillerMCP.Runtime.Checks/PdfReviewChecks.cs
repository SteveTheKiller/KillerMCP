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
        Console.WriteLine("PASS PDF review regressions");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
