using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP;

/// <summary>Screenshot and screen recording tools backed by the KillerShot command line. Each call
/// runs KillerShot.exe --cli in its own process, which never touches a running KillerShot's tray
/// or shortcuts. Captures are saved as files and the result carries their paths; nothing is put
/// on the clipboard unless the call asks for it.</summary>
public static class KillerShotAdapter
{
    private static readonly Version MinimumVersion = new(0, 1, 0);

    public static async Task<IReadOnlyList<AppAdapter>> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path) || !AppVersion.IsAtLeast(path!, MinimumVersion)) return [];

        ProcessResult help;
        try { help = await RunAsync(path!, ["--help"], TimeSpan.FromSeconds(8), 16384, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException) { return []; }
        if (help.ExitCode != 0 || !help.StandardOutput.Contains("KillerShot command line", StringComparison.Ordinal)) return [];

        var adapters = new List<AppAdapter>();
        Add(help, "monitors", adapters,
            new RuntimeTool("killershot_list_monitors", "List the monitors KillerShot can capture, numbered left to right, with their bounds in screen pixels.", EmptySchema()),
            (input, token) => SimpleAsync(path!, input, ["monitors", "--json"], "monitors", token));
        Add(help, "windows [--limit", adapters,
            new RuntimeTool("killershot_list_windows", "List the visible windows KillerShot can capture, topmost first, with their handles, titles, process names and bounds.", LimitSchema(500, 100)),
            (input, token) => WindowsAsync(path!, input, token));
        Add(help, "capture [options]", adapters,
            new RuntimeTool("killershot_capture_monitor", "Take a screenshot of one monitor with KillerShot and return the saved PNG or JPEG path. Omit monitor for the monitor under the pointer.", CaptureSchema([], ("monitor", MonitorProperty()))),
            (input, token) => CaptureAsync(path!, input, "monitor", token));
        Add(help, "capture [options]", adapters,
            new RuntimeTool("killershot_capture_all", "Take a screenshot of every monitor as one image with KillerShot and return the saved PNG or JPEG path.", CaptureSchema([])),
            (input, token) => CaptureAsync(path!, input, "all", token));
        Add(help, "capture [options]", adapters,
            new RuntimeTool("killershot_capture_window", "Take a screenshot of one window with KillerShot, including a covered window, and return the saved PNG or JPEG path. Name the window by handle or by part of its title from killershot_list_windows.", CaptureSchema(["window"], ("window", WindowProperty()))),
            (input, token) => CaptureAsync(path!, input, "window", token));
        Add(help, "capture [options]", adapters,
            new RuntimeTool("killershot_capture_region", "Take a screenshot of one rectangle of the desktop with KillerShot and return the saved PNG or JPEG path. Coordinates are screen pixels, as reported by killershot_list_monitors.", CaptureSchema(["x", "y", "width", "height"], RectProperties())),
            (input, token) => CaptureAsync(path!, input, "region", token));
        Add(help, "record --seconds", adapters,
            new RuntimeTool("killershot_record", "Record a monitor, every monitor, a window or a rectangle to MP4 or GIF with KillerShot for a set number of seconds and return the saved file path. MP4 can include system sound or the microphone.", RecordSchema()),
            (input, token) => RecordAsync(path!, input, token));
        Add(help, "recent [options]", adapters,
            new RuntimeTool("killershot_recent", "List the newest captures in KillerShot's encrypted library with their ids, capture times, sizes, window titles and program names.", LimitSchema(20, 20)),
            (input, token) => RecentAsync(path!, input, token));
        Add(help, "open <image>", adapters,
            new RuntimeTool("killershot_open_in_editor", "Open an image file in the KillerShot editor on the user's screen for annotation, redaction or cropping.", PathSchema()),
            (input, token) => OpenAsync(path!, input, token));
        Add(help, "settings", adapters,
            new RuntimeTool("killershot_settings", "Read KillerShot's capture, recording and shortcut settings without changing them.", EmptySchema()),
            (input, token) => SimpleAsync(path!, input, ["settings", "--json"], "capture", token));
        return adapters;
    }

    // ------------------------------------------------------------------------------------------
    // Calls
    // ------------------------------------------------------------------------------------------

    private static Task<AppCallResult> SimpleAsync(string path, JsonElement input, string[] arguments, string property, CancellationToken token)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any()) return Task.FromResult(Error("This tool takes no arguments"));
        return RunJsonAsync(path, arguments, property, 15, token);
    }

    private static Task<AppCallResult> WindowsAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "limit") || !OptionalInteger(input, "limit", 1, 500, 100, out int limit)) return Task.FromResult(Error("Expected an optional limit from 1 to 500"));
        return RunJsonAsync(path, ["windows", "--limit", Text(limit), "--json"], "windows", 15, token);
    }

    private static Task<AppCallResult> RecentAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "limit") || !OptionalInteger(input, "limit", 1, 20, 20, out int limit)) return Task.FromResult(Error("Expected an optional limit from 1 to 20"));
        return RunJsonAsync(path, ["recent", "--limit", Text(limit), "--json"], "items", 15, token);
    }

    private static Task<AppCallResult> OpenAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "path") || !RequiredString(input, "path", 1024, out var image) || !Path.IsPathFullyQualified(image!) || !File.Exists(image)
            || Path.GetExtension(image).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff"))
            return Task.FromResult(Error("Expected the absolute path of an existing PNG, JPEG, BMP, GIF or TIFF image"));
        return RunJsonAsync(path, ["open", image!, "--json"], "opened", 20, token);
    }

    private static Task<AppCallResult> CaptureAsync(string path, JsonElement input, string mode, CancellationToken token)
    {
        var allowed = new List<string> { "output", "clipboard", "cursor", "delay" };
        if (mode == "monitor") allowed.Add("monitor");
        if (mode == "window") allowed.Add("window");
        if (mode == "region") allowed.AddRange(["x", "y", "width", "height"]);
        if (!Allowed(input, [.. allowed])) return Task.FromResult(Error("Unknown capture argument"));

        var arguments = new List<string> { "capture", "--mode", mode };
        switch (mode)
        {
            case "monitor":
                if (input.TryGetProperty("monitor", out _))
                {
                    if (!Monitor(input, out var monitor)) return Task.FromResult(Error("Monitor must be primary or a number from 1 to 99"));
                    arguments.AddRange(["--monitor", monitor!]);
                }
                break;
            case "window":
                if (!RequiredString(input, "window", 256, out var window)) return Task.FromResult(Error("Window must be a handle or part of a window title"));
                arguments.AddRange(["--window", window!]);
                break;
            case "region":
                if (!Rect(input, out var rect)) return Task.FromResult(Error("Expected x, y, width and height in screen pixels"));
                arguments.AddRange(["--rect", rect!]);
                break;
        }
        var problem = CommonOptions(input, arguments, allowDelay: true);
        if (problem != null) return Task.FromResult(Error(problem));
        arguments.AddRange(["--save", "--no-shutter", "--json"]);
        int delay = input.TryGetProperty("delay", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out int seconds) ? seconds : 0;
        return RunJsonAsync(path, arguments, "path", 45 + delay, token);
    }

    private static Task<AppCallResult> RecordAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "mode", "monitor", "window", "x", "y", "width", "height", "seconds", "format", "sound", "fps", "output", "clipboard", "cursor"))
            return Task.FromResult(Error("Unknown recording argument"));
        if (!RequiredString(input, "mode", 16, out var mode) || mode is not ("monitor" or "all" or "window" or "region"))
            return Task.FromResult(Error("Mode must be monitor, all, window or region"));
        if (!RequiredInteger(input, "seconds", 1, 300, out int seconds)) return Task.FromResult(Error("Seconds must be from 1 to 300"));

        var arguments = new List<string> { "record", "--mode", mode!, "--seconds", Text(seconds) };
        bool hasRect = input.TryGetProperty("x", out _) || input.TryGetProperty("y", out _) || input.TryGetProperty("width", out _) || input.TryGetProperty("height", out _);
        if (mode != "monitor" && input.TryGetProperty("monitor", out _)) return Task.FromResult(Error("Monitor only applies to monitor mode"));
        if (mode != "window" && input.TryGetProperty("window", out _)) return Task.FromResult(Error("Window only applies to window mode"));
        if (mode != "region" && hasRect) return Task.FromResult(Error("x, y, width and height only apply to region mode"));
        switch (mode)
        {
            case "monitor" when input.TryGetProperty("monitor", out _):
                if (!Monitor(input, out var monitor)) return Task.FromResult(Error("Monitor must be primary or a number from 1 to 99"));
                arguments.AddRange(["--monitor", monitor!]);
                break;
            case "window":
                if (!RequiredString(input, "window", 256, out var window)) return Task.FromResult(Error("Window mode needs a window handle or part of a window title"));
                arguments.AddRange(["--window", window!]);
                break;
            case "region":
                if (!Rect(input, out var rect)) return Task.FromResult(Error("Region mode needs x, y, width and height in screen pixels"));
                arguments.AddRange(["--rect", rect!]);
                break;
        }
        if (input.TryGetProperty("format", out _))
        {
            if (!RequiredString(input, "format", 8, out var format) || format is not ("mp4" or "gif")) return Task.FromResult(Error("Format must be mp4 or gif"));
            arguments.AddRange(["--format", format!]);
        }
        if (input.TryGetProperty("sound", out _))
        {
            if (!RequiredString(input, "sound", 8, out var sound) || sound is not ("none" or "system" or "mic" or "both")) return Task.FromResult(Error("Sound must be none, system, mic or both"));
            arguments.AddRange(["--sound", sound!]);
        }
        if (input.TryGetProperty("fps", out _))
        {
            if (!RequiredInteger(input, "fps", 1, 60, out int fps)) return Task.FromResult(Error("Fps must be from 1 to 60"));
            arguments.AddRange(["--fps", Text(fps)]);
        }
        var problem = CommonOptions(input, arguments, allowDelay: false);
        if (problem != null) return Task.FromResult(Error(problem));
        arguments.Add("--json");
        return RunJsonAsync(path, arguments, "path", seconds + 90, token);
    }

    /// <summary>output, clipboard, cursor and delay. The clipboard is left alone unless the call
    /// asks for it, because the user may be holding something there.</summary>
    private static string? CommonOptions(JsonElement input, List<string> arguments, bool allowDelay)
    {
        if (input.TryGetProperty("output", out _))
        {
            if (!RequiredString(input, "output", 1024, out var output) || !Path.IsPathFullyQualified(output!)) return "Output must be an absolute file or folder path";
            arguments.AddRange(["--out", output!]);
        }
        if (!OptionalBoolean(input, "clipboard", out bool clipboard)) return "Clipboard must be true or false";
        if (arguments[0] == "capture") arguments.Add(clipboard ? "--clipboard" : "--no-clipboard");
        else if (clipboard) arguments.Add("--clipboard");
        if (input.TryGetProperty("cursor", out _))
        {
            if (!OptionalBoolean(input, "cursor", out bool cursor)) return "Cursor must be true or false";
            arguments.Add(cursor ? "--cursor" : "--no-cursor");
        }
        if (allowDelay && input.TryGetProperty("delay", out _))
        {
            if (!RequiredInteger(input, "delay", 0, 30, out int delay)) return "Delay must be from 0 to 30 seconds";
            arguments.AddRange(["--delay", Text(delay)]);
        }
        return null;
    }

    private static async Task<AppCallResult> RunJsonAsync(string path, IEnumerable<string> arguments, string property, int seconds, CancellationToken token)
    {
        try
        {
            var response = await RunAsync(path, arguments, TimeSpan.FromSeconds(seconds), 262144, token).ConfigureAwait(false);
            if (response.ExitCode != 0) return Error(ErrorText(response));
            if (response.OutputExceeded) return InvalidResponse();
            using var document = JsonDocument.Parse(response.StandardOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(property, out var value)) return InvalidResponse();
            if (property == "path" && (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))) return InvalidResponse();
            return new AppCallResult(document.RootElement.GetRawText());
        }
        catch (Exception exception) when (exception is JsonException or TimeoutException or InvalidOperationException)
        {
            return exception is JsonException ? InvalidResponse() : Error(Truncate(exception.Message));
        }
    }

    private static Task<ProcessResult> RunAsync(string path, IEnumerable<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken token)
    {
        // The GUI build runs its command line behind --cli; KillerShot.Cli.exe and test doubles do not need it.
        var prefix = path.EndsWith("KillerShot.exe", StringComparison.OrdinalIgnoreCase) ? new[] { "--cli" } : Array.Empty<string>();
        return ProcessRunner.RunAsync(path, prefix.Concat(arguments), timeout, maximumCharacters, token);
    }

    // ------------------------------------------------------------------------------------------
    // Validation
    // ------------------------------------------------------------------------------------------

    private static bool Allowed(JsonElement input, params string[] names) =>
        input.ValueKind == JsonValueKind.Object && input.EnumerateObject().All(item => Array.IndexOf(names, item.Name) >= 0);

    private static bool RequiredString(JsonElement input, string property, int maximum, out string? value)
    {
        value = null;
        if (!input.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    }

    private static bool RequiredInteger(JsonElement input, string property, int minimum, int maximum, out int value)
    {
        value = 0;
        return input.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value) && value >= minimum && value <= maximum;
    }

    private static bool OptionalInteger(JsonElement input, string property, int minimum, int maximum, int fallback, out int value)
    {
        value = fallback;
        return !input.TryGetProperty(property, out _) || RequiredInteger(input, property, minimum, maximum, out value);
    }

    private static bool OptionalBoolean(JsonElement input, string property, out bool value)
    {
        value = false;
        if (!input.TryGetProperty(property, out var element)) return true;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = element.GetBoolean();
        return true;
    }

    private static bool Monitor(JsonElement input, out string? monitor)
    {
        monitor = null;
        if (!RequiredString(input, "monitor", 8, out var value)) return false;
        if (value!.Equals("primary", StringComparison.OrdinalIgnoreCase)) { monitor = "primary"; return true; }
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number is >= 1 and <= 99) { monitor = Text(number); return true; }
        return false;
    }

    private static bool Rect(JsonElement input, out string? rect)
    {
        rect = null;
        if (!RequiredInteger(input, "x", -100000, 100000, out int x) || !RequiredInteger(input, "y", -100000, 100000, out int y)
            || !RequiredInteger(input, "width", 1, 32768, out int width) || !RequiredInteger(input, "height", 1, 32768, out int height)) return false;
        rect = string.Join(',', Text(x), Text(y), Text(width), Text(height));
        return true;
    }

    // ------------------------------------------------------------------------------------------
    // Schemas
    // ------------------------------------------------------------------------------------------

    private static JsonObject EmptySchema() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };

    private static JsonObject LimitSchema(int maximum, int fallback) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = maximum, ["default"] = fallback } },
        ["additionalProperties"] = false,
    };

    private static JsonObject PathSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["pattern"] = "\\.(?:[Pp][Nn][Gg]|[Jj][Pp][Ee]?[Gg]|[Bb][Mm][Pp]|[Gg][Ii][Ff]|[Tt][Ii][Ff][Ff]?)$", ["description"] = "Absolute path of a PNG, JPEG, BMP, GIF or TIFF image" } },
        ["required"] = new JsonArray("path"),
        ["additionalProperties"] = false,
    };

    private static JsonObject MonitorProperty() => new() { ["type"] = "string", ["pattern"] = "^(primary|[1-9][0-9]?)$", ["description"] = "Monitor number from killershot_list_monitors, or primary" };

    private static JsonObject WindowProperty() => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256, ["description"] = "Window handle such as 0x2003C, or part of the window title" };

    private static (string, JsonObject)[] RectProperties() =>
    [
        ("x", new JsonObject { ["type"] = "integer", ["minimum"] = -100000, ["maximum"] = 100000, ["description"] = "Left edge in screen pixels" }),
        ("y", new JsonObject { ["type"] = "integer", ["minimum"] = -100000, ["maximum"] = 100000, ["description"] = "Top edge in screen pixels" }),
        ("width", new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 32768 }),
        ("height", new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 32768 }),
    ];

    private static JsonObject OutputProperty(string kinds) => new()
    {
        ["type"] = "string",
        ["minLength"] = 1,
        ["maxLength"] = 1024,
        ["description"] = $"Absolute new {kinds} file path, or an existing folder. Omit to use KillerShot's output folder. Existing files are never replaced.",
    };

    private static JsonObject CaptureSchema(string[] required, params (string Name, JsonObject Schema)[] extra)
    {
        var properties = new JsonObject();
        foreach (var (name, property) in extra) properties[name] = property;
        properties["output"] = OutputProperty(".png, .jpg or .jpeg");
        properties["clipboard"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Also copy the screenshot to the clipboard" };
        properties["cursor"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include the mouse pointer. Omit to follow KillerShot's setting" };
        properties["delay"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 30, ["default"] = 0, ["description"] = "Seconds to wait before capturing" };
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        if (required.Length > 0) schema["required"] = new JsonArray(required.Select(name => (JsonNode)JsonValue.Create(name)!).ToArray());
        return schema;
    }

    private static JsonObject RecordSchema()
    {
        var properties = new JsonObject
        {
            ["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("monitor", "all", "window", "region") },
            ["seconds"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 300 },
            ["monitor"] = MonitorProperty(),
            ["window"] = WindowProperty(),
        };
        foreach (var (name, schema) in RectProperties()) properties[name] = schema;
        properties["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("mp4", "gif"), ["description"] = "Omit to follow KillerShot's setting" };
        properties["sound"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("none", "system", "mic", "both"), ["description"] = "MP4 only" };
        properties["fps"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 60 };
        properties["output"] = OutputProperty(".mp4 or .gif");
        properties["clipboard"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Also copy the finished file to the clipboard" };
        properties["cursor"] = new JsonObject { ["type"] = "boolean", ["description"] = "Include the mouse pointer. Omit to follow KillerShot's setting" };
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray("mode", "seconds"), ["additionalProperties"] = false };
    }

    // ------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------

    private static void Add(ProcessResult help, string marker, List<AppAdapter> adapters, RuntimeTool tool, Func<JsonElement, CancellationToken, Task<AppCallResult>> call)
    {
        if (help.StandardOutput.Contains(marker, StringComparison.Ordinal)) adapters.Add(new AppAdapter(tool, call));
    }

    private static bool IsFile(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); }
        catch { return false; }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static AppCallResult Error(string message) => new(message, true);
    private static AppCallResult InvalidResponse() => new("KillerShot returned an invalid response", true);
    private static string ErrorText(ProcessResult response) => Truncate(string.IsNullOrWhiteSpace(response.StandardError) ? "KillerShot command failed" : response.StandardError.Trim());
    private static string Truncate(string value) => value.Length <= 1024 ? value : value[..1024];
}
