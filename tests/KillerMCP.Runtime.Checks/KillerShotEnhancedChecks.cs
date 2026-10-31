using System.Text.Json;
using KillerMCP;

internal static class KillerShotEnhancedChecks
{
    internal static async Task RunAsync(string executable, string root)
    {
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT", "1");
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_ENHANCED", "1");
        try
        {
            string image = Path.Combine(root, "enhanced-shot.png");
            File.WriteAllText(image, "fixture");
            foreach (var (version, supported) in new[] { ("1.0.0", false), ("unknown", false), ("1.1.1", true), ("1.2.0", true) })
            {
                Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_VERSION", version);
                var tools = (await KillerShotAdapter.CreateAsync(executable)).ToDictionary(item => item.Tool.Name);
                Require(tools.ContainsKey("killershot_capture_all"), "Existing capture must remain available: " + version);
                Require(tools.ContainsKey("killershot_read_text") == supported, "OCR must be gated to Shot 1.1.1 or newer: " + version);
                Require(tools.ContainsKey("killershot_read_qr") == supported, "QR reading must be version gated: " + version);
                Require(tools.ContainsKey("killershot_text_languages") == supported, "OCR language listing must be version gated: " + version);
                Require(tools.ContainsKey("killershot_recording_control") == supported, "Recording controls must be version gated: " + version);
                Require(tools.ContainsKey("killershot_edit_image") == supported, "Image edits must be version gated: " + version);
                Require(tools["killershot_recent"].Tool.InputSchema["properties"]!.AsObject().ContainsKey("search") == supported, "Library search must be version gated: " + version);
                using var searchRequest = JsonDocument.Parse("{\"limit\":5,\"search\":\"camera club\"}");
                var search = await tools["killershot_recent"].CallAsync(searchRequest.RootElement, CancellationToken.None);
                Require(search.IsError != supported, "Library search must be accepted only when supported: " + version);
                if (!supported) continue;
                Require(awaitedArguments(search).SequenceEqual(new[] { "recent", "--limit", "5", "--search", "camera club", "--json" }), "Search text must reach the CLI as one argument without a library action");
                using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { path = image }));
                var result = await tools["killershot_read_text"].CallAsync(request.RootElement, CancellationToken.None);
                Require(!result.IsError, result.Text);
                using var json = JsonDocument.Parse(result.Text);
                Require(json.RootElement.GetProperty("text").GetString() == "Fixture recognized text", "OCR must return its complete text");
                string[] arguments = json.RootElement.GetProperty("args").EnumerateArray().Select(item => item.GetString()!).ToArray();
                Require(arguments.SequenceEqual(new[] { "text", image, "--json" }), "File OCR must not open a screen selector or copy to the clipboard");
                var qr = await tools["killershot_read_qr"].CallAsync(request.RootElement, CancellationToken.None);
                Require(!qr.IsError && JsonDocument.Parse(qr.Text).RootElement.GetProperty("codes").GetArrayLength() == 1, "QR reading must return decoded codes without opening their links");
                using var empty = JsonDocument.Parse("{}");
                var languages = await tools["killershot_text_languages"].CallAsync(empty.RootElement, CancellationToken.None);
                Require(!languages.IsError && JsonDocument.Parse(languages.Text).RootElement.GetProperty("languages")[0].GetProperty("tag").GetString() == "en-US", "Installed OCR languages must be returned");
                foreach (string action in new[] { "pause", "resume", "stop", "cancel" })
                {
                    using var control = JsonDocument.Parse(JsonSerializer.Serialize(new { action, confirmDiscard = action == "cancel" }));
                    var resultControl = await tools["killershot_recording_control"].CallAsync(control.RootElement, CancellationToken.None);
                    Require(!resultControl.IsError && JsonDocument.Parse(resultControl.Text).RootElement.GetProperty("sent").GetString() == action, "Recording control must send the requested action");
                }
                using var unconfirmed = JsonDocument.Parse("{\"action\":\"cancel\"}");
                Require((await tools["killershot_recording_control"].CallAsync(unconfirmed.RootElement, CancellationToken.None)).IsError, "Cancel must require explicit consent to discard the unfinished recording");
                string edited = Path.Combine(root, "edited-" + version + ".png");
                using var editRequest = JsonDocument.Parse(JsonSerializer.Serialize(new { path = image, output = edited, rotate = "right", resize = "50%", margin = 5, marginColor = "#FFFFFF" }));
                var edit = await tools["killershot_edit_image"].CallAsync(editRequest.RootElement, CancellationToken.None);
                Require(!edit.IsError && File.Exists(edited) && File.ReadAllText(image) == "fixture", "Image edits must create a new output and preserve the source");
                Require(!(awaitedArguments(edit)).Contains("--overwrite"), "Image editing must never request overwrite");
                Require((await tools["killershot_edit_image"].CallAsync(editRequest.RootElement, CancellationToken.None)).IsError, "Existing output must not be replaced");
                using var invalidEdit = JsonDocument.Parse(JsonSerializer.Serialize(new { path = image, output = Path.Combine(root, "invalid-" + version + ".png"), rotate = "up" }));
                Require((await tools["killershot_edit_image"].CallAsync(invalidEdit.RootElement, CancellationToken.None)).IsError, "Unknown image operations must be refused");
            }
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_ENHANCED", null);
            Require(!(await KillerShotAdapter.CreateAsync(executable)).Any(item => item.Tool.Name == "killershot_read_text"), "Version alone must not expose a command missing from help");
            Console.WriteLine("PASS KillerShot OCR version and capability gates");
        }
        finally
        {
            foreach (string variable in new[] { "KILLERMCP_FAKE_SHOT", "KILLERMCP_FAKE_SHOT_ENHANCED", "KILLERMCP_FAKE_SHOT_VERSION" }) Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string[] awaitedArguments(AppCallResult result)
    {
        using var json = JsonDocument.Parse(result.Text);
        return json.RootElement.GetProperty("args").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }
}
