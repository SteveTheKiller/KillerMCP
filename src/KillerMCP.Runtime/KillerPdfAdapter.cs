using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KillerMCP.Runtime;

public static partial class KillerPdfAdapter
{
    private const long MaxPdfBytes = 64L * 1024 * 1024;

    public static async Task<IReadOnlyList<AppAdapter>> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path)) return [];
        var help = await Run(path!, ["--help"], 8, 65536, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0) return [];

        var adapters = new List<AppAdapter>();
        if (MergeMarker().IsMatch(help.StandardOutput)) adapters.Add(new AppAdapter(MergeTool(), (input, token) => Merge(path!, input, token)));
        foreach (var definition in Operations())
        {
            if (help.StandardOutput.Contains(definition.Marker, StringComparison.Ordinal))
            {
                adapters.Add(new AppAdapter(definition.Tool, (input, token) => RunOperation(path!, definition, input, token)));
            }
        }
        AddReport(help, path!, adapters, "--preflight", "killerpdf_preflight", "Check a local PDF with KillerPDF preflight and return findings without changing the file.", PreflightSchema());
        AddReport(help, path!, adapters, "--accessibility", "killerpdf_accessibility", "Check a local PDF for accessibility findings using KillerPDF without changing the file.", Schema(new JsonObject { ["path"] = PdfPath() }, "path"));
        return adapters;
    }

    private static IEnumerable<Operation> Operations()
    {
        yield return FileOperation("killerpdf_extract_pages", "--extract-pages <in.pdf>", "Extract selected pages from a local PDF into a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["pages"] = PageRange(), ["output"] = NewPdf() }, "path", "pages", "output"),
            (input, output) => ["--extract-pages", String(input, "path"), String(input, "pages"), output]);
        yield return DirectoryOperation("killerpdf_split", "--split <in.pdf>", "Split a local PDF into one new PDF per page in a new folder without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["outputFolder"] = NewFolder() }, "path", "outputFolder"),
            (input, output) => ["--split", String(input, "path"), output]);
        yield return FileOperation("killerpdf_decrypt", "--decrypt <in.pdf>", "Remove PDF encryption into a new local PDF. Supply a password when the document requires one. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["password"] = Password() }, "path", "output"),
            (input, output) => Append(["--decrypt", String(input, "path"), output], input, "password", "--password"));
        yield return DirectoryOperation("killerpdf_render_pages", "--to-image <in.pdf>", "Render selected PDF pages into PNG or JPEG images in a new folder. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["outputFolder"] = NewFolder(), ["pages"] = PageRange(), ["dpi"] = Integer(36, 1200, 150), ["format"] = EnumWithDefault(["png", "jpg"], "png"), ["transparent"] = Boolean(false), ["password"] = Password() }, "path", "outputFolder"),
            (input, output) => RenderArguments(input, output));
        yield return FileOperation("killerpdf_flatten", "--flatten <in.pdf>", "Rasterize a local PDF into a new uneditable PDF. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["dpi"] = Integer(36, 1200, 150), ["password"] = Password() }, "path", "output"),
            (input, output) => Append(["--flatten", String(input, "path"), output, "--dpi", Number(input, "dpi", 150)], input, "password", "--password"));
        yield return Action("killerpdf_print", "--print <in.pdf>", "Print selected pages from a local PDF. This sends a real print job to the selected or default printer.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["printer"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256 }, ["pages"] = PageRange(), ["copies"] = Integer(1, 99, 1), ["password"] = Password() }, "path"), PrintArguments);
        yield return FileOperation("killerpdf_ocr", "--ocr <in.pdf>", "Create a new searchable PDF with OCR. A language model may be downloaded on first use. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["language"] = new JsonObject { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 32, ["pattern"] = "^[A-Za-z0-9_]+$", ["default"] = "eng" }, ["password"] = Password() }, "path", "output"),
            (input, output) => Append(["--ocr", String(input, "path"), output, "--lang", OptionalString(input, "language", "eng")], input, "password", "--password"), 600);
        yield return FileOperation("killerpdf_resave", "--batch-resave <in>", "Resave a local PDF through KillerPDF into a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf() }, "path", "output"),
            (input, output) => ["--batch-resave", String(input, "path"), output, "--quiet"], 300);
        yield return DirectoryOperation("killerpdf_benchmark_render", "--batch-render <in>", "Render the first pages of a local PDF into PNG files with timing data for comparison and diagnostics.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["outputFolder"] = NewFolder(), ["size"] = Integer(16, 8192, 1024), ["pageLimit"] = Integer(1, 100, 1) }, "path", "outputFolder"),
            (input, output) => ["--batch-render", String(input, "path"), output, "--size", Number(input, "size", 1024), "--pages", Number(input, "pageLimit", 1), "--quiet"], 600);
        yield return FileOperation("killerpdf_rotate_pages", "--rotate-pages <in.pdf>", "Rotate selected pages clockwise into a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["pages"] = PageRange(), ["degrees"] = Enum(90, 180, 270), ["output"] = NewPdf() }, "path", "pages", "degrees", "output"),
            (input, output) => ["--rotate-pages", String(input, "path"), String(input, "pages"), Number(input, "degrees"), output]);
        yield return FileOperation("killerpdf_delete_pages", "--delete-pages <in.pdf>", "Remove selected pages into a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["pages"] = PageRange(), ["output"] = NewPdf() }, "path", "pages", "output"),
            (input, output) => ["--delete-pages", String(input, "path"), String(input, "pages"), output]);
        yield return FileOperation("killerpdf_move_pages", "--move-pages <in.pdf>", "Move selected pages before a one-based position in a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["pages"] = PageRange(), ["position"] = Integer(1, 100000), ["output"] = NewPdf() }, "path", "pages", "position", "output"),
            (input, output) => ["--move-pages", String(input, "path"), String(input, "pages"), Number(input, "position"), output]);
        yield return FileOperation("killerpdf_insert_blank_page", "--insert-blank <in.pdf>", "Insert a blank page before a one-based position in a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["position"] = Integer(1, 100000), ["width"] = NumberSchema(0, 14400, 612), ["height"] = NumberSchema(0, 14400, 792), ["output"] = NewPdf() }, "path", "position", "output"),
            (input, output) => ["--insert-blank", String(input, "path"), Number(input, "position"), output, "--width", Number(input, "width", 612), "--height", Number(input, "height", 792)]);
        yield return FileOperation("killerpdf_duplicate_page", "--duplicate-page <in.pdf>", "Duplicate one page in a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["page"] = Integer(1, 100000), ["output"] = NewPdf() }, "path", "page", "output"),
            (input, output) => ["--duplicate-page", String(input, "path"), Number(input, "page"), output]);
        yield return Action("killerpdf_document_info", "--document-info <in.pdf>", "Read page count, PDF version, metadata, language, and dates from a local PDF without changing it.",
            Schema(new JsonObject { ["path"] = PdfPath() }, "path"), input => ["--document-info", String(input, "path")]);
        yield return Action("killerpdf_search_text", "--search-text <in.pdf>", "Search text in a local PDF and return the matching page numbers without changing it.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["query"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 512 } }, "path", "query"),
            input => ["--search-text", String(input, "path"), String(input, "query")]);
    }

    private static async Task<AppCallResult> Report(string path, string command, JsonElement input, CancellationToken token)
    {
        var problem = ValidateReport(input, command);
        if (problem is not null) return Error(problem);
        var args = new List<string> { command, String(input, "path"), "--json" };
        if (command == "--preflight") args.AddRange(["--profile", OptionalString(input, "profile", "general")]);
        var response = await Run(path, args, 45, 262144, token).ConfigureAwait(false);
        if (response.OutputExceeded) return Error("KillerPDF command output exceeded limit");
        if (response.ExitCode is not 0 and not 3) return ProcessError(response);
        try
        {
            using var document = JsonDocument.Parse(response.StandardOutput);
            return document.RootElement.ValueKind == JsonValueKind.Object && !response.OutputExceeded ? Ok(document.RootElement.GetRawText()) : Error("KillerPDF returned an invalid report");
        }
        catch (JsonException) { return Error("KillerPDF returned an invalid report"); }
    }

    private static async Task<AppCallResult> Merge(string path, JsonElement input, CancellationToken token)
    {
        var problem = ValidateMerge(input);
        if (problem is not null) return Error(problem);
        var temporary = Path.Combine(Path.GetTempPath(), "killermcp-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporary);
            var staged = Path.Combine(temporary, "merged.pdf");
            var inputs = input.GetProperty("inputs").EnumerateArray().Select(item => item.GetString()!).ToArray();
            var response = await Run(path, new[] { "--merge", staged }.Concat(inputs), 120, 8192, token).ConfigureAwait(false);
            if (response.OutputExceeded) return Error("KillerPDF command output exceeded limit");
            if (response.ExitCode != 0) return ProcessError(response);
            if (!File.Exists(staged)) return Error("KillerPDF did not create the merged PDF");
            var output = String(input, "output");
            File.Copy(staged, output, false);
            return Ok(JsonSerializer.Serialize(new { output, inputCount = inputs.Length }));
        }
        catch (IOException exception) { return Error(File.Exists(String(input, "output")) ? "Output already exists" : Limit(exception.Message)); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException) { return Error(Limit(exception.Message)); }
        finally { TryDelete(temporary); }
    }

    private static async Task<AppCallResult> RunOperation(string path, Operation definition, JsonElement input, CancellationToken token)
    {
        var problem = ValidateOperation(input, definition);
        if (problem is not null) return Error(problem);
        var temporary = Path.Combine(Path.GetTempPath(), "killermcp-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (definition.Kind == OutputKind.Action)
            {
                var action = await Run(path, definition.Arguments(input, string.Empty), definition.TimeoutSeconds, 262144, token).ConfigureAwait(false);
                if (action.OutputExceeded) return Error("KillerPDF command output exceeded limit");
                if (action.ExitCode != 0) return ProcessError(action);
                return Ok(JsonSerializer.Serialize(new { completed = true, message = Limit(action.StandardOutput.Trim()) }));
            }
            Directory.CreateDirectory(temporary);
            var staged = definition.Kind == OutputKind.File ? Path.Combine(temporary, "output.pdf") : Path.Combine(temporary, "output");
            var response = await Run(path, definition.Arguments(input, staged), definition.TimeoutSeconds, 262144, token).ConfigureAwait(false);
            if (response.OutputExceeded) return Error("KillerPDF command output exceeded limit");
            if (response.ExitCode != 0) return ProcessError(response);
            if (definition.Kind == OutputKind.File ? !File.Exists(staged) : !Directory.Exists(staged)) return Error("KillerPDF did not create the requested output");
            var destination = definition.Kind == OutputKind.File ? String(input, "output") : String(input, "outputFolder");
            if (definition.Kind == OutputKind.File) File.Copy(staged, destination, false); else Directory.Move(staged, destination);
            var created = definition.Kind == OutputKind.File ? 1 : Directory.EnumerateFileSystemEntries(destination).Count();
            return Ok(JsonSerializer.Serialize(new { output = destination, created }));
        }
        catch (IOException exception) { return Error(DestinationExists(input, definition) ? "Output already exists" : Limit(exception.Message)); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException) { return Error(Limit(exception.Message)); }
        finally { TryDelete(temporary); }
    }

    private static string? ValidateReport(JsonElement input, string command)
    {
        if (!ObjectWith(input, command == "--preflight" ? ["path", "profile"] : ["path"])) return "Expected PDF arguments";
        var pathProblem = ValidatePdf(input);
        if (pathProblem is not null) return pathProblem;
        if (command == "--preflight" && input.TryGetProperty("profile", out var profile) && (profile.ValueKind != JsonValueKind.String || profile.GetString() is not ("general" or "attachments" or "print"))) return "Invalid preflight profile";
        return null;
    }

    private static string? ValidateMerge(JsonElement input)
    {
        if (!ObjectWith(input, "inputs", "output") || !input.TryGetProperty("inputs", out var inputs) || inputs.ValueKind != JsonValueKind.Array || inputs.GetArrayLength() is < 2 or > 8) return "Provide two to eight input PDFs and a new output path";
        if (!input.TryGetProperty("output", out var outputValue) || outputValue.ValueKind != JsonValueKind.String || !ValidAbsolutePdf(outputValue.GetString())) return "Output must be an absolute PDF path";
        var output = outputValue.GetString()!;
        if (!Directory.Exists(Path.GetDirectoryName(output))) return "Output folder does not exist";
        if (File.Exists(output) || Directory.Exists(output)) return "Output already exists";
        long total = 0;
        foreach (var item in inputs.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !ValidAbsolutePdf(item.GetString())) return "Inputs must be absolute PDF paths";
            try
            {
                var info = new FileInfo(item.GetString()!);
                if (!info.Exists || info.Length > MaxPdfBytes) return "Each input must be a PDF no larger than 64 MiB";
                total += info.Length;
            }
            catch { return "Unable to read an input PDF"; }
        }
        return total > 128L * 1024 * 1024 ? "Combined inputs must be no larger than 128 MiB" : null;
    }

    private static string? ValidateOperation(JsonElement input, Operation definition)
    {
        if (!ObjectWith(input, definition.Tool.InputSchema["properties"]!.AsObject().Select(item => item.Key).ToArray()) || !HasRequired(input, definition.Tool.InputSchema)) return "Expected PDF arguments";
        var pathProblem = ValidatePdf(input);
        if (pathProblem is not null) return pathProblem;
        if (definition.Kind != OutputKind.Action)
        {
            var name = definition.Kind == OutputKind.File ? "output" : "outputFolder";
            if (!input.TryGetProperty(name, out var destinationValue) || destinationValue.ValueKind != JsonValueKind.String || destinationValue.GetString() is not { } destination || !Path.IsPathFullyQualified(destination) || destination.Length > 1024) return "Output must be an absolute path";
            if (File.Exists(destination) || Directory.Exists(destination)) return "Output already exists";
            if (!Directory.Exists(Path.GetDirectoryName(destination))) return "Output folder does not exist";
            if (definition.Kind == OutputKind.File && !destination.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return "Output must be a PDF path";
        }
        if (!OptionalPageRange(input, "pages")) return "Invalid page range";
        if (!OptionalInteger(input, "dpi", 36, 1200)) return "DPI must be between 36 and 1200";
        if (!OptionalInteger(input, "copies", 1, 99)) return "Copies must be between 1 and 99";
        if (!OptionalInteger(input, "size", 16, 8192)) return "Render size must be between 16 and 8192";
        if (!OptionalInteger(input, "pageLimit", 1, 100)) return "Page limit must be between 1 and 100";
        if (input.TryGetProperty("degrees", out var degrees) && (degrees.ValueKind != JsonValueKind.Number || !degrees.TryGetInt32(out var degree) || degree is not (90 or 180 or 270))) return "Degrees must be 90, 180, or 270";
        if (!OptionalInteger(input, "position", 1, 100000)) return "Position must be a positive page number";
        if (!OptionalInteger(input, "page", 1, 100000)) return "Page must be a positive number";
        if (!OptionalNumber(input, "width", 0, 14400)) return "Width must be between 0 and 14400 points";
        if (!OptionalNumber(input, "height", 0, 14400)) return "Height must be between 0 and 14400 points";
        if (input.TryGetProperty("query", out var query) && (query.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(query.GetString()) || query.GetString()!.Length > 512)) return "Query must contain text and be no longer than 512 characters";
        if (input.TryGetProperty("password", out var password) && (password.ValueKind != JsonValueKind.String || password.GetString()!.Length > 1024)) return "Invalid password";
        if (input.TryGetProperty("format", out var format) && (format.ValueKind != JsonValueKind.String || format.GetString() is not ("png" or "jpg"))) return "Format must be png or jpg";
        if (input.TryGetProperty("language", out var language) && (language.ValueKind != JsonValueKind.String || language.GetString()!.Length is < 3 or > 32 || !LanguagePattern().IsMatch(language.GetString()!))) return "Invalid OCR language";
        if (input.TryGetProperty("printer", out var printer) && (printer.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(printer.GetString()) || printer.GetString()!.Length > 256)) return "Invalid printer";
        if (input.TryGetProperty("transparent", out var transparent) && transparent.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "Transparent must be true or false";
        return null;
    }

    private static string? ValidatePdf(JsonElement input)
    {
        if (!input.TryGetProperty("path", out var value) || value.ValueKind != JsonValueKind.String || !ValidAbsolutePdf(value.GetString())) return "Path must name an absolute PDF file";
        try { var info = new FileInfo(value.GetString()!); return !info.Exists || info.Length > MaxPdfBytes ? "PDF must be a file no larger than 64 MiB" : null; }
        catch { return "Unable to read PDF file"; }
    }

    private static List<string> RenderArguments(JsonElement input, string output)
    {
        var args = new List<string> { "--to-image", String(input, "path"), output, "--dpi", Number(input, "dpi", 150), "--format", OptionalString(input, "format", "png") };
        Append(args, input, "pages", "--pages");
        if (input.TryGetProperty("transparent", out var transparent) && transparent.ValueKind == JsonValueKind.True) args.Add("--transparent");
        return Append(args, input, "password", "--password");
    }

    private static List<string> PrintArguments(JsonElement input)
    {
        var args = new List<string> { "--print", String(input, "path") };
        Append(args, input, "printer", "--printer");
        Append(args, input, "pages", "--pages");
        args.AddRange(["--copies", Number(input, "copies", 1)]);
        return Append(args, input, "password", "--password");
    }

    private static List<string> Append(List<string> args, JsonElement input, string property, string flag)
    {
        if (input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())) args.AddRange([flag, value.GetString()!]);
        return args;
    }

    private static void AddReport(ProcessResult help, string path, List<AppAdapter> adapters, string command, string name, string description, JsonObject schema)
    {
        if (help.StandardOutput.Contains($"{command} <in.pdf>", StringComparison.Ordinal)) adapters.Add(new AppAdapter(new RuntimeTool(name, description, schema), (input, token) => Report(path, command, input, token)));
    }

    private static Operation FileOperation(string name, string marker, string description, JsonObject schema, Func<JsonElement, string, List<string>> args, int timeout = 180) => new(marker, OutputKind.File, new RuntimeTool(name, description, schema), args, timeout);
    private static Operation DirectoryOperation(string name, string marker, string description, JsonObject schema, Func<JsonElement, string, List<string>> args, int timeout = 180) => new(marker, OutputKind.Directory, new RuntimeTool(name, description, schema), args, timeout);
    private static Operation Action(string name, string marker, string description, JsonObject schema, Func<JsonElement, List<string>> args, int timeout = 180) => new(marker, OutputKind.Action, new RuntimeTool(name, description, schema), (input, _) => args(input), timeout);
    private static RuntimeTool MergeTool() => new("killerpdf_merge", "Merge two to eight local PDFs with KillerPDF into one new PDF. Use for requests such as \"killer merge these PDFs\". Never replaces an existing output file.", Schema(new JsonObject { ["inputs"] = new JsonObject { ["type"] = "array", ["items"] = PdfPath(), ["minItems"] = 2, ["maxItems"] = 8 }, ["output"] = NewPdf() }, "inputs", "output"));
    private static JsonObject PreflightSchema() => Schema(new JsonObject { ["path"] = PdfPath(), ["profile"] = Enum("general", "attachments", "print", "general") }, "path");
    private static JsonObject PdfPath() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute path to a local PDF" };
    private static JsonObject NewPdf() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute path for a new PDF that does not already exist" };
    private static JsonObject NewFolder() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute path for a new output folder" };
    private static JsonObject PageRange() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256, ["pattern"] = "^\\d+(?:-\\d+)?(?:,\\d+(?:-\\d+)?)*$", ["description"] = "One-based pages such as 1-3,5,9-12" };
    private static JsonObject Password() => new() { ["type"] = "string", ["maxLength"] = 1024, ["description"] = "PDF password when required" };
    private static JsonObject Integer(int min, int max, int? defaultValue = null) { var value = new JsonObject { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max }; if (defaultValue.HasValue) value["default"] = defaultValue; return value; }
    private static JsonObject NumberSchema(double exclusiveMin, double max, double defaultValue) => new() { ["type"] = "number", ["exclusiveMinimum"] = exclusiveMin, ["maximum"] = max, ["default"] = defaultValue };
    private static JsonObject Boolean(bool defaultValue) => new() { ["type"] = "boolean", ["default"] = defaultValue };
    private static JsonObject Enum(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray()) };
    private static JsonObject Enum(string first, string second, string third, string defaultValue) => new() { ["type"] = "string", ["enum"] = new JsonArray(first, second, third), ["default"] = defaultValue };
    private static JsonObject EnumWithDefault(string[] values, string defaultValue) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray()), ["default"] = defaultValue };
    private static JsonObject Enum(params int[] values) => new() { ["type"] = "integer", ["enum"] = new JsonArray(values.Select(value => JsonValue.Create(value)).ToArray()) };
    private static JsonObject Schema(JsonObject properties, params string[] required) { var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false }; if (required.Length > 0) schema["required"] = new JsonArray(required.Select(value => JsonValue.Create(value)).ToArray()); return schema; }

    private static bool ObjectWith(JsonElement input, params string[] allowed) => input.ValueKind == JsonValueKind.Object && input.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal));
    private static bool HasRequired(JsonElement input, JsonObject schema) => schema["required"] is not JsonArray required || required.All(item => item is not null && input.TryGetProperty(item.GetValue<string>(), out _));
    private static bool ValidAbsolutePdf(string? path) => !string.IsNullOrEmpty(path) && path.Length <= 1024 && Path.IsPathFullyQualified(path) && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    private static bool OptionalPageRange(JsonElement input, string name) => !input.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 256 && PageRangePattern().IsMatch(value.GetString()!);
    private static bool OptionalInteger(JsonElement input, string name, int min, int max) => !input.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= min && number <= max;
    private static bool OptionalNumber(JsonElement input, string name, double min, double max) => !input.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number > min && number <= max;
    private static bool IsFile(string? path) { try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); } catch { return false; } }
    private static bool DestinationExists(JsonElement input, Operation definition) { var name = definition.Kind == OutputKind.File ? "output" : "outputFolder"; return input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } path && (System.IO.File.Exists(path) || System.IO.Directory.Exists(path)); }
    private static string String(JsonElement input, string name) => input.GetProperty(name).GetString()!;
    private static string OptionalString(JsonElement input, string name, string fallback) => input.TryGetProperty(name, out var value) ? value.GetString()! : fallback;
    private static string Number(JsonElement input, string name, int fallback = 0) => input.TryGetProperty(name, out var value) ? value.GetRawText() : fallback.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string Number(JsonElement input, string name, double fallback) => input.TryGetProperty(name, out var value) ? value.GetRawText() : fallback.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static Task<ProcessResult> Run(string path, IEnumerable<string> args, int seconds, int maximum, CancellationToken token) => ProcessRunner.RunAsync(path, args, TimeSpan.FromSeconds(seconds), maximum, token);
    private static AppCallResult Ok(string text) => new(text);
    private static AppCallResult Error(string text) => new(text, true);
    private static AppCallResult ProcessError(ProcessResult response) { var text = string.IsNullOrWhiteSpace(response.StandardError) ? string.IsNullOrWhiteSpace(response.StandardOutput) ? "KillerPDF command failed" : response.StandardOutput : response.StandardError; return Error(Limit(text.Trim())); }
    private static string Limit(string text) => text.Length <= 1024 ? text : text[..1024];
    private static void TryDelete(string path) { try { if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true); } catch { } }

    private sealed record Operation(string Marker, OutputKind Kind, RuntimeTool Tool, Func<JsonElement, string, List<string>> Arguments, int TimeoutSeconds);
    private enum OutputKind { File, Directory, Action }

    [GeneratedRegex("^\\s*--merge <out\\.pdf>", RegexOptions.Multiline | RegexOptions.CultureInvariant)] private static partial Regex MergeMarker();
    [GeneratedRegex("^\\d+(?:-\\d+)?(?:,\\d+(?:-\\d+)?)*$", RegexOptions.CultureInvariant)] private static partial Regex PageRangePattern();
    [GeneratedRegex("^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)] private static partial Regex LanguagePattern();
}
