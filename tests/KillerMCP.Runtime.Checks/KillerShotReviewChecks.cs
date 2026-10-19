using KillerMCP.Runtime;
using System.Text.Json;

public static class KillerShotReviewChecks
{
    public static async Task RunAsync(string executable, string root)
    {
        var previous = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT");
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT", "1");
        try
        {
            var adapters = (await KillerShotAdapter.CreateAsync(executable)).ToDictionary(item => item.Tool.Name);
            async Task<AppCallResult> Call(string name, object input)
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(input));
                return await adapters[name].CallAsync(json.RootElement, CancellationToken.None);
            }
            var capture = await Call("killershot_capture_all", new { });
            Require(!capture.IsError, capture.Text);
            using var result = JsonDocument.Parse(capture.Text);
            Require(result.RootElement.GetProperty("args").EnumerateArray().Any(item => item.GetString() == "--save"), "Capture must request a saved file even when settings disable saving");
            var previousNull = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_SHOT_NULL_PATH");
            try
            {
                Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_NULL_PATH", "1");
                Require((await Call("killershot_capture_all", new { })).IsError, "A capture without a saved path must fail");
            }
            finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT_NULL_PATH", previousNull); }
            foreach (var monitor in new[] { "17", "99" })
            {
                Require(!(await Call("killershot_capture_monitor", new { monitor })).IsError, "Advertised monitor number must reach KillerShot: " + monitor);
                Require(!(await Call("killershot_record", new { mode = "monitor", monitor, seconds = 1 })).IsError, "Recording must accept the same monitor numbers: " + monitor);
            }
            Require((await Call("killershot_capture_monitor", new { monitor = "100" })).IsError, "Monitor above the advertised range must fail");
            Require((await Call("killershot_capture_monitor", new { monitor = "0" })).IsError, "Monitor zero must fail");
            var unsupported = Path.Combine(root, "shot-review.txt");
            await File.WriteAllTextAsync(unsupported, "fixture");
            try { Require((await Call("killershot_open_in_editor", new { path = unsupported })).IsError, "Existing files with unsupported image extensions must fail"); }
            finally { File.Delete(unsupported); }
            foreach (var extension in new[] { ".png", ".JPG", ".jpeg", ".bmp", ".gif", ".tif", ".TIFF" })
            {
                var image = Path.Combine(root, "shot-review" + extension);
                await File.WriteAllTextAsync(image, "fixture");
                try { Require(!(await Call("killershot_open_in_editor", new { path = image })).IsError, "Advertised image extension must reach the decoder: " + extension); }
                finally { File.Delete(image); }
            }
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_FAKE_SHOT", previous); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
