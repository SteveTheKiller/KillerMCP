using System.Text.Json;
using KillerMCP;

internal static class PdfReviewChecks
{
    internal static async Task RunAsync(string executable, string root)
    {
        await OptimizeVersionGate(executable, root);
        await ConvertBook(executable, root);
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
        string otherFile = Path.Combine(root, "not-a-pdf-or-macro.txt");
        File.WriteAllText(otherFile, "{}");
        using var invalidMacro = JsonDocument.Parse(JsonSerializer.Serialize(new { path = sourcePdf, output = Path.Combine(root, "invalid-macro-extension.pdf"), macroFile = otherFile }));
        Require((await adapters["killerpdf_run_macro"].CallAsync(invalidMacro.RootElement, CancellationToken.None)).IsError, "Exported macros must use the advertised JSON extension");
        using var invalidBates = JsonDocument.Parse(JsonSerializer.Serialize(new { inputs = new[] { otherFile }, outputFolder = Path.Combine(root, "bates-invalid-extension") }));
        Require((await adapters["killerpdf_bates"].CallAsync(invalidBates.RootElement, CancellationToken.None)).IsError, "Bates must accept only advertised PDF inputs");
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

    private static async Task OptimizeVersionGate(string executable, string root)
    {
        string source = Path.Combine(root, "version-gate-source.pdf");
        File.WriteAllText(source, "fixture");
        string? previousVersion = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION");
        try
        {
            foreach (var (version, supported) in new[] { ("1.9.0", false), ("unknown", false), ("1.8.91", false), ("1.9.1", true), ("1.10.0", true) })
            {
                Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION", version);
                var tool = (await KillerPdfAdapter.CreateAsync(executable)).Single(item => item.Tool.Name == "killerpdf_optimize");
                Require(tool.Tool.InputSchema["properties"]!.AsObject().ContainsKey("invalidateSignatures") == supported,
                    "Optimize signature consent must be offered only for PDF 1.9.1 or newer: " + version);
                using var consent = JsonDocument.Parse(JsonSerializer.Serialize(new { path = source, output = Path.Combine(root, "consent-" + version + ".pdf"), invalidateSignatures = true }));
                Require((await tool.CallAsync(consent.RootElement, CancellationToken.None)).IsError != supported,
                    "Optimize must reject unsupported signature consent: " + version);
                using var ordinary = JsonDocument.Parse(JsonSerializer.Serialize(new { path = source, output = Path.Combine(root, "plain-" + version + ".pdf") }));
                Require(!(await tool.CallAsync(ordinary.RootElement, CancellationToken.None)).IsError,
                    "Ordinary optimization must remain available: " + version);
            }
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION", previousVersion); }
        Console.WriteLine("PASS PDF optimize version gates");
    }

    // killerpdf_convert_book runs KillerPDF 1.9.3's --convert-book. Older versions do not have the command, so the
    // tool stays hidden there. Paper, size and margin are EPUB layout options that KillerPDF refuses for DjVu, and a
    // custom size needs both width and height, so those calls stop before KillerPDF starts.
    private static async Task ConvertBook(string executable, string root)
    {
        string? previousVersion = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION");
        string argumentsLog = Path.Combine(root, "convert-book-arguments.json");
        string epub = Path.Combine(root, "novel.epub");
        string djvu = Path.Combine(root, "scan.djvu");
        string djv = Path.Combine(root, "scan-short.DJV");
        foreach (var book in new[] { epub, djvu, djv }) File.WriteAllText(book, "fixture book");
        try
        {
            foreach (var (version, shown) in new[] { ("1.9.2", false), ("1.9.0", false), ("1.8.91", false), ("unknown", false), ("1.9.3", true), ("1.10.0", true) })
            {
                Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION", version);
                Require((await KillerPdfAdapter.CreateAsync(executable)).Any(item => item.Tool.Name == "killerpdf_convert_book") == shown,
                    "killerpdf_convert_book must appear only with KillerPDF 1.9.3 or later: " + version);
            }

            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION", "1.9.3");
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_ARGS_LOG", argumentsLog);
            var tool = (await KillerPdfAdapter.CreateAsync(executable)).Single(item => item.Tool.Name == "killerpdf_convert_book");
            var properties = tool.Tool.InputSchema["properties"]!.AsObject();
            Require(properties.Select(item => item.Key).OrderBy(item => item, StringComparer.Ordinal)
                .SequenceEqual(["height", "margin", "output", "paper", "path", "width"]), "Book conversion inputs: " + properties.ToJsonString());
            Require(!properties["paper"]!.AsObject().ContainsKey("default"), "Paper must have no schema default, which a client would send for DjVu");
            var safety = JsonDocument.Parse(JsonSerializer.Serialize(ToolSafety.For("killerpdf_convert_book"))).RootElement;
            Require(!safety.GetProperty("readOnlyHint").GetBoolean() && !safety.GetProperty("destructiveHint").GetBoolean() && !safety.GetProperty("openWorldHint").GetBoolean(),
                "Book conversion writes only a new file: " + safety.GetRawText());

            async Task<(AppCallResult Result, string[]? Arguments)> Convert(object request)
            {
                if (File.Exists(argumentsLog)) File.Delete(argumentsLog);
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(request));
                var result = await tool.CallAsync(document.RootElement, CancellationToken.None);
                string[]? arguments = File.Exists(argumentsLog)
                    ? [.. JsonDocument.Parse(File.ReadAllText(argumentsLog)).RootElement.GetProperty("args").EnumerateArray().Select(item => item.GetString()!)]
                    : null;
                return (result, arguments);
            }
            // The staged output KillerMCP hands KillerPDF is a fresh temporary path, so the check compares the rest.
            async Task Builds(object request, string output, string[] expected)
            {
                var (result, arguments) = await Convert(request);
                Require(!result.IsError && arguments is not null && File.Exists(output), "Book conversion must succeed: " + result.Text);
                Require(arguments![2].Contains("killermcp-pdf-", StringComparison.Ordinal) && arguments[2].EndsWith(".pdf", StringComparison.Ordinal),
                    "Book conversion must write a staged PDF: " + string.Join(" ", arguments));
                Require(arguments.Take(2).Concat(arguments.Skip(3)).SequenceEqual(expected), "Book conversion arguments: " + string.Join(" ", arguments));
                Require(!arguments.Any(argument => argument is "--shell" or "--overwrite" or "--json"), "Book conversion must not pass --shell, --overwrite or --json");
                Require(JsonDocument.Parse(result.Text).RootElement.GetProperty("output").GetString() == output, "Book conversion must name the requested output: " + result.Text);
            }
            async Task Refuses(object request, string because)
            {
                var (result, arguments) = await Convert(request);
                Require(result.IsError && arguments is null, because + ": " + result.Text);
            }

            string Output(string name) => Path.Combine(root, "book-" + name + ".pdf");
            await Builds(new { path = epub, output = Output("letter"), paper = "letter" }, Output("letter"), ["--convert-book", epub, "--paper", "letter"]);
            await Builds(new { path = epub, output = Output("custom"), width = 150, height = 220.5, margin = 12 }, Output("custom"),
                ["--convert-book", epub, "--width", "150", "--height", "220.5", "--margin", "12"]);
            await Builds(new { path = epub, output = Output("default") }, Output("default"), ["--convert-book", epub]);
            await Builds(new { path = djvu, output = Output("djvu") }, Output("djvu"), ["--convert-book", djvu]);
            await Builds(new { path = djv, output = Output("djv") }, Output("djv"), ["--convert-book", djv]);

            // An existing output is never replaced: the call stops before KillerPDF starts.
            File.WriteAllText(Output("existing"), "keep me");
            var (existing, existingArguments) = await Convert(new { path = epub, output = Output("existing") });
            Require(existing.IsError && existing.Text == "Output already exists" && existingArguments is null && File.ReadAllText(Output("existing")) == "keep me",
                "Book conversion must not replace an existing output: " + existing.Text);

            await Refuses(new { path = djvu, output = Output("djvu-paper"), paper = "a4" }, "DjVu takes no paper size");
            await Refuses(new { path = djv, output = Output("djv-margin"), margin = 10 }, "DjVu takes no margin");
            await Refuses(new { path = djvu, output = Output("djvu-size"), width = 150, height = 220 }, "DjVu takes no page size");
            await Refuses(new { path = epub, output = Output("width-only"), width = 150 }, "Width needs height");
            await Refuses(new { path = epub, output = Output("height-only"), height = 220 }, "Height needs width");
            await Refuses(new { path = epub, output = Output("a5"), paper = "a5" }, "Paper is a4, letter or legal");
            await Refuses(new { path = Path.Combine(root, "version-gate-source.pdf"), output = Output("pdf-input") }, "Input must be EPUB or DjVu");
            await Refuses(new { path = epub, output = Path.Combine(root, "book-output.epub") }, "Output must be a PDF");

            // KillerPDF's own usage refusal, exit 2, comes back as an error with its message.
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_USAGE_ERROR", "Margin leaves no room for text.");
            try
            {
                var (usage, _) = await Convert(new { path = epub, output = Output("usage"), margin = 200 });
                Require(usage.IsError && usage.Text == "Margin leaves no room for text." && !File.Exists(Output("usage")), "KillerPDF usage errors must reach the caller: " + usage.Text);
            }
            finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_USAGE_ERROR", null); }
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_ARGS_LOG", null);
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_PDF_VERSION", previousVersion);
        }
        Console.WriteLine("PASS PDF book conversion");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
