using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

public static class IntegrationAdapter
{
    public static IReadOnlyList<AppAdapter> Create(string? notes, string? calendar, string? shell, string? scan, string? pdf)
    {
        var tools = new List<AppAdapter>
        {
            Tool("killer_create_pdf", "Create a new PDF from HTML or plain text. Use this to turn agent output or data from a Killer app into a PDF.", ContentPdfSchema(), CreatePdf),
        };
        if (IsFile(notes)) tools.Add(Tool("killernotes_export_pdf", "Export one KillerNotes note directly to a new PDF.", IdOutputSchema(), (input, token) => NotePdf(notes!, input, token)));
        if (IsFile(scan)) tools.Add(Tool("killerscan_export_report_pdf", "Scan a network and create a PDF report with a device table.", ScanOutputSchema(), (input, token) => ScanPdf(scan!, input, token)));
        if (IsFile(scan) && IsFile(notes)) tools.Add(Tool("killerscan_save_report_note", "Scan a network and save the device report as a new KillerNotes note.", ScanNoteSchema(), (input, token) => ScanNote(scan!, notes!, input, token)));
        if (IsFile(calendar) && AppVersion.IsAtLeast(calendar!, new Version(1, 1, 4))) tools.Add(Tool("killendar_export_agenda_pdf", "Export a Killendar agenda to a new PDF.", AgendaOutputSchema(), (input, token) => AgendaPdf(calendar!, input, token)));
        if (IsFile(calendar) && AppVersion.IsAtLeast(calendar!, new Version(1, 1, 4)) && IsFile(notes)) tools.Add(Tool("killendar_save_agenda_note", "Save a Killendar agenda as a new KillerNotes note.", AgendaNoteSchema(), (input, token) => AgendaNote(calendar!, notes!, input, token)));
        if (IsFile(shell)) tools.Add(Tool("killershell_export_directory_pdf", "Export a KillerShell directory listing to a new PDF.", DirectoryOutputSchema(), (input, token) => DirectoryPdf(shell!, input, token)));
        if (IsFile(shell) && IsFile(notes)) tools.Add(Tool("killershell_save_directory_note", "Save a KillerShell directory listing as a new KillerNotes note.", DirectoryNoteSchema(), (input, token) => DirectoryNote(shell!, notes!, input, token)));
        if (IsFile(pdf) && IsFile(notes)) tools.Add(Tool("killerpdf_save_pages_as_notes", "Render selected KillerPDF pages and save each page image as a new KillerNotes note.", PdfNotesSchema(), (input, token) => PdfPagesToNotes(pdf!, notes!, input, token)));
        return tools;
    }

    private static AppAdapter Tool(string name, string description, JsonObject schema, Func<JsonElement, CancellationToken, Task<AppCallResult>> call) => new(new RuntimeTool(name, description, schema), call);

