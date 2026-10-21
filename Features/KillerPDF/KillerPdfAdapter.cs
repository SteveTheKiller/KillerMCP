using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KillerMCP;

public static partial class KillerPdfAdapter
{
    private const long MaxPdfBytes = 64L * 1024 * 1024;
    private const int MessageLimit = 65536;

    // Passwords on a command line can be read by any program running as the same user and are kept in
    // process-creation logs, so KillerPDF gets them on standard input when its help says it accepts them
    // there. The option names are string instances of their own, found by reference, so text a user
    // passes that happens to read "--password" is never taken for one.
    private static readonly string PasswordOption = Secret("--password");
    private static readonly string UserPasswordOption = Secret("--user-password");
    private static readonly string OwnerPasswordOption = Secret("--owner-password");
    private static readonly string PfxPasswordOption = Secret("--pfx-password");
    private static readonly string[] SecretOptions = [PasswordOption, UserPasswordOption, OwnerPasswordOption, PfxPasswordOption];
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> SecretsOnInput = new(StringComparer.OrdinalIgnoreCase);

    private static string Secret(string name) => new(name.AsSpan());

    private static string? MoveSecretsToInput(List<string> args)
    {
        var secrets = new JsonObject();
        for (var index = 0; index < args.Count - 1;)
        {
            var option = Array.Find(SecretOptions, secret => ReferenceEquals(secret, args[index]));
            if (option is null) { index++; continue; }
            secrets[option[2..]] = args[index + 1];
            args.RemoveRange(index, 2);
        }
        if (secrets.Count == 0) return null;
        args.Add("--secrets-stdin");
        return secrets.ToJsonString();
    }

