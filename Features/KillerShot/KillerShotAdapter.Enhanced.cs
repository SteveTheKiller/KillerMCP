using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KillerMCP;

public static partial class KillerShotAdapter
{
    private static async Task AddEnhancedAsync(string path, string help, List<AppAdapter> adapters, CancellationToken token)
    {
        ProcessResult version;
        try { version = await RunAsync(path, ["version"], TimeSpan.FromSeconds(8), 1024, token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or IOException) { return; }
        if (version.ExitCode != 0 || !Version.TryParse(version.StandardOutput.Trim(), out var installed) || installed < new Version(1, 1, 1)) return;
        if (Advertises(help, "text"))
        {
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_read_text", "Read text from an existing local image with KillerShot's on-device OCR. Returns text without opening links, capturing the screen or changing the clipboard. Requires KillerShot 1.1.1 or later.", PathSchema()),
                (input, cancellation) => ReadImageAsync(path, input, "text", "text", cancellation)));
            if (help.Contains("text --languages", StringComparison.Ordinal))
                adapters.Add(new AppAdapter(new RuntimeTool("killershot_text_languages", "List the installed on-device OCR languages without changing KillerShot's settings. Requires KillerShot 1.1.1 or later.", EmptySchema()),
                    (input, cancellation) => SimpleAsync(path, input, ["text", "--languages", "--json"], "languages", cancellation)));
        }
        if (Advertises(help, "qr"))
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_read_qr", "Read QR codes from an existing local image. Returns their text and link checks without opening links or capturing the screen. Requires KillerShot 1.1.1 or later.", PathSchema()),
                (input, cancellation) => ReadImageAsync(path, input, "qr", "codes", cancellation)));
        if (Advertises(help, "recording"))
        {
            var schema = EmptySchema();
            schema["properties"]!["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("pause", "resume", "stop", "cancel") };
            schema["properties"]!["confirmDiscard"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Required true for cancel, which discards the unfinished recording" };
            schema["required"] = new JsonArray("action");
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_recording_control", "Send pause, resume, stop or cancel to the running KillerShot recording. Stop finishes the file; cancel discards the unfinished recording and requires confirmDiscard=true. The response acknowledges the request. Requires KillerShot 1.1.1 or later.", schema),
                (input, cancellation) => RecordingControlAsync(path, input, cancellation)));
        }
        if (Advertises(help, "edit"))
        {
            var schema = PathSchema();
            var properties = schema["properties"]!.AsObject();
            properties["output"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute new PNG or JPEG path. Existing files are never replaced." };
            properties["trim"] = new JsonObject { ["type"] = "boolean" };
            properties["rotate"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("right", "left", "180") };
            properties["flip"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("horizontal", "vertical") };
            properties["resize"] = new JsonObject { ["type"] = "string", ["maxLength"] = 16, ["pattern"] = "^[1-9][0-9]{0,4}(?:%|x[1-9][0-9]{0,4})?$", ["description"] = "Percentage, width or dimensions, such as 50%, 1280 or 1280x720" };
            properties["margin"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 2000 };
            properties["marginColor"] = new JsonObject { ["type"] = "string", ["pattern"] = "^#[0-9A-Fa-f]{6}$" };
            properties["quality"] = new JsonObject { ["type"] = "integer", ["minimum"] = 10, ["maximum"] = 100 };
            schema["required"] = new JsonArray("path", "output");
            var tool = new RuntimeTool("killershot_edit_image", "Trim, rotate, flip, resize or add a border to an existing local image, saving a new PNG or JPEG. Operations run in that order and never replace existing files. Requires KillerShot 1.1.1 or later.", schema);
            var validation = JsonSerializer.SerializeToElement(schema);
            adapters.Add(new AppAdapter(tool, (input, cancellation) =>
                Services.Mcp.ToolSchema.Validate(input, validation) is { } problem
                    ? Task.FromResult(Error(problem)) : EditImageAsync(path, input, cancellation)));
        }
        int recent = adapters.FindIndex(item => item.Tool.Name == "killershot_recent");
        if (recent >= 0 && Regex.IsMatch(help, @"^\s*--search\s+<words>", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var schema = LimitSchema(20, 20);
            schema["properties"]!["search"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 512, ["description"] = "Words to match in capture titles, programs, notes, collections and recognized text" };
            adapters[recent] = new AppAdapter(new RuntimeTool("killershot_recent", "List up to 20 recent captures, optionally searching their titles, programs, notes, collections and recognized text. Search requires KillerShot 1.1.1 or later. Does not change the library.", schema),
                (input, cancellation) => SearchRecentAsync(path, input, cancellation));
        }
    }

    private static Task<AppCallResult> SearchRecentAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "limit", "search") || !OptionalInteger(input, "limit", 1, 20, 20, out int limit)) return Task.FromResult(Error("Expected an optional limit from 1 to 20 and search text"));
        var arguments = new List<string> { "recent", "--limit", Text(limit) };
        if (input.TryGetProperty("search", out _))
        {
            if (!RequiredString(input, "search", 512, out var search)) return Task.FromResult(Error("Search must contain text and be no longer than 512 characters"));
            arguments.AddRange(["--search", search!]);
        }
        arguments.Add("--json");
        return RunJsonAsync(path, arguments, "items", 30, token);
    }

    private static Task<AppCallResult> RecordingControlAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "action", "confirmDiscard") || !RequiredString(input, "action", 6, out var action)
            || action is not ("pause" or "resume" or "stop" or "cancel") || !OptionalBoolean(input, "confirmDiscard", out bool confirm))
            return Task.FromResult(Error("Expected pause, resume, stop or cancel and optional confirmDiscard"));
        if (action == "cancel" && !confirm) return Task.FromResult(Error("Cancel discards the unfinished recording; confirmDiscard must be true"));
        return RunJsonAsync(path, ["recording", action, "--json"], "sent", 20, token);
    }

    private static bool Advertises(string help, string command) => Regex.IsMatch(help,
        @"^\s*" + Regex.Escape(command) + @"(?=\s|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static Task<AppCallResult> EditImageAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!ImagePath(input, out var image) || !RequiredString(input, "output", 1024, out var output) || !Path.IsPathFullyQualified(output!))
            return Task.FromResult(Error("Expected an existing local image and an absolute new output path"));
        try
        {
            if (Path.GetExtension(output!).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")
                || !Directory.Exists(Path.GetDirectoryName(output!)) || File.Exists(output) || Directory.Exists(output))
                return Task.FromResult(Error("Output must be a new PNG or JPEG path in an existing folder"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return Task.FromResult(Error("Unable to use the output path")); }
        if (input.TryGetProperty("marginColor", out _) && !input.TryGetProperty("margin", out _)) return Task.FromResult(Error("marginColor requires margin"));
        var arguments = new List<string> { "edit", image!, "--out", output! };
        if (input.TryGetProperty("trim", out var trim) && trim.GetBoolean()) arguments.Add("--trim");
        foreach (var (name, option) in new[] { ("rotate", "--rotate"), ("flip", "--flip"), ("resize", "--resize"), ("margin", "--margin"), ("marginColor", "--margin-color"), ("quality", "--quality") })
            if (input.TryGetProperty(name, out var value)) arguments.AddRange([option, value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()]);
        arguments.Add("--json");
        return RunJsonAsync(path, arguments, "path", 120, token);
    }

    private static Task<AppCallResult> ReadImageAsync(string path, JsonElement input, string command, string property, CancellationToken token)
    {
        if (!Allowed(input, "path") || !ImagePath(input, out var image)) return Task.FromResult(Error("Expected the absolute path of an existing image no larger than 64 MiB"));
        return RunJsonAsync(path, [command, image!, "--json"], property, 120, token);
    }

    private static bool ImagePath(JsonElement input, out string? path)
    {
        path = null;
        if (!RequiredString(input, "path", 1024, out var value) || !Path.IsPathFullyQualified(value!)
            || Path.GetExtension(value!).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff")) return false;
        try
        {
            var file = new FileInfo(value!);
            if (!file.Exists || file.Length > 64L * 1024 * 1024) return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
        path = value;
        return true;
    }
}