    private static async Task<AppCallResult> CreatePdf(JsonElement input, CancellationToken token)
    {
        if (!Exact(input, "content", "format", "title", "output") || !Text(input, "content", 1_000_000, out var content) || !Text(input, "output", 1024, out var output) || !NewPdf(output!)) return Error("Provide content and a new absolute PDF output path");
        var format = input.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : "html";
        if (format is not ("html" or "text")) return Error("Format must be html or text");
        var title = input.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "Killer Tools Report" : "Killer Tools Report";
        var body = format == "html" ? content! : $"<pre>{WebUtility.HtmlEncode(content)}</pre>";
        return await RenderPdf(Document(title, body), output!, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> NotePdf(string notes, JsonElement input, CancellationToken token)
    {
        if (!IdOutput(input, out var id, out var output)) return Error("Provide a note ID and a new absolute PDF output path");
        var temp = TempFolder();
        try
        {
            Directory.CreateDirectory(temp);
            var html = Path.Combine(temp, "note.html");
            var response = await RunApp(notes, ["export", "--id", id.ToString(), "--output", html], 30, token).ConfigureAwait(false);
            if (response.ExitCode != 0 || !File.Exists(html)) return ProcessError("KillerNotes", response);
            return await RenderPdf(await File.ReadAllTextAsync(html, token).ConfigureAwait(false), output!, token).ConfigureAwait(false);
        }
        finally { Delete(temp); }
    }

    private static async Task<AppCallResult> ScanPdf(string scan, JsonElement input, CancellationToken token)
    {
        if (!ScanInput(input, true, out var target, out var full, out var limit, out var output, out _)) return Error("Provide valid scan options and a new absolute PDF output path");
        var json = await Scan(scan, target, full, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        return await RenderPdf(ScanReport(json.Root, target), output!, token).ConfigureAwait(false);
    }

    private static async Task<AppCallResult> ScanNote(string scan, string notes, JsonElement input, CancellationToken token)
    {
        if (!ScanInput(input, false, out var target, out var full, out var limit, out _, out var group)) return Error("Provide valid scan options");
        var json = await Scan(scan, target, full, limit, token).ConfigureAwait(false);
        if (json.Error is not null) return json.Error;
        return await CreateNote(notes, "KillerScan network report", MarkdownReport(json.Root), group, token).ConfigureAwait(false);
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
        if (!Exact(input, "path", "pages", "dpi", "group") || !ExistingFile(input, "path", ".pdf", out var path)) return Error("Provide an existing absolute PDF path");
        var pages = input.TryGetProperty("pages", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "1";
        var dpi = input.TryGetProperty("dpi", out var d) && d.TryGetInt32(out var n) ? n : 150;
        if (dpi is < 72 or > 600 || string.IsNullOrWhiteSpace(pages) || pages.Length > 200) return Error("Pages or DPI is invalid");
        var group = input.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
        var temp = TempFolder();
        try
        {
            Directory.CreateDirectory(temp);
            var rendered = await RunApp(pdf, ["--to-image", path!, temp, "--pages", pages!, "--dpi", dpi.ToString(), "--format", "png"], 300, token).ConfigureAwait(false);
            if (rendered.ExitCode != 0) return ProcessError("KillerPDF", rendered);
            var images = Directory.EnumerateFiles(temp, "*.png").OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            if (images.Length == 0) return Error("KillerPDF did not render any pages");
            var created = new JsonArray();
            foreach (var image in images)
            {
                var args = new List<string> { "import-image", "--path", image };
                if (!string.IsNullOrWhiteSpace(group)) args.AddRange(["--group", group!]);
                var result = await RunApp(notes, args, 30, token).ConfigureAwait(false);
                if (result.ExitCode != 0) return ProcessError("KillerNotes", result);
                created.Add(JsonNode.Parse(result.StandardOutput));
            }
            return Ok(new JsonObject { ["created"] = created }.ToJsonString());
        }
        finally { Delete(temp); }
    }

    private static async Task<(JsonElement Root, AppCallResult? Error)> Scan(string path, string? target, bool full, int limit, CancellationToken token)
    {
        var args = new List<string> { "/scan" };
        if (!string.IsNullOrWhiteSpace(target)) args.Add(target!);
        args.AddRange(["/json", "/timeout", "60", "/limit", limit.ToString()]);
        if (!full) args.Add("/quick");
        return await JsonCommand(path, args, "KillerScan", 90, token).ConfigureAwait(false);
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

    private static async Task<AppCallResult> CreateNote(string notes, string title, string content, string? group, CancellationToken token)
    {
        var args = new List<string> { "create", "--title", title, "--content", content };
        if (!string.IsNullOrWhiteSpace(group)) args.AddRange(["--group", group!]);
        var result = await RunApp(notes, args, 30, token).ConfigureAwait(false);
        return result.ExitCode == 0 && !result.OutputExceeded ? Ok(result.StandardOutput.Trim()) : ProcessError("KillerNotes", result);
    }

    private static async Task<AppCallResult> RenderPdf(string html, string output, CancellationToken token)
    {
        if (!NewPdf(output)) return Error("Output must be a new absolute PDF path in an existing folder");
        var edge = FindEdge();
        if (edge is null) return Error("Microsoft Edge is required to create PDFs");
        var temp = TempFolder();
        try
        {
            Directory.CreateDirectory(temp);
            var source = Path.Combine(temp, "report.html");
            await File.WriteAllTextAsync(source, html, new UTF8Encoding(false), token).ConfigureAwait(false);
            var result = await ProcessRunner.RunAsync(edge, ["--headless", "--disable-gpu", "--no-pdf-header-footer", "--print-to-pdf=" + output, new Uri(source).AbsoluteUri], TimeSpan.FromSeconds(90), 32768, token).ConfigureAwait(false);
            return result.ExitCode == 0 && File.Exists(output) ? Ok(JsonSerializer.Serialize(new { output })) : ProcessError("PDF creation", result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException) { return Error(Limit(ex.Message)); }
        finally { Delete(temp); }
    }

    private static string JsonReport(string title, JsonElement root)
    {
        var rows = Rows(root);
        return Document(title, $"<p>{rows.Length} result(s)</p>{JsonTable(rows)}");
    }

    private static string ScanReport(JsonElement root, string? target)
    {
        var rows = Rows(root);
        var scope = string.IsNullOrWhiteSpace(target) ? "Local network" : target;
        return Document("KillerScan Network Report", $"<p>Scan target: {WebUtility.HtmlEncode(scope)}</p><p>{rows.Length} {(rows.Length == 1 ? "device" : "devices")} found.</p><h2>Devices</h2>{ScanTable(rows)}");
    }

    private static string ScanTable(JsonElement[] rows)
    {
        var columns = new (string Key, string Label)[]
        {
            ("IpAddress", "IP address"), ("Hostname", "Hostname"), ("MacAddress", "MAC address"),
            ("Vendor", "Vendor"), ("DeviceType", "Type"), ("OpenPortsDisplay", "Open ports")
        };
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

    private static string Document(string title, string body) => "<!doctype html><html><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(title) + "</title><style>@page{margin:18mm}body{font:14px Arial,sans-serif;color:#111}h1{font-size:24px}table{border-collapse:collapse;width:100%;font-size:11px}th,td{border:1px solid #999;padding:5px;text-align:left;vertical-align:top}th{background:#eee}pre{white-space:pre-wrap;word-break:break-word}</style></head><body><h1>" + WebUtility.HtmlEncode(title) + "</h1>" + body + "</body></html>";
    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private static Task<ProcessResult> RunApp(string path, IEnumerable<string> arguments, int seconds, CancellationToken token)
    {
        IEnumerable<string> prefix = path.EndsWith("KillerNotes.exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith("Killendar.exe", StringComparison.OrdinalIgnoreCase) ? ["--cli"] : [];
        return ProcessRunner.RunAsync(path, prefix.Concat(arguments), TimeSpan.FromSeconds(seconds), 1_048_576, token);
    }

    private static bool IdOutput(JsonElement input, out int id, out string? output) { id = 0; output = null; return Exact(input, "id", "output") && input.TryGetProperty("id", out var value) && value.TryGetInt32(out id) && id > 0 && Text(input, "output", 1024, out output) && NewPdf(output!); }
    private static bool ScanInput(JsonElement input, bool needsOutput, out string? target, out bool full, out int limit, out string? output, out string? group) { target = output = group = null; full = false; limit = 100; if (!Exact(input, needsOutput ? ["target", "full", "limit", "output"] : ["target", "full", "limit", "group"])) return false; if (input.TryGetProperty("target", out var t) && (t.ValueKind != JsonValueKind.String || !ValidScanTarget(target = t.GetString()))) return false; if (input.TryGetProperty("full", out var f)) { if (f.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false; full = f.GetBoolean(); } if (!OptionalInt(input, "limit", 1, 100, ref limit)) return false; if (needsOutput && (!Text(input, "output", 1024, out output) || !NewPdf(output!))) return false; if (!needsOutput && input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return false; return true; }
    private static bool AgendaInput(JsonElement input, bool needsOutput, out string? date, out int days, out int limit, out string? output, out string? group) { date = output = group = null; days = 7; limit = 50; if (!Exact(input, needsOutput ? ["date", "days", "limit", "output"] : ["date", "days", "limit", "group"]) || !Text(input, "date", 10, out date) || !DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) return false; if (!OptionalInt(input, "days", 1, 31, ref days) || !OptionalInt(input, "limit", 1, 100, ref limit)) return false; if (needsOutput && (!Text(input, "output", 1024, out output) || !NewPdf(output!))) return false; if (!needsOutput && input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return false; return true; }
    private static bool DirectoryInput(JsonElement input, bool needsOutput, out string? path, out int limit, out string? output, out string? group) { path = output = group = null; limit = 100; if (!Exact(input, needsOutput ? ["path", "limit", "output"] : ["path", "limit", "group"]) || !Text(input, "path", 1024, out path) || !Path.IsPathFullyQualified(path!) || !Directory.Exists(path) || !OptionalInt(input, "limit", 1, 100, ref limit)) return false; if (needsOutput && (!Text(input, "output", 1024, out output) || !NewPdf(output!))) return false; if (!needsOutput && input.TryGetProperty("group", out var g) && (g.ValueKind != JsonValueKind.String || (group = g.GetString())!.Length > 240)) return false; return true; }
    private static bool OptionalInt(JsonElement input, string name, int min, int max, ref int value) => !input.TryGetProperty(name, out var item) || item.TryGetInt32(out value) && value >= min && value <= max;
    private static bool ValidScanTarget(string? value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 43) return false; var parts = value.Split('/'); if (parts.Length is < 1 or > 2 || !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false; return parts.Length == 1 || int.TryParse(parts[1], out var prefix) && prefix is >= 22 and <= 32; }
    private static bool Text(JsonElement input, string name, int max, out string? value) { value = null; return input.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = item.GetString()) && value.Length <= max; }
    private static bool ExistingFile(JsonElement input, string name, string extension, out string? path) { path = null; return Text(input, name, 1024, out path) && Path.IsPathFullyQualified(path!) && path!.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && File.Exists(path); }
    private static bool Exact(JsonElement input, params string[] allowed) => input.ValueKind == JsonValueKind.Object && input.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal));
    private static bool NewPdf(string path) { try { return Path.IsPathFullyQualified(path) && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.GetDirectoryName(path)) && !File.Exists(path) && !Directory.Exists(path); } catch { return false; } }
    private static bool IsFile(string? path) { try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); } catch { return false; } }
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "killermcp-integration-" + Guid.NewGuid().ToString("N"));
    private static string? FindEdge() => new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Select(root => Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists);
    private static void Delete(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
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
    private static JsonObject PdfNotesSchema() => Schema(new() { ["path"] = PathValue("Existing absolute PDF path"), ["pages"] = String(200), ["dpi"] = Integer(72, 600, 150), ["group"] = String(240) }, "path");
    private static JsonObject Schema(JsonObject properties, params string[] required) { var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false }; if (required.Length > 0) schema["required"] = new JsonArray(required.Select(value => JsonValue.Create(value)).ToArray()); return schema; }
    private static JsonObject String(int max) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = max };
    private static JsonObject PathValue(string description) => new() { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 1024, ["description"] = description };
    private static JsonObject Integer(int min, int max, int fallback) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max, ["default"] = fallback };
    private static JsonObject Integer(int min, int max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Boolean(bool fallback) => new() { ["type"] = "boolean", ["default"] = fallback };
    private static JsonObject Enum(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray()), ["default"] = values[0] };
}