    public static async Task<IReadOnlyList<AppAdapter>> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path)) return [];
        var help = await Run(path!, ["--help"], 8, 65536, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0) return [];
        var current = await VersionAsync(path!, cancellationToken).ConfigureAwait(false) is { } version && version >= CurrentVersion;
        SecretsOnInput[path!] = current;

        var adapters = new List<AppAdapter>();
        if (KillerPdfCapabilities.Supports(help.StandardOutput, "--merge <out.pdf>")) adapters.Add(new AppAdapter(MergeTool(), (input, token) => Merge(path!, input, token)));
        foreach (var operation in Operations())
        {
            if (KillerPdfCapabilities.Supports(help.StandardOutput, operation.Marker) && (current ? operation : ForOlderVersion(operation)) is { } definition)
            {
                adapters.Add(new AppAdapter(definition.Tool, (input, token) => RunOperation(path!, definition, input, token)));
            }
        }
        AddReport(help, path!, adapters, "--preflight", "killerpdf_preflight", "Check a local PDF with KillerPDF preflight and return findings without changing the file.", PreflightSchema());
        AddReport(help, path!, adapters, "--accessibility", "killerpdf_accessibility", "Check a local PDF for accessibility findings using KillerPDF without changing the file.", Schema(new JsonObject { ["path"] = PdfPath() }, "path"));
        if (KillerPdfCapabilities.Supports(help.StandardOutput, "--verify | /verify")) adapters.Add(new AppAdapter(VerifyTool(), (input, token) => Verify(path!, input, token)));
        return adapters;
    }

    // KillerPDF 1.9 takes passwords on standard input (--secrets-stdin) and added the split rules and the flatten
    // color and compression options. An older KillerPDF could only get a password on its command line, where other
    // programs and process logs can read it, and rejects those options, so its tools leave them out. A version
    // KillerMCP cannot read counts as older.
    private static readonly Version CurrentVersion = new(1, 9, 0);
    private static readonly string[] PasswordInputs = ["password", "userPassword", "ownerPassword", "pfxPassword"];
    private static readonly Dictionary<string, string[]> NewerOptions = new(StringComparer.Ordinal)
    {
        ["killerpdf_split"] = ["every", "parts", "bookmarkLevel", "maxSize", "nameTemplate"],
        ["killerpdf_flatten"] = ["colorMode", "threshold", "compression", "jpegQuality", "scannedLook"],
    };

    private static async Task<Version?> VersionAsync(string path, CancellationToken token)
    {
        try
        {
            var response = await Run(path, ["--version"], 8, 1024, token).ConfigureAwait(false);
            var match = VersionPattern().Match(response.StandardOutput.Trim());
            return response.ExitCode == 0 && match.Success && Version.TryParse(match.Value, out var version) ? version : null;
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    // The tool as an older KillerPDF can run it, or null when it cannot run without a password input.
    private static Operation? ForOlderVersion(Operation operation)
    {
        var schema = operation.Tool.InputSchema.DeepClone().AsObject();
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"] is JsonArray list ? list.Select(item => item!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) : [];
        if (PasswordInputs.Any(required.Contains)) return null;
        var hadPassword = PasswordInputs.Any(properties.ContainsKey);
        foreach (var name in PasswordInputs.Concat(NewerOptions.TryGetValue(operation.Tool.Name, out var newer) ? newer : [])) properties.Remove(name);
        var description = hadPassword ? operation.Tool.Description + " Password-protected PDFs need KillerPDF 1.9 or later." : operation.Tool.Description;
        return operation with { Tool = operation.Tool with { InputSchema = schema, Description = description } };
    }

    private static IEnumerable<Operation> Operations()
    {
        yield return FileOperation("killerpdf_extract_pages", "--extract-pages <in.pdf>", "Extract selected pages from a local PDF into a new PDF without changing the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["pages"] = PageRange(), ["output"] = NewPdf() }, "path", "pages", "output"),
            (input, output) => ["--extract-pages", String(input, "path"), String(input, "pages"), output]);
        yield return DirectoryOperation("killerpdf_split", "--split <in.pdf>", "Split a local PDF into new PDFs in a new folder, one per page by default, or by page count, equal parts, bookmark level or file size. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["outputFolder"] = NewFolder(), ["every"] = Integer(1, 100000), ["parts"] = Integer(2, 10000), ["bookmarkLevel"] = Integer(1, 6), ["maxSize"] = Text(16, "^[0-9]+(?:KB|MB)?$", "Largest part size such as 500KB or 5MB"), ["nameTemplate"] = Text(256, null, "File names from {name} {index} {first} {last} {title}") }, "path", "outputFolder"),
            (input, output) => Options(["--split", String(input, "path"), output], input, ("every", "--every"), ("parts", "--parts"), ("bookmarkLevel", "--bookmarks"), ("maxSize", "--max-size"), ("nameTemplate", "--name")));
        yield return FileOperation("killerpdf_decrypt", "--decrypt <in.pdf>", "Remove PDF encryption into a new local PDF. Supply a password when the document requires one. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["password"] = Password() }, "path", "output"),
            (input, output) => Append(["--decrypt", String(input, "path"), output], input, "password", PasswordOption));
        yield return DirectoryOperation("killerpdf_render_pages", "--to-image <in.pdf>", "Render selected PDF pages into PNG or JPEG images in a new folder. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["outputFolder"] = NewFolder(), ["pages"] = PageRange(), ["dpi"] = Integer(36, 1200, 150), ["format"] = EnumWithDefault(["png", "jpg"], "png"), ["transparent"] = Boolean(false), ["password"] = Password() }, "path", "outputFolder"),
            (input, output) => RenderArguments(input, output));
        yield return FileOperation("killerpdf_flatten", "--flatten <in.pdf>", "Rasterize a local PDF into a new uneditable PDF, in color, grayscale or black and white, optionally looking printed and scanned. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["dpi"] = Integer(36, 1200, 150), ["password"] = Password(), ["colorMode"] = Choice("color", "grayscale", "black-white"), ["threshold"] = Integer(0, 255), ["compression"] = Choice("lossless", "jpeg"), ["jpegQuality"] = Integer(1, 100), ["scannedLook"] = Text(64, "^(?:all|[a-z-]+(?:,[a-z-]+)*)$", "all, or any of tilt, paper, grain, soft-focus, shadow") }, "path", "output"),
            (input, output) => Options(Append(["--flatten", String(input, "path"), output, "--dpi", Number(input, "dpi", 150)], input, "password", PasswordOption), input, ("colorMode", "--color-mode"), ("threshold", "--threshold"), ("compression", "--compression"), ("jpegQuality", "--jpeg-quality"), ("scannedLook", "--scanned-look")), 300);
        yield return Action("killerpdf_print", "--print <in.pdf>", "Print selected pages from a local PDF. This sends a real print job to the selected or default printer.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["printer"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256 }, ["pages"] = PageRange(), ["copies"] = Integer(1, 99, 1), ["password"] = Password() }, "path"), PrintArguments);
        yield return FileOperation("killerpdf_ocr", "--ocr <in.pdf>", "Create a new searchable PDF with OCR. A language model may be downloaded on first use. Never changes the source.",
            Schema(new JsonObject { ["path"] = PdfPath(), ["output"] = NewPdf(), ["language"] = new JsonObject { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 32, ["pattern"] = "^[A-Za-z0-9_]+$", ["default"] = "eng" }, ["password"] = Password() }, "path", "output"),
            (input, output) => Append(["--ocr", String(input, "path"), output, "--lang", OptionalString(input, "language", "eng")], input, "password", PasswordOption), 600);
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
        foreach (var operation in DocumentOperations()) yield return operation;
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

    private static async Task<AppCallResult> Verify(string path, JsonElement input, CancellationToken token)
    {
        if (!ObjectWith(input)) return Error("Expected no arguments");
        var response = await Run(path, ["--verify"], 120, 65536, token).ConfigureAwait(false);
        if (response.OutputExceeded) return Error("KillerPDF command output exceeded limit");
        var lines = response.StandardOutput.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        // KillerPDF exits 1 with a FAIL line when a payload file does not match; any other failure is an error.
        var passed = response.ExitCode == 0 && lines.Length > 0 && lines[0].StartsWith("PASS:", StringComparison.Ordinal);
        var failed = response.ExitCode == 1 && lines.Length > 0 && lines[0].StartsWith("FAIL:", StringComparison.Ordinal);
        if (!passed && !failed) return ProcessError(response);
        return Ok(JsonSerializer.Serialize(new { passed, summary = Limit(lines[0]), problems = lines.Skip(1).Take(50).Select(Limit).ToArray() }));
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

    // A KillerPDF that cannot take passwords on standard input is never given one on its command line.
    private const string SecretsUnsupported = "This KillerPDF version can only take passwords on its command line, where other programs and process logs can read them, so KillerMCP did not run it. Please update KillerPDF, or run this without a password.";

    private static bool NeedsSecretInput(string path, IEnumerable<string> args) =>
        !(SecretsOnInput.TryGetValue(path, out var supported) && supported) && args.Any(argument => SecretOptions.Any(secret => ReferenceEquals(secret, argument)));

    private static async Task<AppCallResult> RunOperation(string path, Operation definition, JsonElement input, CancellationToken token)
    {
        var problem = ValidateOperation(input, definition);
        if (problem is not null) return Error(problem);
        var stagingParent = definition.Kind == OutputKind.Directory
            ? Path.GetDirectoryName(String(input, "outputFolder"))! : Path.GetTempPath();
        var temporary = Path.Combine(stagingParent, "killermcp-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (definition.Kind == OutputKind.Action)
            {
                var action = await Run(path, definition.Arguments(input, string.Empty), definition.TimeoutSeconds, 262144, token).ConfigureAwait(false);
                if (action.OutputExceeded) return Error("KillerPDF command output exceeded limit");
                if (!definition.SuccessCodes.Contains(action.ExitCode)) return ProcessError(action);
                // A JSON answer is returned whole as data. Text is returned up to a bound, and says when it was cut.
                var text = action.StandardOutput.Trim();
                try
                {
                    using var answer = JsonDocument.Parse(text);
                    if (answer.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        return Ok(new JsonObject { ["completed"] = true, ["result"] = JsonNode.Parse(answer.RootElement.GetRawText()) }.ToJsonString());
                }
                catch (JsonException) { }
                return Ok(JsonSerializer.Serialize(text.Length <= MessageLimit
                    ? (object)new { completed = true, message = text }
                    : new { completed = true, message = text[..MessageLimit], truncated = true, characters = text.Length }));
            }
            if (definition.Kind == OutputKind.Report)
            {
                var report = await Run(path, definition.Arguments(input, string.Empty), definition.TimeoutSeconds, 262144, token).ConfigureAwait(false);
                if (report.OutputExceeded) return Error("KillerPDF command output exceeded limit");
                if (!definition.SuccessCodes.Contains(report.ExitCode)) return ProcessError(report);
                try
                {
                    using var document = JsonDocument.Parse(report.StandardOutput);
                    return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? Ok(document.RootElement.GetRawText()) : Error("KillerPDF returned an invalid report");
                }
                catch (JsonException) { return Error("KillerPDF returned an invalid report"); }
            }
            Directory.CreateDirectory(temporary);
            var staged = definition.Kind == OutputKind.File ? Path.Combine(temporary, "output" + Path.GetExtension(String(input, "output")).ToLowerInvariant()) : Path.Combine(temporary, "output");
            var response = await Run(path, definition.Arguments(input, staged), definition.TimeoutSeconds, 262144, token).ConfigureAwait(false);
            if (response.OutputExceeded) return Error("KillerPDF command output exceeded limit");
            if (!definition.SuccessCodes.Contains(response.ExitCode)) return ProcessError(response);
            if (definition.Tool.Name == "killerpdf_run_macro" && !File.Exists(staged))
            {
                var renamed = Directory.GetFiles(temporary, "*.pdf", SearchOption.TopDirectoryOnly);
                if (renamed.Length == 1) staged = renamed[0];
            }
            if (definition.Kind == OutputKind.File ? !File.Exists(staged) : !Directory.Exists(staged)) return Error("KillerPDF did not create the requested output");
            var destination = definition.Kind == OutputKind.File ? String(input, "output") : String(input, "outputFolder");
            if (definition.Kind == OutputKind.File) File.Copy(staged, destination, false); else Directory.Move(staged, destination);
            var created = definition.Kind == OutputKind.File ? 1 : Directory.EnumerateFileSystemEntries(destination).Count();
            // Whatever KillerPDF printed while it worked, such as a warning that a fallback dropped bookmarks
            // or form fields, goes back with the result.
            var messages = string.Join("\n", new[] { response.StandardOutput.Trim(), response.StandardError.Trim() }.Where(text => text.Length > 0));
            if (messages.Length == 0) return Ok(JsonSerializer.Serialize(new { output = destination, created }));
            return Ok(JsonSerializer.Serialize(messages.Length <= MessageLimit
                ? (object)new { output = destination, created, messages }
                : new { output = destination, created, messages = messages[..MessageLimit], truncated = true }));
        }
        catch (IOException exception) { return Error(DestinationExists(input, definition) ? "Output already exists" : Limit(exception.Message)); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException) { return Error(Limit(exception.Message)); }
        // Run refuses before KillerPDF starts when a password would have to go on its command line.
        catch (InvalidOperationException exception) when (ReferenceEquals(exception.Message, SecretsUnsupported)) { return Error(SecretsUnsupported); }
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
        var pathProblem = definition.InputExtensions.Length == 0 ? null : definition.InputExtensions.SequenceEqual([".pdf"]) ? ValidatePdf(input) : ValidateInput(input, definition.InputExtensions);
        if (pathProblem is not null) return pathProblem;
        foreach (var property in definition.Tool.Name switch
        {
            "killerpdf_split" => new[] { "nameTemplate" },
            "killerpdf_data_merge" => new[] { "name" },
            _ => Array.Empty<string>()
        })
        {
            if (input.TryGetProperty(property, out var template) &&
                (template.ValueKind != JsonValueKind.String || template.GetString() is not { } name ||
                 name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                return "File name templates must not contain parent paths or invalid file name characters";
        }
        if (definition.Kind is OutputKind.File or OutputKind.Directory)
        {
            var name = definition.Kind == OutputKind.File ? "output" : "outputFolder";
            if (!input.TryGetProperty(name, out var destinationValue) || destinationValue.ValueKind != JsonValueKind.String || destinationValue.GetString() is not { } destination || !Path.IsPathFullyQualified(destination) || destination.Length > 1024) return "Output must be an absolute path";
            if (File.Exists(destination) || Directory.Exists(destination)) return "Output already exists";
            if (!Directory.Exists(Path.GetDirectoryName(destination))) return "Output folder does not exist";
            if (definition.Kind == OutputKind.File && !definition.OutputExtensions.Any(extension => destination.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                return definition.OutputExtensions.SequenceEqual([".pdf"]) ? "Output must be a PDF path" : "Output must end with " + string.Join(", ", definition.OutputExtensions);
        }
        var fileProblem = ValidateFiles(input, definition.ExtraFiles) ?? ValidateNewFolders(input, definition.NewFolders);
        if (fileProblem is not null) return fileProblem;
        var valueProblem = ValidateValues(input, definition.Tool.InputSchema);
        if (valueProblem is not null) return valueProblem;
        if (definition.Tool.Name == "killerpdf_bates")
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in Strings(input, "inputs"))
            {
                if (!source.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return "Bates inputs must be PDF files";
                if (!names.Add(Path.GetFileName(source))) return "Bates inputs must have different file names so every stamped PDF is preserved";
            }
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

    private static string? ValidateInput(JsonElement input, string[] extensions)
    {
        if (!input.TryGetProperty("path", out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 1024 } path
            || !Path.IsPathFullyQualified(path) || !extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            return "Path must name an absolute " + string.Join(", ", extensions) + " file";
        try { var info = new FileInfo(path); return !info.Exists || info.Length > MaxPdfBytes ? "Input must be a file no larger than 64 MiB" : null; }
        catch { return "Unable to read input file"; }
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
        return Append(args, input, "password", PasswordOption);
    }

    private static List<string> PrintArguments(JsonElement input)
    {
        var args = new List<string> { "--print", String(input, "path") };
        Append(args, input, "printer", "--printer");
        Append(args, input, "pages", "--pages");
        args.AddRange(["--copies", Number(input, "copies", 1)]);
        return Append(args, input, "password", PasswordOption);
    }

    private static List<string> Append(List<string> args, JsonElement input, string property, string flag)
    {
        if (input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString())) args.AddRange([flag, value.GetString()!]);
        return args;
    }

    private static void AddReport(ProcessResult help, string path, List<AppAdapter> adapters, string command, string name, string description, JsonObject schema)
    {
        if (KillerPdfCapabilities.Supports(help.StandardOutput, $"{command} <in.pdf>")) adapters.Add(new AppAdapter(new RuntimeTool(name, description, schema), (input, token) => Report(path, command, input, token)));
    }

    private static Operation FileOperation(string name, string marker, string description, JsonObject schema, Func<JsonElement, string, List<string>> args, int timeout = 180) => new(marker, OutputKind.File, new RuntimeTool(name, description, schema), args, timeout);
    private static Operation DirectoryOperation(string name, string marker, string description, JsonObject schema, Func<JsonElement, string, List<string>> args, int timeout = 180) => new(marker, OutputKind.Directory, new RuntimeTool(name, description, schema), args, timeout);
    private static Operation Action(string name, string marker, string description, JsonObject schema, Func<JsonElement, List<string>> args, int timeout = 180) => new(marker, OutputKind.Action, new RuntimeTool(name, description, schema), (input, _) => args(input), timeout);
    private static RuntimeTool MergeTool() => new("killerpdf_merge", "Merge two to eight local PDFs with KillerPDF into one new PDF. Use for requests such as \"killer merge these PDFs\". Never replaces an existing output file.", Schema(new JsonObject { ["inputs"] = new JsonObject { ["type"] = "array", ["items"] = PdfPath(), ["minItems"] = 2, ["maxItems"] = 8 }, ["output"] = NewPdf() }, "inputs", "output"));
    private static RuntimeTool VerifyTool() => new("killerpdf_verify_install", "Check every installed KillerPDF program file against the SHA-256 hashes in its payload manifest and report missing, changed, or extra files. Changes nothing.", Schema(new JsonObject()));
    private static JsonObject PreflightSchema() => Schema(new JsonObject { ["path"] = PdfPath(), ["profile"] = Enum("general", "attachments", "print", "general") }, "path");
    private static JsonObject PdfPath() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["pattern"] = "\\.[Pp][Dd][Ff]$", ["description"] = "Absolute path to a local PDF" };
    private static JsonObject NewPdf() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute path for a new PDF that does not already exist" };
    private static JsonObject NewFolder() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute path for a new output folder" };
    private static JsonObject PageRange() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256, ["pattern"] = "^[0-9]+(?:-[0-9]+)?(?:,[0-9]+(?:-[0-9]+)?)*$", ["description"] = "One-based pages such as 1-3,5,9-12" };
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
    private static Task<ProcessResult> Run(string path, IEnumerable<string> args, int seconds, int maximum, CancellationToken token)
    {
        var arguments = args.ToList();
        if (NeedsSecretInput(path, arguments)) throw new InvalidOperationException(SecretsUnsupported);
        var secrets = MoveSecretsToInput(arguments);
        return ProcessRunner.RunAsync(path, arguments, TimeSpan.FromSeconds(seconds), maximum, token, secrets);
    }
    private static AppCallResult Ok(string text) => new(text);
    private static AppCallResult Error(string text) => new(text, true);
    private static AppCallResult ProcessError(ProcessResult response) { var text = string.IsNullOrWhiteSpace(response.StandardError) ? string.IsNullOrWhiteSpace(response.StandardOutput) ? "KillerPDF command failed" : response.StandardOutput : response.StandardError; return Error(Limit(text.Trim())); }
    private static string Limit(string text) => text.Length <= 1024 ? text : text[..1024];
    private static void TryDelete(string path) { try { if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true); } catch { } }

    private sealed record Operation(string Marker, OutputKind Kind, RuntimeTool Tool, Func<JsonElement, string, List<string>> Arguments, int TimeoutSeconds)
    {
        // Extensions accepted for "path"; empty when the tool takes no input document.
        public string[] InputExtensions { get; init; } = [".pdf"];
        public string[] OutputExtensions { get; init; } = [".pdf"];
        // Properties holding absolute paths to other local files the command reads.
        public string[] ExtraFiles { get; init; } = [];
        // Properties holding absolute paths for extra new folders the command writes.
        public string[] NewFolders { get; init; } = [];
        // Exit codes that mean the command ran; reports exit 3 when they have findings.
        public int[] SuccessCodes { get; init; } = [0];
    }
    private enum OutputKind { File, Directory, Action, Report }

    [GeneratedRegex("^[0-9]+(?:-[0-9]+)?(?:,[0-9]+(?:-[0-9]+)?)*\\z", RegexOptions.CultureInvariant)] private static partial Regex PageRangePattern();
    [GeneratedRegex("^[A-Za-z0-9_]+\\z", RegexOptions.CultureInvariant)] private static partial Regex LanguagePattern();
    [GeneratedRegex("^[0-9]{1,5}\\.[0-9]{1,5}\\.[0-9]{1,5}", RegexOptions.CultureInvariant)] private static partial Regex VersionPattern();
}
