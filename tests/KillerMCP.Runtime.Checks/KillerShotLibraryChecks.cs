using System.Text.Json;
using KillerMCP;

/// <summary>The capture and collection tools: gated on KillerShot's version (collections from
/// 1.2.0, capture ids from 1.2.1), acting by capture id, never replacing files, and refusing
/// deletes without consent.</summary>
internal static class KillerShotLibraryChecks
{
    private static readonly string[] CaptureTools =
    [
        "killershot_export_capture", "killershot_copy_capture", "killershot_open_capture", "killershot_set_capture_note",
        "killershot_move_capture", "killershot_delete_capture",
    ];
    private static readonly string[] CollectionTools =
    [
        "killershot_list_collections", "killershot_create_collection", "killershot_rename_collection", "killershot_delete_collection",
    ];
    private static readonly string[] Tools = [.. CaptureTools, .. CollectionTools];

    internal static async Task RunAsync(string executable, string root)
    {
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT", "1");
        try
        {
            // The help below advertises every library command, so only the version decides.
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_LIBRARY", "1");
            foreach (var (version, managed, captures) in new[] { ("1.1.1", false, false), ("unknown", false, false), ("1.2.0", true, false), ("1.2.1", true, true), ("1.3.0", true, true) })
            {
                Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_VERSION", version);
                var listed = (await KillerShotAdapter.CreateAsync(executable)).Select(item => item.Tool.Name).ToHashSet();
                Require(CollectionTools.All(listed.Contains) == managed && CollectionTools.Any(listed.Contains) == managed,
                    "Collection tools must be gated to KillerShot 1.2.0 or newer: " + version);
                Require(CaptureTools.All(listed.Contains) == captures && CaptureTools.Any(listed.Contains) == captures,
                    "Capture id tools must be gated to KillerShot 1.2.1 or newer: " + version);
            }

            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_VERSION", "1.2.1");
            var tools = (await KillerShotAdapter.CreateAsync(executable)).ToDictionary(item => item.Tool.Name);
            foreach (string name in Tools)
            {
                Require(tools.ContainsKey(name), "Missing library tool " + name);
                JsonSerializer.SerializeToElement(ToolSafety.For(name));
            }
            Require(Hint("killershot_list_collections", "readOnlyHint"), "Listing collections must be read-only");
            foreach (string name in new[] { "killershot_copy_capture", "killershot_set_capture_note", "killershot_move_capture", "killershot_delete_capture", "killershot_rename_collection", "killershot_delete_collection" })
                Require(Hint(name, "destructiveHint"), name + " must be marked destructive");

            async Task<AppCallResult> Call(string name, object input)
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(input));
                return await tools[name].CallAsync(json.RootElement, CancellationToken.None);
            }
            async Task<string[]> Args(string name, object input)
            {
                var result = await Call(name, input);
                Require(!result.IsError, name + " failed: " + result.Text);
                return JsonDocument.Parse(result.Text).RootElement.GetProperty("args").EnumerateArray().Select(item => item.GetString()!).ToArray();
            }
            void Same(string[] actual, string[] expected, string what) =>
                Require(actual.SequenceEqual(expected), what + ": " + string.Join(' ', actual));

            string output = Path.Combine(root, "export.png");
            Same(await Args("killershot_export_capture", new { id = 42, output }), ["recent", "--export", "id:42", "--out", output, "--json"], "Export must name the capture by id and never overwrite");
            Require((await Call("killershot_export_capture", new { id = 42, output = "relative.png" })).IsError, "Export must refuse a relative path");
            Same(await Args("killershot_copy_capture", new { id = 7 }), ["recent", "--copy", "id:7", "--json"], "Copy must name the capture by id");
            Same(await Args("killershot_open_capture", new { id = 7 }), ["recent", "--open", "id:7", "--json"], "Open must name the capture by id");
            Same(await Args("killershot_set_capture_note", new { id = 7, note = "--looks like an option" }), ["recent", "--note", "id:7", "--text=--looks like an option", "--json"], "A note must reach KillerShot as text even when it starts with --");
            Same(await Args("killershot_set_capture_note", new { id = 7, note = "" }), ["recent", "--note", "id:7", "--text=", "--json"], "Empty note text must clear the note");
            Same(await Args("killershot_move_capture", new { id = 7, collection = "Camera club" }), ["recent", "--move", "id:7", "--to=Camera club", "--json"], "Move must name the collection");
            Same(await Args("killershot_move_capture", new { id = 7, collection = (string?)null }), ["recent", "--move", "id:7", "--to=none", "--json"], "A null collection must take the capture out of its collection");
            Require((await Call("killershot_move_capture", new { id = 7, collection = "none" })).IsError, "A collection literally named none must not silently unfile the capture");
            Require((await Call("killershot_delete_capture", new { id = 7, confirmDelete = false })).IsError, "Delete must refuse without consent");
            Require((await Call("killershot_delete_capture", new { id = 7 })).IsError, "Delete must refuse when consent is missing");
            Same(await Args("killershot_delete_capture", new { id = 7, confirmDelete = true }), ["recent", "--delete", "id:7", "--yes", "--json"], "Delete must send --yes only with consent");
            foreach (var bad in new object[] { new { id = 0 }, new { id = -3 }, new { id = "7" }, new { id = 1.5 }, new { } })
                Require((await Call("killershot_copy_capture", bad)).IsError, "A capture id must be a whole number from 1: " + JsonSerializer.Serialize(bad));
            Require((await Call("killershot_copy_capture", new { id = 7, index = 1 })).IsError, "Unknown arguments must be refused");

            var collections = await Call("killershot_list_collections", new { });
            Require(!collections.IsError && JsonDocument.Parse(collections.Text).RootElement.GetProperty("collections").GetArrayLength() == 1, "Collections must be listed");
            Same(await Args("killershot_create_collection", new { name = "Trips" }), ["collections", "create", "Trips", "--json"], "Create must pass the name");
            Same(await Args("killershot_rename_collection", new { name = "Trips", newName = "Travel" }), ["collections", "rename", "Trips", "Travel", "--json"], "Rename must pass both names");
            Require((await Call("killershot_create_collection", new { name = "--yes" })).IsError, "A name that reads as an option must be refused");
            Require((await Call("killershot_delete_collection", new { name = "Travel" })).IsError, "Deleting a collection must refuse without consent");
            Same(await Args("killershot_delete_collection", new { name = "Travel", confirmDelete = true }), ["collections", "delete", "Travel", "--yes", "--json"], "Delete must send --yes only with consent");
            Console.WriteLine("PASS KillerShot capture and collection tools");
        }
        finally
        {
            foreach (string variable in new[] { "KILLERMCP_FAKE_SHOT", "KILLERMCP_FAKE_SHOT_LIBRARY", "KILLERMCP_FAKE_SHOT_VERSION" }) Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static bool Hint(string name, string hint) => JsonSerializer.SerializeToElement(ToolSafety.For(name)).GetProperty(hint).GetBoolean();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
