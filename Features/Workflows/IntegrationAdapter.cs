using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP;

/// <summary>Replaceable browser and file system boundaries for PDF creation checks.</summary>
public sealed class IntegrationPdfOptions
{
    public string? EdgePath { get; init; }
    public string? TemporaryRoot { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(90);
    public Func<Stream, Stream, CancellationToken, Task>? CopyPdf { get; init; }
}

public static partial class IntegrationAdapter
{
    /// <summary>Removes abandoned PDF workflow folders from the temporary directory.</summary>
    public static async Task SweepStaleTemporaryFoldersAsync(string? temporaryRoot = null)
    {
        try
        {
            var root = temporaryRoot ?? Path.GetTempPath();
            if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var folder in Directory.EnumerateDirectories(root, "killermcp-integration-*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var name = Path.GetFileName(folder);
                    if (!Guid.TryParseExact(name["killermcp-integration-".Length..], "N", out _)
                        || Directory.GetLastWriteTimeUtc(folder) >= cutoff || HasReparsePoint(folder)) continue;
                    // A running workflow holds this file open, including work older than a day.
                    using (new FileStream(Path.Combine(folder, ".killermcp-active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                    await DeleteAsync(folder).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, FileStream> TemporaryLeases = new(StringComparer.OrdinalIgnoreCase);

    private static void CreateTemporaryFolder(string path)
    {
        Directory.CreateDirectory(path);
        TemporaryLeases[path] = new FileStream(Path.Combine(path, ".killermcp-active"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    public static IReadOnlyList<AppAdapter> Create(string? notes, string? calendar, string? shell, string? scan, string? pdf, IntegrationPdfOptions? pdfOptions = null)
    {
        var tools = new List<AppAdapter>
        {
            Tool("killer_create_pdf", "Create a new PDF from HTML or plain text. Use this to turn agent output or data from a Killer app into a PDF.", ContentPdfSchema(), (input, token) => CreatePdf(input, token, pdfOptions)),
        };
        if (IsFile(notes)) tools.Add(Tool("killernotes_export_pdf", "Export one KillerNotes note directly to a new PDF.", IdOutputSchema(), (input, token) => NotePdf(notes!, input, token)));
        if (IsFile(notes) && IsFile(pdf)) tools.Add(Tool("killernotes_export_notes_pdf", "Export up to 50 KillerNotes notes, in the order given, into one new PDF combined by KillerPDF.", NotesPdfSchema(), (input, token) => NotesPdf(notes!, pdf!, input, token)));
        if (IsFile(scan)) tools.Add(Tool("killerscan_export_report_pdf", "Scan a network and create a PDF report with a device table.", ScanOutputSchema(), (input, token) => ScanPdf(scan!, input, token)));
        if (IsFile(scan) && IsFile(notes)) tools.Add(Tool("killerscan_save_report_note", "Scan a network and save the device report as a new KillerNotes note.", ScanNoteSchema(), (input, token) => ScanNote(scan!, notes!, input, token)));
        if (IsFile(calendar) && AppVersion.IsAtLeast(calendar!, new Version(1, 1, 4))) tools.Add(Tool("killendar_export_agenda_pdf", "Export a Killendar agenda to a new PDF.", AgendaOutputSchema(), (input, token) => AgendaPdf(calendar!, input, token)));
        if (IsFile(calendar) && AppVersion.IsAtLeast(calendar!, new Version(1, 1, 4)) && IsFile(notes)) tools.Add(Tool("killendar_save_agenda_note", "Save a Killendar agenda as a new KillerNotes note.", AgendaNoteSchema(), (input, token) => AgendaNote(calendar!, notes!, input, token)));
        if (IsFile(shell)) tools.Add(Tool("killershell_export_directory_pdf", "Export a KillerShell directory listing to a new PDF.", DirectoryOutputSchema(), (input, token) => DirectoryPdf(shell!, input, token)));
        if (IsFile(shell) && IsFile(notes)) tools.Add(Tool("killershell_save_directory_note", "Save a KillerShell directory listing as a new KillerNotes note.", DirectoryNoteSchema(), (input, token) => DirectoryNote(shell!, notes!, input, token)));
        if (IsFile(pdf) && IsFile(notes)) tools.Add(Tool("killerpdf_save_text_as_note", "Export the text of a local PDF as Markdown with KillerPDF and save it as one new KillerNotes note. Scanned pages need killerpdf_ocr or killerpdf_scan_to_text first.", PdfTextNoteSchema(), (input, token) => PdfTextToNote(pdf!, notes!, input, token)));
        if (IsFile(pdf) && IsFile(notes)) tools.Add(Tool("killerpdf_save_report_as_note", "Run a KillerPDF report on a local PDF, such as its signatures, comments, measurements or preflight findings, and save the report as a new KillerNotes note. Never changes the PDF.", PdfReportNoteSchema(), (input, token) => PdfReportToNote(pdf!, notes!, input, token)));
        if (IsFile(pdf) && IsFile(notes)) tools.Add(Tool("killerpdf_save_pages_as_notes", "Render up to 100 selected KillerPDF pages and save each PNG of up to 32 MiB as a new KillerNotes note.", PdfNotesSchema(), (input, token) => PdfPagesToNotes(pdf!, notes!, input, token)));
        return tools;
    }

    private static AppAdapter Tool(string name, string description, JsonObject schema, Func<JsonElement, CancellationToken, Task<AppCallResult>> call) => new(new RuntimeTool(name, description, schema), call);

    private static async Task<AppCallResult> CreatePdf(JsonElement input, CancellationToken token, IntegrationPdfOptions? options)
    {
        if (!Exact(input, "content", "format", "title", "output") || !Text(input, "content", 1_000_000, out var content) || !Text(input, "output", 1024, out var output) || !NewPdf(output!)) return Error("Provide content and a new absolute PDF output path");
        var format = input.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : "html";
        if (format is not ("html" or "text")) return Error("Format must be html or text");
        var title = input.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "Killer Tools Report" : "Killer Tools Report";
        var body = format == "html" ? content! : $"<pre>{WebUtility.HtmlEncode(content)}</pre>";
        return await RenderPdf(Document(title, body), output!, token, options).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> NotePdf(string notes, JsonElement input, CancellationToken token)
    {
        if (!IdOutput(input, out var id, out var output)) return Error("Provide a note ID and a new absolute PDF output path");
        var temp = TempFolder();
        try
        {
            CreateTemporaryFolder(temp);
            var html = Path.Combine(temp, "note.html");
            var response = await RunNotes(notes, ["export", "--id", id.ToString(), "--output", html], token).ConfigureAwait(false);
            if (response.ExitCode != 0 || !File.Exists(html)) return ProcessError("KillerNotes", response);
            return await RenderPdf(await File.ReadAllTextAsync(html, token).ConfigureAwait(false), output!, token).ConfigureAwait(false);
        }
        finally { await DeleteAsync(temp).ConfigureAwait(false); }
    }

    private static async Task<AppCallResult> ScanPdf(string scan, JsonElement input, CancellationToken token)
    {
        if (!ScanInput(input, true, out var target, out var full, out var limit, out var output, out _)) return Error("Provide valid scan options and a new absolute PDF output path");
        var json = await Scan(scan, target, full, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        var rendered = await RenderPdf(ScanReport(json.Root, json.Target), output!, token).ConfigureAwait(false);
        if (rendered.IsError) return rendered;
        return Ok(JsonSerializer.Serialize(new { output, target = json.Target, devices = Rows(json.Root).Length }));
    }

    private static async Task<AppCallResult> ScanNote(string scan, string notes, JsonElement input, CancellationToken token)
    {
        if (!ScanInput(input, false, out var target, out var full, out var limit, out _, out var group)) return Error("Provide valid scan options");
        var json = await Scan(scan, target, full, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        return await CreateNote(notes, "KillerScan network report", MarkdownScanReport(json.Root, json.Target), group, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> AgendaPdf(string calendar, JsonElement input, CancellationToken token)
    {
        if (!AgendaInput(input, true, out var date, out var days, out var limit, out var output, out _)) return Error("Provide a date, valid agenda options, and a new absolute PDF output path");
        var json = await Agenda(calendar, date!, days, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        return await RenderPdf(JsonReport($"Killendar Agenda: {date}", json.Root), output!, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> AgendaNote(string calendar, string notes, JsonElement input, CancellationToken token)
    {
        if (!AgendaInput(input, false, out var date, out var days, out var limit, out _, out var group)) return Error("Provide a date and valid agenda options");
        var json = await Agenda(calendar, date!, days, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        return await CreateNote(notes, $"Killendar agenda {date}", MarkdownReport(json.Root), group, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> DirectoryPdf(string shell, JsonElement input, CancellationToken token)
    {
        if (!DirectoryInput(input, true, out var path, out var limit, out var output, out _)) return Error("Provide an existing absolute directory and a new absolute PDF output path");
        var json = await DirectoryList(shell, path!, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        return await RenderPdf(JsonReport($"Directory: {path}", json.Root), output!, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> DirectoryNote(string shell, string notes, JsonElement input, CancellationToken token)
    {
        if (!DirectoryInput(input, false, out var path, out var limit, out _, out var group)) return Error("Provide an existing absolute directory");
        var json = await DirectoryList(shell, path!, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        return await CreateNote(notes, $"Directory listing: {Path.GetFileName(path)}", MarkdownReport(json.Root), group, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> PdfPagesToNotes(string pdf, string notes, JsonElement input, CancellationToken token)
    {
        if (!Exact(input, "path", "pages", "dpi", "group") || !ExistingFile(input, "path", ".pdf", out var path)) return Error(TooLarge(input, "path") ?? "Provide an existing absolute PDF path");
        string? pages = "1";
        if (input.TryGetProperty("pages", out var p) && (p.ValueKind != JsonValueKind.String || (pages = p.GetString())!.Length > 200 || !PageList().IsMatch(pages))) return Error("Pages must look like 1-3,5");
        if (!BoundedPageSelection(pages!)) return Error("Select 1 to 100 distinct positive page numbers");
        if (NotesContent.AvailabilityRefusal(notes) is { } unavailable) return Error(unavailable);
        var dpi = 150;
        if (!OptionalInt(input, "dpi", 72, 600, ref dpi)) return Error("DPI must be a whole number from 72 to 600");
        var group = input.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
        var temp = TempFolder();
        try
        {
            CreateTemporaryFolder(temp);
            var rendered = await RunApp(pdf, ["--to-image", path!, temp, "--pages", pages!, "--dpi", dpi.ToString(), "--format", "png"], 300, token).ConfigureAwait(false);
            if (rendered.ExitCode != 0) return ProcessError("KillerPDF", rendered);
            var images = Directory.EnumerateFiles(temp, "*.png").Take(101).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            if (images.Length == 0) return Error("KillerPDF did not render any pages");
            if (images.Length > 100) return Error("KillerPDF rendered more than 100 pages; no notes were imported");
            if (images.Any(image => RenderedPageNumber(image) < 1)) return Error("KillerPDF returned an image without a source page number");
            var oversized = images.FirstOrDefault(image => new FileInfo(image).Length > 32L * 1024 * 1024);
            if (oversized is not null)
                return Error(new JsonObject { ["error"] = "A rendered PNG is larger than the 32 MiB KillerNotes accepts. Reduce DPI.", ["failedAtPage"] = RenderedPageNumber(oversized), ["created"] = new JsonArray() }.ToJsonString());
            var created = new JsonArray();
            foreach (var image in images)
            {
                var args = new List<string> { "import-image", "--path", image };
                if (!string.IsNullOrWhiteSpace(group)) args.AddRange(["--group", group!]);
                var result = await RunNotes(notes, args, token).ConfigureAwait(false);
                JsonNode? note = null;
                if (result.ExitCode == 0)
                {
                    try { note = JsonNode.Parse(result.StandardOutput); }
                    catch (JsonException) { }
                }
                if (note is null)
                {
                    // Notes already made stay made, so the error names them.
                    var failure = result.ExitCode != 0 ? ProcessError("KillerNotes", result).Text : "KillerNotes returned an invalid response";
                    return Error(new JsonObject { ["error"] = failure, ["failedAtPage"] = RenderedPageNumber(image), ["created"] = created }.ToJsonString());
                }
                created.Add(note);
            }
            return Ok(new JsonObject { ["created"] = created }.ToJsonString());
        }
        finally { await DeleteAsync(temp).ConfigureAwait(false); }
    }

    private static int RenderedPageNumber(string image)
    {
        string name = Path.GetFileNameWithoutExtension(image);
        int marker = name.LastIndexOf("page-", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 && int.TryParse(name[(marker + 5)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int page) && page > 0 ? page : 0;
    }

    private static bool BoundedPageSelection(string selection)
    {
        var selected = new HashSet<int>();
        foreach (string part in selection.Split(','))
        {
            string[] bounds = part.Split('-');
            if (!int.TryParse(bounds[0], out int first) || first < 1 ||
                !int.TryParse(bounds[^1], out int last) || last < 1) return false;
            if (first > last) (first, last) = (last, first);
            if ((long)last - first + 1 > 100) return false;
            for (int page = first; ; page++)
            {
                selected.Add(page);
                if (selected.Count > 100) return false;
                if (page == last) break;
            }
        }
        return selected.Count > 0;
    }

    private static async Task<AppCallResult> PdfTextToNote(string pdf, string notes, JsonElement input, CancellationToken token)
    {
        if (!Exact(input, "path", "pages", "title", "group") || !ExistingFile(input, "path", ".pdf", out var path)) return Error(TooLarge(input, "path") ?? "Provide an existing absolute PDF path");
        string? pages = null, title = Path.GetFileNameWithoutExtension(path), group = null;
        if (input.TryGetProperty("pages", out var p) && (p.ValueKind != JsonValueKind.String || (pages = p.GetString())!.Length > 200 || !PageList().IsMatch(pages))) return Error("Pages must look like 1-3,5");
        if (input.TryGetProperty("title", out var t) && (t.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title = t.GetString()) || title!.Length > 240)) return Error("Title must be 1 to 240 characters");
        if (input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return Error("Group must be at most 240 characters");
        // A KillerPDF without --export would open its window with the file instead, so ask first.
        var help = await RunApp(pdf, ["--help"], 15, token).ConfigureAwait(false);
        if (help.ExitCode != 0 || !KillerPdfCapabilities.Supports(help.StandardOutput, "--export <in.pdf>")) return Error("KillerPDF 1.9 or later is needed to export a PDF's text");
        var temp = TempFolder();
        try
        {
            CreateTemporaryFolder(temp);
            var markdown = Path.Combine(temp, "text.md");
            var args = new List<string> { "--export", path!, markdown };
            if (pages is not null) args.AddRange(["--pages", pages]);
            var exported = await RunApp(pdf, args, 300, token).ConfigureAwait(false);
            if (exported.ExitCode != 0 || !File.Exists(markdown)) return Error(Limit(string.IsNullOrWhiteSpace(exported.StandardOutput) ? "KillerPDF could not export the text" : exported.StandardOutput.Trim()));
            using var reader = File.OpenText(markdown);
            var characters = new char[Math.Min(reader.BaseStream.Length, NotesContent.NoteLimit) + 1];
            int count = await reader.ReadBlockAsync(characters.AsMemory(), token).ConfigureAwait(false);
            if (count > NotesContent.NoteLimit) return Error("The exported text exceeds the 500,000 characters a KillerNotes note holds.");
            var text = new string(characters, 0, count);
            if (string.IsNullOrWhiteSpace(text)) return Error("The PDF has no text to save. Run killerpdf_ocr or killerpdf_scan_to_text on a scanned PDF first.");
            return await CreateNote(notes, title!, text, group, token).ConfigureAwait(false);
        }
        finally { await DeleteAsync(temp).ConfigureAwait(false); }
    }

    private static readonly (string Name, string Command, string Label)[] PdfReports =
    [
        ("signatures", "--signatures", "Signatures"), ("comments", "--comments", "Comments"),
        ("measurements", "--measurements", "Measurements"), ("navigation", "--navigation-audit", "Navigation audit"),
        ("layers", "--layers", "Layers"), ("print-production", "--print-production", "Print production"),
        ("portfolio", "--portfolio", "Portfolio"), ("attachments", "--attachments", "Attachments"),
        ("separations", "--separations", "Separations"), ("preflight", "--preflight", "Preflight"),
        ("accessibility", "--accessibility", "Accessibility"), ("fields", "--recognize-fields", "Recognized form fields"),
    ];

    private static async Task<AppCallResult> PdfReportToNote(string pdf, string notes, JsonElement input, CancellationToken token)
    {
        if (!Exact(input, "path", "report", "title", "group") || !ExistingFile(input, "path", ".pdf", out var path)) return Error(TooLarge(input, "path") ?? "Provide an existing absolute PDF path and a report");
        if (!input.TryGetProperty("report", out var r) || r.ValueKind != JsonValueKind.String || PdfReports.FirstOrDefault(item => item.Name == r.GetString()) is not { Command: not null } report) return Error("Report must be one of " + string.Join(", ", PdfReports.Select(item => item.Name)));
        string? title = $"KillerPDF {report.Label}: {Path.GetFileName(path)}", group = null;
        if (input.TryGetProperty("title", out var t) && (t.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(title = t.GetString()) || title!.Length > 240)) return Error("Title must be 1 to 240 characters");
        if (input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return Error("Group must be at most 240 characters");
        // A KillerPDF without the report would open its window with the file instead, so ask first.
        var help = await RunApp(pdf, ["--help"], 15, token).ConfigureAwait(false);
        if (help.ExitCode != 0 || !KillerPdfCapabilities.Supports(help.StandardOutput, report.Command + " <in.pdf>")) return Error($"This KillerPDF has no {report.Label.ToLowerInvariant()} report; KillerPDF 1.9 or later is needed");
        // The reports exit 3 when they have findings, which is still a finished report.
        var result = await RunApp(pdf, [report.Command, path!], 120, token).ConfigureAwait(false);
        if (result.ExitCode is not (0 or 3) || result.OutputExceeded || string.IsNullOrWhiteSpace(result.StandardOutput)) return Error(Limit(string.IsNullOrWhiteSpace(result.StandardOutput) ? "KillerPDF could not run the report" : result.StandardOutput.Trim()));
        return await CreateNote(notes, title!, "```text\n" + result.StandardOutput.TrimEnd() + "\n```\n", group, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> NotesPdf(string notes, string pdf, JsonElement input, CancellationToken token)
    {
        if (!Exact(input, "ids", "output") || !input.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() is < 1 or > 50
            || !Text(input, "output", 1024, out var output) || !NewPdf(output!)) return Error("Provide 1 to 50 note IDs and a new absolute PDF output path");
        var list = new List<int>();
        foreach (var id in ids.EnumerateArray())
        {
            if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || number < 1) return Error("Note IDs must be positive whole numbers");
            list.Add(number);
        }
        var temp = TempFolder();
        try
        {
            CreateTemporaryFolder(temp);
            var parts = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var html = Path.Combine(temp, $"note-{i + 1}.html");
                var exported = await RunNotes(notes, ["export", "--id", list[i].ToString(), "--output", html], token).ConfigureAwait(false);
                if (exported.ExitCode != 0 || !File.Exists(html)) return ProcessError("KillerNotes", exported);
                var part = Path.Combine(temp, $"note-{i + 1}.pdf");
                var rendered = await RenderPdf(await File.ReadAllTextAsync(html, token).ConfigureAwait(false), part, token).ConfigureAwait(false);
                if (rendered.IsError) return rendered;
                parts.Add(part);
            }
            var combined = parts[0];
            if (parts.Count > 1)
            {
                combined = Path.Combine(temp, "notes.pdf");
                var merged = await RunApp(pdf, new[] { "--merge", combined }.Concat(parts), 120, token).ConfigureAwait(false);
                if (merged.ExitCode != 0 || !File.Exists(combined)) return Error(Limit(string.IsNullOrWhiteSpace(merged.StandardOutput) ? "KillerPDF could not combine the notes" : merged.StandardOutput.Trim()));
            }
            await PublishPdf(combined, output!, token).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { output, notes = parts.Count }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Error(Limit(ex.Message)); }
        finally { await DeleteAsync(temp).ConfigureAwait(false); }
    }

    // A report scans exactly what a direct scan would: the same scope rule, settled before any probe.
    private static async Task<(JsonElement Root, AppCallResult? Error, string Target)> Scan(string path, string? target, bool full, int limit, CancellationToken token)
    {
        var (scope, problem) = await KillerScanAdapter.ResolveScopeAsync(path, string.IsNullOrWhiteSpace(target) ? null : target, token).ConfigureAwait(false);
        if (scope is null) return (default, Error(problem!), string.Empty);
        var (root, error) = await JsonCommand(path, KillerScanAdapter.ScanArguments(scope, full, limit), "KillerScan", 90, token).ConfigureAwait(false);
        return (root, error, scope.Target);
    }

    private static Task<(JsonElement Root, AppCallResult? Error)> Agenda(string path, string date, int days, int limit, CancellationToken token) => JsonCommand(path, ["agenda", date, days.ToString(), "--limit", limit.ToString()], "Killendar", 30, token);
    private static Task<(JsonElement Root, AppCallResult? Error)> DirectoryList(string path, string directory, int limit, CancellationToken token) => JsonCommand(path, ["list", directory, "--limit", limit.ToString()], "KillerShell", 30, token);

    private static async Task<(JsonElement Root, AppCallResult? Error)> JsonCommand(string path, IEnumerable<string> args, string app, int seconds, CancellationToken token)
    {
        var response = await RunApp(path, args, seconds, token).ConfigureAwait(false);
        if (response.ExitCode != 0 || response.OutputExceeded) return (default, ProcessError(app, response));
        try { using var document = JsonDocument.Parse(response.StandardOutput); return (document.RootElement.Clone(), null); }
        catch (JsonException) { return (default, Error(app + " returned invalid JSON")); }
    }

    // Reports and exported text can run past what a command line holds, so long text goes to
    // KillerNotes in a file when it accepts one.
    private static async Task<AppCallResult> CreateNote(string notes, string title, string content, string? group, CancellationToken token)
    {
        if (NotesContent.AvailabilityRefusal(notes) is { } unavailable) return Error(unavailable);
        var args = new List<string> { "create", "--title", title };
        if (!string.IsNullOrWhiteSpace(group)) args.AddRange(["--group", group!]);
        var (contentArguments, folder, problem) = await NotesContent.ArgumentsAsync(notes, content, token, args).ConfigureAwait(false);
        if (problem is not null) return Error(problem);
        try
        {
            args.AddRange(contentArguments);
            var result = await RunNotes(notes, args, token).ConfigureAwait(false);
            return result.ExitCode == 0 && !result.OutputExceeded ? Ok(result.StandardOutput.Trim()) : ProcessError("KillerNotes", result);
        }
        finally { NotesContent.Delete(folder); }
    }

    private static async Task<AppCallResult> RenderPdf(string html, string output, CancellationToken token, IntegrationPdfOptions? options = null)
    {
        if (!NewPdf(output)) return Error("Output must be a new absolute PDF path in an existing folder");
        var edge = options?.EdgePath ?? FindEdge();
        if (edge is null) return Error("Microsoft Edge is required to create PDFs");
        var temp = Path.Combine(options?.TemporaryRoot ?? Path.GetTempPath(), "killermcp-integration-" + Guid.NewGuid().ToString("N"));
        var timeout = options?.Timeout ?? TimeSpan.FromSeconds(90);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            CreateTemporaryFolder(temp);
            var source = Path.Combine(temp, "report.html");
            var rendered = Path.Combine(temp, "report.pdf");
            await File.WriteAllTextAsync(source, Harden(html), new UTF8Encoding(false), deadline.Token).ConfigureAwait(false);
            // A private profile keeps browser data separate. The resolver rule fails every host name
            // lookup, and the dead proxy, with loopback taken out of its bypass list, fails every
            // request to an address, so nothing in the page can reach a server.
            var result = await ProcessRunner.RunAsync(edge,
            [
                "--headless", "--disable-gpu", "--no-pdf-header-footer", "--user-data-dir=" + Path.Combine(temp, "profile"),
                "--host-resolver-rules=MAP * ~NOTFOUND", "--proxy-server=http://127.0.0.1:9", "--proxy-bypass-list=<-loopback>",
                "--disable-background-networking", "--disable-component-update", "--disable-extensions", "--disable-sync", "--no-first-run",
                "--print-to-pdf=" + rendered, new Uri(source).AbsoluteUri,
            ], timeout, 32768, deadline.Token, environment: new Dictionary<string, string?> { ["__COMPAT_LAYER"] = null }).ConfigureAwait(false);
            if (result.ExitCode != 0) return ProcessError("PDF creation", result);
            await WaitForPdf(rendered, deadline.Token).ConfigureAwait(false);
            await PublishPdf(rendered, output, deadline.Token, options?.CopyPdf).ConfigureAwait(false);
            return Ok(JsonSerializer.Serialize(new { output }));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Error("PDF creation timed out"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException) { return Error(Limit(ex.Message)); }
        finally { await DeleteAsync(temp).ConfigureAwait(false); }
    }

    internal static async Task PublishPdf(string source, string output, CancellationToken token, Func<Stream, Stream, CancellationToken, Task>? copy = null)
    {
        var sibling = Path.Combine(Path.GetDirectoryName(output)!, ".killermcp-pdf-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            await using (var destination = new FileStream(sibling, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (copy is null) await input.CopyToAsync(destination, token).ConfigureAwait(false);
                else await copy(input, destination, token).ConfigureAwait(false);
                await destination.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            // Both paths share a directory, so this publishes a completed file in one move.
            // A destination created during rendering is preserved by the non-overwriting move.
            File.Move(sibling, output, false);
        }
        finally
        {
            try { File.Delete(sibling); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task WaitForPdf(string path, CancellationToken token)
    {
        (long Length, DateTime Modified)? previous = null;
        var stable = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                // The launcher can exit while another browser process is still writing the PDF.
                // An exclusive read, a finished trailer and an unchanged file are all required.
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                var current = (file.Length, File.GetLastWriteTimeUtc(path));
                if (previous != current) { previous = current; stable.Restart(); }
                if (file.Length >= 12 && stable.Elapsed >= TimeSpan.FromMilliseconds(500))
                {
                    var header = new byte[5];
                    file.ReadExactly(header);
                    var tail = new byte[(int)Math.Min(file.Length, 1024)];
                    file.Seek(-tail.Length, SeekOrigin.End);
                    file.ReadExactly(tail);
                    if (Encoding.ASCII.GetString(header) == "%PDF-" && Encoding.ASCII.GetString(tail).TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal)) return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { previous = null; stable.Restart(); }
            await Task.Delay(100, token).ConfigureAwait(false);
        }
    }

    // Notes and agent content are untrusted HTML. The policy comes first in the document, ahead of
    // anything the content could use to slip it. Base and meta elements are disabled because the
    // policy cannot stop a base or a refresh from pointing the page elsewhere, including a file
    // share. A browser starts one of these elements only at a less-than sign followed by the tag
    // name and a space, slash or greater-than sign, whatever the attributes say or encode. Renaming
    // the tag to an unknown x-base or x-meta element there disables it wherever it appears, including
    // in nested or split markup, and removes no text, so it cannot join the pieces around it into a new
    // element. A real tag renamed this way shows nothing; the same letters inside an attribute value,
    // a comment or text such as a textarea read x-base or x-meta instead. The policy element added here
    // supplies the character set.
    internal static string Harden(string html)
    {
        var body = LeadingDoctype().Replace(BaseOrMeta().Replace(html, "<x-"), string.Empty, 1);
        return "<!doctype html><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"" + ContentPolicy + "\">" + body;
    }

    private const string ContentPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:";

    [System.Text.RegularExpressions.GeneratedRegex(@"<(?=[Bb][Aa][Ss][Ee](?:[\t\n\f\r />]|\z)|[Mm][Ee][Tt][Aa](?:[\t\n\f\r />]|\z))", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex BaseOrMeta();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[^<]*<!doctype[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex LeadingDoctype();

    private static string JsonReport(string title, JsonElement root)
    {
        var rows = Rows(root);
        return Document(title, $"<p>{rows.Length} result(s)</p>{JsonTable(rows)}");
    }

    private static readonly (string Key, string Label)[] ScanColumns =
    [
        ("IpAddress", "IP address"), ("Hostname", "Hostname"), ("MacAddress", "MAC address"),
        ("Vendor", "Vendor"), ("DeviceType", "Type"), ("OpenPortsDisplay", "Open ports")
    ];

    private static string ScanScope(string? target) => string.IsNullOrWhiteSpace(target) ? "Local network" : target!;
    private static string DeviceCount(int count) => $"{count} {(count == 1 ? "device" : "devices")} found.";

    private static string ScanReport(JsonElement root, string? target)
    {
        var rows = Rows(root);
        return Document("KillerScan Network Report", $"<p>Scan target: {WebUtility.HtmlEncode(ScanScope(target))}</p><p>{DeviceCount(rows.Length)}</p><h2>Devices</h2>{ScanTable(rows)}");
    }

    private static string MarkdownScanReport(JsonElement root, string? target)
    {
        var rows = Rows(root);
        var text = new StringBuilder();
        text.Append("Scan target: ").AppendLine(MarkdownCell(ScanScope(target))).AppendLine();
        text.AppendLine(DeviceCount(rows.Length)).AppendLine();
        text.Append('|');
        foreach (var column in ScanColumns) text.Append(' ').Append(column.Label).Append(" |");
        text.AppendLine();
        text.Append('|');
        foreach (var _ in ScanColumns) text.Append(" --- |");
        text.AppendLine();
        foreach (var row in rows)
        {
            text.Append('|');
            foreach (var column in ScanColumns)
                text.Append(' ').Append(MarkdownCell(row.ValueKind == JsonValueKind.Object && row.TryGetProperty(column.Key, out var value) ? Display(value) : string.Empty)).Append(" |");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string MarkdownCell(string value) => value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string ScanTable(JsonElement[] rows)
    {
        var columns = ScanColumns;
        var table = new StringBuilder("<table><thead><tr>");
        foreach (var column in columns) table.Append("<th>").Append(column.Label).Append("</th>");
        table.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            table.Append("<tr>");
            foreach (var column in columns)
                table.Append("<td>").Append(WebUtility.HtmlEncode(row.ValueKind == JsonValueKind.Object && row.TryGetProperty(column.Key, out var value) ? Display(value) : string.Empty)).Append("</td>");
            table.Append("</tr>");
        }
        table.Append("</tbody></table>");
        return table.ToString();
    }

    private static string JsonTable(JsonElement[] rows)
    {
        var keys = rows.Where(row => row.ValueKind == JsonValueKind.Object).SelectMany(row => row.EnumerateObject().Select(property => property.Name)).Distinct(StringComparer.Ordinal).Take(12).ToArray();
        var table = new StringBuilder("<table><thead><tr>");
        foreach (var key in keys) table.Append("<th>").Append(WebUtility.HtmlEncode(key)).Append("</th>");
        table.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            table.Append("<tr>");
            foreach (var key in keys) table.Append("<td>").Append(WebUtility.HtmlEncode(row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) ? Display(value) : string.Empty)).Append("</td>");
            table.Append("</tr>");
        }
        table.Append("</tbody></table>");
        return table.ToString();
    }

    private static JsonElement[] Rows(JsonElement root) => root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array ? entries.EnumerateArray().ToArray() : [root];

    private static string MarkdownReport(JsonElement root)
    {
        var rows = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToArray() : root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array ? entries.EnumerateArray().ToArray() : [root];
        var text = new StringBuilder();
        foreach (var row in rows) { text.AppendLine("## Item"); if (row.ValueKind == JsonValueKind.Object) foreach (var property in row.EnumerateObject()) text.Append("- **").Append(property.Name).Append(":** ").AppendLine(Display(property.Value)); else text.AppendLine(Display(row)); text.AppendLine(); }
        return text.ToString();
    }

    private static string Document(string title, string body) => "<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:\"><title>" + WebUtility.HtmlEncode(title) + "</title><style>@page{margin:18mm}body{font:14px Arial,sans-serif;color:#111}h1{font-size:24px}table{border-collapse:collapse;width:100%;font-size:11px}th,td{border:1px solid #999;padding:5px;text-align:left;vertical-align:top}th{background:#eee}pre{white-space:pre-wrap;word-break:break-word}</style></head><body><h1>" + WebUtility.HtmlEncode(title) + "</h1>" + body + "</body></html>";
    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private static Task<ProcessResult> RunApp(string path, IEnumerable<string> arguments, int seconds, CancellationToken token)
    {
        IEnumerable<string> prefix = path.EndsWith("KillerNotes.exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith("Killendar.exe", StringComparison.OrdinalIgnoreCase) ? ["--cli"] : [];
        return ProcessRunner.RunAsync(path, prefix.Concat(arguments), TimeSpan.FromSeconds(seconds), 1_048_576, token);
    }

    private static async Task<ProcessResult> RunNotes(string notes, IEnumerable<string> arguments, CancellationToken token)
    {
        if (NotesContent.AvailabilityRefusal(notes) is { } unavailable) return new ProcessResult(1, "", unavailable, false);
        token.ThrowIfCancellationRequested();
        try { return await RunApp(notes, arguments, 40, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        { return new ProcessResult(3, "", "KillerNotes outcome unknown. Check the requested note or output before retrying; the command may already have run.", false); }
    }

    private static bool IdOutput(JsonElement input, out int id, out string? output) { id = 0; output = null; return Exact(input, "id", "output") && input.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out id) && id > 0 && Text(input, "output", 1024, out output) && NewPdf(output!); }
    private static bool ScanInput(JsonElement input, bool needsOutput, out string? target, out bool full, out int limit, out string? output, out string? group) { target = output = group = null; full = false; limit = 100; if (!Exact(input, needsOutput ? ["target", "full", "limit", "output"] : ["target", "full", "limit", "group"])) return false; if (input.TryGetProperty("target", out var t) && (t.ValueKind != JsonValueKind.String || !ValidScanTarget(target = t.GetString()))) return false; if (input.TryGetProperty("full", out var f)) { if (f.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false; full = f.GetBoolean(); } if (!OptionalInt(input, "limit", 1, 100, ref limit)) return false; if (needsOutput && (!Text(input, "output", 1024, out output) || !NewPdf(output!))) return false; if (!needsOutput && input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return false; return true; }
    private static bool AgendaInput(JsonElement input, bool needsOutput, out string? date, out int days, out int limit, out string? output, out string? group) { date = output = group = null; days = 7; limit = 50; if (!Exact(input, needsOutput ? ["date", "days", "limit", "output"] : ["date", "days", "limit", "group"]) || !Text(input, "date", 10, out date) || !DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) return false; if (!OptionalInt(input, "days", 1, 31, ref days) || !OptionalInt(input, "limit", 1, 100, ref limit)) return false; if (needsOutput && (!Text(input, "output", 1024, out output) || !NewPdf(output!))) return false; if (!needsOutput && input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return false; return true; }
    private static bool DirectoryInput(JsonElement input, bool needsOutput, out string? path, out int limit, out string? output, out string? group) { path = output = group = null; limit = 100; if (!Exact(input, needsOutput ? ["path", "limit", "output"] : ["path", "limit", "group"]) || !Text(input, "path", 1024, out path) || !Path.IsPathFullyQualified(path!) || !Directory.Exists(path) || !OptionalInt(input, "limit", 1, 100, ref limit)) return false; if (needsOutput && (!Text(input, "output", 1024, out output) || !NewPdf(output!))) return false; if (!needsOutput && input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return false; return true; }
    private static bool OptionalInt(JsonElement input, string name, int min, int max, ref int value) => !input.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out value) && value >= min && value <= max;
    private static bool ValidScanTarget(string? value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 43) return false; var parts = value.Split('/'); if (parts.Length is < 1 or > 2 || !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false; return parts.Length == 1 || int.TryParse(parts[1], out var prefix) && prefix is >= 22 and <= 32; }
    private static bool Text(JsonElement input, string name, int max, out string? value) { value = null; return input.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = item.GetString()) && value.Length <= max; }
    // Match the KillerPDF adapter's input limit for files produced by larger workflows.
    private const long MaximumInputBytes = 512L * 1024 * 1024;
    private static string? TooLarge(JsonElement input, string name) { try { return Text(input, name, 1024, out var path) && Path.IsPathFullyQualified(path!) && new FileInfo(path!) is { Exists: true } info && info.Length > MaximumInputBytes ? "The PDF is larger than the 512 MiB KillerPDF accepts" : null; } catch { return null; } }
    private static bool ExistingFile(JsonElement input, string name, string extension, out string? path) { path = null; if (!Text(input, name, 1024, out path) || !Path.IsPathFullyQualified(path!) || !path!.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false; try { var info = new FileInfo(path); return info.Exists && info.Length <= MaximumInputBytes; } catch { return false; } }
    private static bool Exact(JsonElement input, params string[] allowed) => input.ValueKind == JsonValueKind.Object && input.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal));
    private static bool NewPdf(string path) { try { return Path.IsPathFullyQualified(path) && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.GetDirectoryName(path)) && !File.Exists(path) && !Directory.Exists(path); } catch { return false; } }
    private static bool IsFile(string? path) { try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); } catch { return false; } }
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "killermcp-integration-" + Guid.NewGuid().ToString("N"));
    private static string? FindEdge() => new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Select(root => Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists);
    private static async Task DeleteAsync(string path)
    {
        if (TemporaryLeases.TryRemove(path, out var lease)) lease.Dispose();
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                if (HasReparsePoint(path)) return;
                Directory.Delete(path, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (elapsed.Elapsed >= TimeSpan.FromSeconds(3)) return;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private static bool HasReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return true;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
            if ((attributes & FileAttributes.Directory) != 0 && HasReparsePoint(entry)) return true;
        }
        return false;
    }
    private static AppCallResult ProcessError(string app, ProcessResult result) => Error(Limit(string.IsNullOrWhiteSpace(result.StandardError) ? app + " command failed" : result.StandardError.Trim()));
    private static AppCallResult Ok(string text) => new(text);
    private static AppCallResult Error(string text) => new(text, true);
    private static string Limit(string text) => text.Length <= 1024 ? text : text[..1024];

    private static JsonObject ContentPdfSchema() => Schema(new() { ["content"] = String(1_000_000), ["format"] = Enum("html", "text"), ["title"] = String(240), ["output"] = PathValue("New absolute PDF path") }, "content", "output");
    private static JsonObject IdOutputSchema() => Schema(new() { ["id"] = Integer(1, int.MaxValue), ["output"] = PathValue("New absolute PDF path") }, "id", "output");
    private static JsonObject ScanOutputSchema() => Schema(new() { ["target"] = String(64), ["full"] = Boolean(false), ["limit"] = Integer(1, 100, 100), ["output"] = PathValue("New absolute PDF path") }, "output");
    private static JsonObject ScanNoteSchema() => Schema(new() { ["target"] = String(64), ["full"] = Boolean(false), ["limit"] = Integer(1, 100, 100), ["group"] = String(240) });
    private static JsonObject AgendaOutputSchema() => Schema(new() { ["date"] = String(10), ["days"] = Integer(1, 31, 7), ["limit"] = Integer(1, 100, 50), ["output"] = PathValue("New absolute PDF path") }, "date", "output");
    private static JsonObject AgendaNoteSchema() => Schema(new() { ["date"] = String(10), ["days"] = Integer(1, 31, 7), ["limit"] = Integer(1, 100, 50), ["group"] = String(240) }, "date");
    private static JsonObject DirectoryOutputSchema() => Schema(new() { ["path"] = PathValue("Existing absolute directory"), ["limit"] = Integer(1, 100, 100), ["output"] = PathValue("New absolute PDF path") }, "path", "output");
    private static JsonObject DirectoryNoteSchema() => Schema(new() { ["path"] = PathValue("Existing absolute directory"), ["limit"] = Integer(1, 100, 100), ["group"] = String(240) }, "path");
    private static JsonObject PdfReportNoteSchema() => Schema(new() { ["path"] = PathValue("Existing absolute PDF path"), ["report"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(PdfReports.Select(item => JsonValue.Create(item.Name)).ToArray()) }, ["title"] = String(240), ["group"] = String(240) }, "path", "report");
    private static JsonObject NotesPdfSchema() => Schema(new() { ["ids"] = new JsonObject { ["type"] = "array", ["items"] = Integer(1, int.MaxValue), ["minItems"] = 1, ["maxItems"] = 50 }, ["output"] = PathValue("New absolute PDF path") }, "ids", "output");
    private static JsonObject PdfTextNoteSchema() => Schema(new() { ["path"] = PathValue("Existing absolute PDF path"), ["pages"] = String(200), ["title"] = String(240), ["group"] = String(240) }, "path");
    private static JsonObject PdfNotesSchema() => Schema(new() { ["path"] = PathValue("Existing absolute PDF path"), ["pages"] = String(200), ["dpi"] = Integer(72, 600, 150), ["group"] = String(240) }, "path");
    private static JsonObject Schema(JsonObject properties, params string[] required) { var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false }; if (required.Length > 0) schema["required"] = new JsonArray(required.Select(value => JsonValue.Create(value)).ToArray()); return schema; }
    private static JsonObject String(int max) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = max };
    private static JsonObject PathValue(string description) => new() { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 1024, ["description"] = description };
    private static JsonObject Integer(int min, int max, int fallback) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max, ["default"] = fallback };
    private static JsonObject Integer(int min, int max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Boolean(bool fallback) => new() { ["type"] = "boolean", ["default"] = fallback };
    [System.Text.RegularExpressions.GeneratedRegex("^[0-9]+(?:-[0-9]+)?(?:,[0-9]+(?:-[0-9]+)?)*\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant)] private static partial System.Text.RegularExpressions.Regex PageList();
    private static JsonObject Enum(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray()), ["default"] = values[0] };
}
