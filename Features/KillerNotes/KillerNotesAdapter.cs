using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP;

public static class KillerNotesAdapter
{
    private static readonly Version MinimumVersion = new(1, 3, 1);

    public static async Task<IReadOnlyList<AppAdapter>> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path) || !AppVersion.IsAtLeast(path!, MinimumVersion)) return [];
        var help = await Run(path!, ["--help"], cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0 || !help.StandardOutput.Contains("create --title", StringComparison.Ordinal)) return [];
        // KillerNotes that reads long text from a file takes a whole note; older ones take what fits on a command line.
        NotesContent.Remember(path!, help.StandardOutput);
        bool longNotes = NotesContent.AcceptsFile(help.StandardOutput);
        int contentLimit = longNotes ? NotesContent.NoteLimit : NotesContent.CommandLineLimit;
        string limitText = longNotes ? "500,000" : "30,000";
        return
        [
            Make(path!, "killernotes_search", "Search notes by title, tags, and content.", Schema(new() { ["query"] = Text(200), ["limit"] = Integer(1, 20, 10) }, "query"), i => ["search", Str(i, "query"), "--limit", Num(i, "limit", 10)]),
            Make(path!, "killernotes_list", "List notes, optionally filtered by group or tag.", Schema(new() { ["group"] = Text(240), ["tag"] = Text(240), ["limit"] = Integer(1, 50, 20) }), i => Optional(["list", "--limit", Num(i, "limit", 20)], i, ("group", "--group"), ("tag", "--tag"))),
            Make(path!, "killernotes_get", "Read one note by ID.", Schema(new() { ["id"] = Id(), ["maxChars"] = Integer(1, 50000, 12000) }, "id"), i => ["get", Num(i, "id"), "--max-chars", Num(i, "maxChars", 12000)]),
            Make(path!, "killernotes_groups", "List note groups, colors, and note counts.", Schema(new()), _ => ["groups"]),
            Make(path!, "killernotes_tags", "List tags, colors, and note counts.", Schema(new()), _ => ["tags"]),
            Make(path!, "killernotes_backlinks", "List notes that link to a note.", Schema(new() { ["id"] = Id(), ["limit"] = Integer(1, 50, 20) }, "id"), i => ["backlinks", Num(i, "id"), "--limit", Num(i, "limit", 20)]),
            Make(path!, "killernotes_links", "List links from a note and whether each target exists.", Schema(new() { ["id"] = Id(), ["limit"] = Integer(1, 50, 20) }, "id"), i => ["links", Num(i, "id"), "--limit", Num(i, "limit", 20)]),
            Make(path!, "killernotes_history", "List saved versions for a note.", Schema(new() { ["id"] = Id(), ["limit"] = Integer(1, 50, 20) }, "id"), i => ["history", Num(i, "id"), "--limit", Num(i, "limit", 20)]),
            Make(path!, "killernotes_stats", "Return note, group, tag, link, history, trash, pin, and Markdown counts.", Schema(new()), _ => ["stats"]),
            Make(path!, "killernotes_create", $"Create a Markdown note of up to {limitText} characters in the open KillerNotes vault.", Schema(new() { ["title"] = Text(240), ["content"] = Text(contentLimit), ["group"] = Text(240), ["tags"] = Text(1000), ["titleColor"] = Color() }, "title", "content"), i => Optional(["create", "--title", Str(i, "title"), "--content", Str(i, "content")], i, ("group", "--group"), ("tags", "--tags"), ("titleColor", "--title-color"))),
            Make(path!, "killernotes_update", $"Update a note's title, Markdown content of up to {limitText} characters, group, tags, or title color.", Schema(new() { ["id"] = Id(), ["title"] = Text(240), ["content"] = Text(contentLimit), ["group"] = Text(240, true), ["tags"] = Text(1000, true), ["titleColor"] = Color(true) }, "id"), i => Optional(["update", "--id", Num(i, "id")], i, ("title", "--title"), ("content", "--content"), ("group", "--group"), ("tags", "--tags"), ("titleColor", "--title-color"))),
            Make(path!, "killernotes_create_group", "Create a note group and optionally set its color.", Schema(new() { ["name"] = Text(240), ["parent"] = Text(240), ["color"] = Color() }, "name"), i => Optional(["create-group", "--name", Str(i, "name")], i, ("parent", "--parent"), ("color", "--color"))),
            Make(path!, "killernotes_set_group_color", "Set or clear a note group color.", Schema(new() { ["name"] = Text(240), ["color"] = Color(true) }, "name", "color"), i => ["set-group-color", "--name", Str(i, "name"), "--color", Str(i, "color")]),
            Make(path!, "killernotes_set_title_color", "Set or clear a note title color.", Schema(new() { ["id"] = Id(), ["color"] = Color(true) }, "id", "color"), i => ["set-title-color", "--id", Num(i, "id"), "--color", Str(i, "color")]),
            Make(path!, "killernotes_import_image", "Save a local image as a new rich KillerNotes note.", Schema(new() { ["path"] = PathValue("Absolute path to a local image"), ["group"] = Text(240) }, "path"), i => Optional(["import-image", "--path", Str(i, "path")], i, ("group", "--group"))),
            Make(path!, "killernotes_export", "Export a note to a new TXT, Markdown, HTML, or KNOTE file. Its absolute output path can be passed to another Killer app.", Schema(new() { ["id"] = Id(), ["output"] = PathValue("Absolute path for a new .txt, .md, .html, or .knote file") }, "id", "output"), i => ["export", "--id", Num(i, "id"), "--output", Str(i, "output")]),
        ];
    }

    private static AppAdapter Make(string path, string name, string description, JsonObject schema, Func<JsonElement, List<string>> args) => new(new RuntimeTool(name, description, schema), async (input, token) =>
    {
        if (!Valid(input, schema)) return new AppCallResult("Invalid KillerNotes tool arguments", true);
        var arguments = args(input);
        string? folder = null;
        try
        {
            // Text too long for a command line goes to KillerNotes in a file.
            int index = -1;
            if (name is "killernotes_create" or "killernotes_update")
                for (int flag = 1; flag + 1 < arguments.Count; flag += 2)
                    if (arguments[flag] == "--content") { index = flag; break; }
            if (index >= 0)
            {
                var prepared = await NotesContent.ArgumentsAsync(path, arguments[index + 1], token,
                    arguments.Where((_, position) => position != index && position != index + 1)).ConfigureAwait(false);
                if (prepared.Error is not null) return new AppCallResult(prepared.Error, true);
                arguments.RemoveRange(index, 2);
                arguments.InsertRange(index, prepared.Arguments);
                folder = prepared.Folder;
            }
            var r = await Run(path, arguments, token).ConfigureAwait(false); if (r.ExitCode != 0 || r.OutputExceeded) return new AppCallResult(Error(r), true); using var doc = JsonDocument.Parse(r.StandardOutput); return new AppCallResult(doc.RootElement.GetRawText());
        }
        catch (Exception ex) when (ex is JsonException or TimeoutException or InvalidOperationException or IOException or UnauthorizedAccessException) { return new AppCallResult(Limit(ex.Message), true); }
        finally { NotesContent.Delete(folder); }
    });

    private static Task<ProcessResult> Run(string path, IEnumerable<string> args, CancellationToken token) { var prefix = path.EndsWith("KillerNotes.exe", StringComparison.OrdinalIgnoreCase) ? new[] { "--cli" } : Array.Empty<string>(); return ProcessRunner.RunAsync(path, prefix.Concat(args), TimeSpan.FromSeconds(30), 65536, token); }
    private static List<string> Optional(List<string> args, JsonElement input, params (string Property, string Flag)[] options) { foreach (var o in options) if (input.TryGetProperty(o.Property, out var v) && v.ValueKind == JsonValueKind.String) args.AddRange([o.Flag, v.GetString()!]); return args; }
    private static JsonObject Schema(JsonObject properties, params string[] required) { var value = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false }; if (required.Length > 0) value["required"] = new JsonArray(required.Select(item => JsonValue.Create(item)).ToArray()); return value; }
    private static JsonObject Text(int max, bool empty = false) => new() { ["type"] = "string", ["minLength"] = empty ? 0 : 1, ["maxLength"] = max };
    private static JsonObject Color(bool empty = false) => new() { ["type"] = "string", ["minLength"] = empty ? 0 : 1, ["maxLength"] = 32, ["description"] = "Named color or #RRGGBB. Use an empty string to clear when allowed." };
    private static JsonObject Id() => Integer(1, int.MaxValue);
    private static JsonObject Integer(int min, int max, int? fallback = null) { var v = new JsonObject { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max }; if (fallback.HasValue) v["default"] = fallback; return v; }
    private static JsonObject PathValue(string description) => new() { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 1024, ["description"] = description };
    private static bool Valid(JsonElement input, JsonObject schema)
    {
        if (input.ValueKind != JsonValueKind.Object || schema["properties"] is not JsonObject properties) return false;
        if (input.EnumerateObject().Any(item => !properties.ContainsKey(item.Name))) return false;
        if (schema["required"] is JsonArray required && required.Any(item => item is null || !input.TryGetProperty(item.GetValue<string>(), out _))) return false;
        foreach (var item in input.EnumerateObject())
        {
            if (properties[item.Name] is not JsonObject rule || rule["type"] is null) return false;
            string type = rule["type"]!.GetValue<string>();
            if (type == "string")
            {
                if (item.Value.ValueKind != JsonValueKind.String) return false;
                int length = item.Value.GetString()!.Length;
                if (rule["minLength"] is JsonValue min && length < min.GetValue<int>() || rule["maxLength"] is JsonValue max && length > max.GetValue<int>()) return false;
            }
            else if (type == "integer")
            {
                if (item.Value.ValueKind != JsonValueKind.Number || !item.Value.TryGetInt32(out int number)) return false;
                if (rule["minimum"] is JsonValue min && number < min.GetValue<int>() || rule["maximum"] is JsonValue max && number > max.GetValue<int>()) return false;
            }
        }
        return true;
    }
    private static string Str(JsonElement input, string name) => input.GetProperty(name).GetString()!;
    private static string Num(JsonElement input, string name, int fallback = 0) => input.TryGetProperty(name, out var v) ? v.GetRawText() : fallback.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static bool IsFile(string? path) { try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); } catch { return false; } }
    private static string Error(ProcessResult r) => Limit(string.IsNullOrWhiteSpace(r.StandardError) ? "KillerNotes command failed" : r.StandardError.Trim());
    private static string Limit(string value) => value.Length <= 1024 ? value : value[..1024];
}
