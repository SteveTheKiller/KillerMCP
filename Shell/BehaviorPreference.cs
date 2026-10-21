using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Notify;

// Setup and the update window share update-preferences.json. A change keeps any other keys in it and
// replaces the file whole, so a reader never sees it half written. A file that cannot be read counts as
// the default choice.
internal static class BehaviorPreference
{
    internal static string Read(string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject values
                && values["Behavior"] is JsonValue value && value.TryGetValue(out string? behavior)
                && behavior.ToLowerInvariant() is "off" or "check" or "download" or "install")
                return behavior.ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        return "check";
    }

    internal static void Write(string path, string behavior)
    {
        JsonObject values;
        try { values = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or JsonException) { values = new JsonObject(); }
        values["Behavior"] = behavior;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, values.ToJsonString() + Environment.NewLine);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
