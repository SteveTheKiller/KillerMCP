using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP;

public static partial class KillerShotAdapter
{
    // KillerShot 1.2.1 and later act on a capture by the id that killershot_recent lists. A list
    // position moves when a new capture arrives, so these tools never send one.
    private const string CaptureIdMarker = "id:<id> with the id from --json";
    private const string CollectionsMarker = "collections [create|rename|delete]";

    private static void AddLibrary(string path, string help, List<AppAdapter> adapters)
    {
        if (help.Contains(CaptureIdMarker, StringComparison.Ordinal))
        {
            var export = IdSchema();
            export["properties"]!["output"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute new file path or an existing folder. A picture saves as .png, .jpg or .jpeg; a recording saves as its clip's own extension. Existing files are never replaced." };
            export["required"] = new JsonArray("id", "output");
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_export_capture", "Save a copy of one capture from KillerShot's library to a file or folder and return its path. Edited pictures are saved flattened; a recording saves as its clip. Use the id from killershot_recent.", export),
                (input, token) => ExportCaptureAsync(path, input, token)));
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_copy_capture", "Copy one picture from KillerShot's library to the clipboard, replacing what is there. Recordings cannot be copied; export them instead. Use the id from killershot_recent.", IdSchema()),
                (input, token) => CaptureActionAsync(path, input, "copy", [], token)));
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_open_capture", "Open one capture from KillerShot's library in the KillerShot editor on the user's screen. Use the id from killershot_recent.", IdSchema()),
                (input, token) => CaptureActionAsync(path, input, "open", [], token)));
            var note = IdSchema();
            note["properties"]!["note"] = new JsonObject { ["type"] = "string", ["maxLength"] = 2000, ["description"] = "The new note. Empty text clears it." };
            note["required"] = new JsonArray("id", "note");
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_set_capture_note", "Set or clear the note on one capture in KillerShot's library, replacing its current note. Use the id from killershot_recent.", note),
                (input, token) => SetNoteAsync(path, input, token)));
            var move = IdSchema();
            move["properties"]!["collection"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["maxLength"] = 200, ["description"] = "Name of an existing collection from killershot_list_collections, or null to take the capture out of its collection" };
            move["required"] = new JsonArray("id", "collection");
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_move_capture", "Move one capture in KillerShot's library into a collection, or out of its collection. Use the id from killershot_recent.", move),
                (input, token) => MoveCaptureAsync(path, input, token)));
            var delete = IdSchema();
            delete["properties"]!["confirmDelete"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Required true. Deleting removes the capture from the album for good." };
            delete["required"] = new JsonArray("id", "confirmDelete");
            adapters.Add(new AppAdapter(new RuntimeTool("killershot_delete_capture", "Delete one capture from KillerShot's library. This cannot be undone and requires confirmDelete=true. Use the id from killershot_recent.", delete),
                (input, token) => DeleteCaptureAsync(path, input, token)));
        }
        if (!help.Contains(CollectionsMarker, StringComparison.Ordinal)) return;
        adapters.Add(new AppAdapter(new RuntimeTool("killershot_list_collections", "List the collections in KillerShot's library with how many captures each holds. Does not change the library.", EmptySchema()),
            (input, token) => SimpleAsync(path, input, ["collections", "--json"], "collections", token)));
        adapters.Add(new AppAdapter(new RuntimeTool("killershot_create_collection", "Create an empty collection in KillerShot's library.", NameSchema(("name", "Name for the new collection"))),
            (input, token) => CollectionAsync(path, input, "create", ["name"], confirm: false, token)));
        adapters.Add(new AppAdapter(new RuntimeTool("killershot_rename_collection", "Rename a collection in KillerShot's library.", NameSchema(("name", "Current name from killershot_list_collections"), ("newName", "The new name"))),
            (input, token) => CollectionAsync(path, input, "rename", ["name", "newName"], confirm: false, token)));
        var removeCollection = NameSchema(("name", "Name from killershot_list_collections"));
        removeCollection["properties"]!["confirmDelete"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Required true. The collection is removed for good; its captures stay in the album." };
        removeCollection["required"] = new JsonArray("name", "confirmDelete");
        adapters.Add(new AppAdapter(new RuntimeTool("killershot_delete_collection", "Delete a collection from KillerShot's library. Its captures stay in the album, in no collection. Requires confirmDelete=true.", removeCollection),
            (input, token) => CollectionAsync(path, input, "delete", ["name"], confirm: true, token)));
    }

    private static Task<AppCallResult> ExportCaptureAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "id", "output") || !CaptureId(input, out var id) || !RequiredString(input, "output", 1024, out var output) || !Path.IsPathFullyQualified(output!))
            return Task.FromResult(Error("Expected a capture id from killershot_recent and an absolute output file or folder"));
        // An agent never replaces a file, so --overwrite is never sent.
        return RunJsonAsync(path, ["recent", "--export", "id:" + id, "--out", output!, "--json"], "path", 60, token);
    }

    private static Task<AppCallResult> SetNoteAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "id", "note") || !CaptureId(input, out var id)
            || !input.TryGetProperty("note", out var note) || note.ValueKind != JsonValueKind.String || note.GetString()!.Length > 2000)
            return Task.FromResult(Error("Expected a capture id from killershot_recent and note text of up to 2000 characters"));
        // The inline form keeps a note that starts with -- from reading as an option.
        return CaptureActionAsync(path, input, "note", ["--text=" + note.GetString()], token, id);
    }

    private static Task<AppCallResult> MoveCaptureAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "id", "collection") || !CaptureId(input, out var id) || !input.TryGetProperty("collection", out var collection))
            return Task.FromResult(Error("Expected a capture id from killershot_recent and a collection name or null"));
        string target;
        if (collection.ValueKind == JsonValueKind.Null) target = "none";
        else if (collection.ValueKind == JsonValueKind.String && CollectionName(collection.GetString(), out var name))
        {
            // KillerShot reads none as no collection, so a collection by that name cannot be named here.
            if (name!.Equals("none", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(Error("Use null to take a capture out of its collection; a collection named none cannot be chosen here"));
            target = name;
        }
        else return Task.FromResult(Error("Collection must be a name of up to 200 characters or null"));
        return CaptureActionAsync(path, input, "move", ["--to=" + target], token, id);
    }

    private static Task<AppCallResult> DeleteCaptureAsync(string path, JsonElement input, CancellationToken token)
    {
        if (!Allowed(input, "id", "confirmDelete") || !CaptureId(input, out var id) || !OptionalBoolean(input, "confirmDelete", out bool confirm))
            return Task.FromResult(Error("Expected a capture id from killershot_recent and confirmDelete"));
        if (!confirm) return Task.FromResult(Error("Deleting a capture cannot be undone; confirmDelete must be true"));
        return CaptureActionAsync(path, input, "delete", ["--yes"], token, id);
    }

    private static Task<AppCallResult> CaptureActionAsync(string path, JsonElement input, string action, string[] extra, CancellationToken token, string? checkedId = null)
    {
        string? id = checkedId;
        if (id == null && (!Allowed(input, "id") || !CaptureId(input, out id)))
            return Task.FromResult(Error("Expected a capture id from killershot_recent"));
        return RunJsonAsync(path, ["recent", "--" + action, "id:" + id, .. extra, "--json"], "action", 30, token);
    }

    private static Task<AppCallResult> CollectionAsync(string path, JsonElement input, string verb, string[] names, bool confirm, CancellationToken token)
    {
        string[] allowed = confirm ? [.. names, "confirmDelete"] : names;
        if (!Allowed(input, allowed)) return Task.FromResult(Error("Unknown collection argument"));
        var arguments = new List<string> { "collections", verb };
        foreach (string property in names)
        {
            if (!input.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || !CollectionName(value.GetString(), out var name))
                return Task.FromResult(Error($"{property} must be a collection name of up to 200 characters that does not start with -"));
            arguments.Add(name!);
        }
        if (confirm)
        {
            if (!OptionalBoolean(input, "confirmDelete", out bool yes)) return Task.FromResult(Error("confirmDelete must be true or false"));
            if (!yes) return Task.FromResult(Error("Deleting a collection cannot be undone; confirmDelete must be true"));
            arguments.Add("--yes");
        }
        arguments.Add("--json");
        return RunJsonAsync(path, arguments, "action", 30, token);
    }

    private static bool CaptureId(JsonElement input, out string? id)
    {
        id = null;
        if (!input.TryGetProperty("id", out var element) || element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out long value) || value < 1) return false;
        id = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    // Collection names are command line arguments, so one that starts with - would read as an option.
    private static bool CollectionName(string? value, out string? name)
    {
        name = value?.Trim();
        return !string.IsNullOrEmpty(name) && name.Length <= 200 && !name.StartsWith('-');
    }

    private static JsonObject IdSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["id"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["description"] = "Capture id from killershot_recent" } },
        ["required"] = new JsonArray("id"),
        ["additionalProperties"] = false,
    };

    private static JsonObject NameSchema(params (string Name, string Description)[] names)
    {
        var properties = new JsonObject();
        foreach (var (name, description) in names)
            properties[name] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200, ["pattern"] = "^[^-]", ["description"] = description };
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(names.Select(item => (JsonNode)JsonValue.Create(item.Name)!).ToArray()), ["additionalProperties"] = false };
    }
}
