using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using KillerMCP;
using QRCoder;

internal static class RealAppCompatibilityChecks
{
    internal static async Task PrintAsync(string executable, string source, string printer)
    {
        var tool = (await KillerPdfAdapter.CreateAsync(executable)).Single(item => item.Tool.Name == "killerpdf_print");
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { path = source, printer }));
        var clock = Stopwatch.StartNew();
        var result = await tool.CallAsync(request.RootElement, CancellationToken.None);
        if (result.IsError) throw new InvalidOperationException(result.Text);
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "real PDF print through MCP", elapsedMs = clock.ElapsedMilliseconds, result = result.Text }));
    }

    internal static async Task ShotAsync(string executable, string root)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Use an owned Windows test VM");
        var actual = (await KillerShotAdapter.CreateAsync(executable)).ToDictionary(item => item.Tool.Name);
        foreach (string name in new[] { "killershot_read_text", "killershot_text_languages", "killershot_read_qr", "killershot_edit_image", "killershot_recording_control" })
            Require(actual.ContainsKey(name), "Released KillerShot must advertise " + name);
        Require(actual["killershot_recent"].Tool.InputSchema["properties"]!.AsObject().ContainsKey("search"), "Released Shot must advertise library search");
        string image = Path.Combine(root, "text.png"), qr = Path.Combine(root, "qr.png");
        using (var bitmap = new Bitmap(900, 200))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Arial", 38))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("MCP TEXT CHECK 12345", font, Brushes.Black, 20, 50);
            bitmap.Save(image, ImageFormat.Png);
        }
        using (var generator = new QRCodeGenerator())
        using (var data = generator.CreateQrCode("https://example.com/mcp-compatibility", QRCodeGenerator.ECCLevel.Q))
        using (var png = new PngByteQRCode(data)) File.WriteAllBytes(qr, png.GetGraphic(8));
        string oldBridge = Environment.GetEnvironmentVariable("KILLERMCP_REAL_SHOT") ?? "";
        string oldRoot = Environment.GetEnvironmentVariable("KILLERMCP_REAL_SHOT_ROOT") ?? "";
        Environment.SetEnvironmentVariable("KILLERMCP_REAL_SHOT", executable);
        Environment.SetEnvironmentVariable("KILLERMCP_REAL_SHOT_ROOT", root);
        try
        {
            // The released app's existing self-test entry isolates settings and the album. Its real
            // CLI and image/OCR implementations still perform each operation.
            var tools = (await KillerShotAdapter.CreateAsync(Environment.ProcessPath!)).ToDictionary(item => item.Tool.Name);
            async Task<JsonElement> Call(string name, object input)
            {
                using var request = JsonDocument.Parse(JsonSerializer.Serialize(input));
                var result = await tools[name].CallAsync(request.RootElement, CancellationToken.None);
                Require(!result.IsError, name + ": " + result.Text);
                using var json = JsonDocument.Parse(result.Text);
                return json.RootElement.Clone();
            }
            var languages = await Call("killershot_text_languages", new { });
            Require(languages.GetProperty("languages").GetArrayLength() > 0, "OCR languages must be installed on the VM");
            var text = await Call("killershot_read_text", new { path = image });
            Require(text.GetProperty("text").GetString()!.Contains("12345", StringComparison.Ordinal), "Actual OCR must recognize the synthetic digits");
            var codes = await Call("killershot_read_qr", new { path = qr });
            Require(codes.GetProperty("codes").EnumerateArray().Any(code => code.GetProperty("text").GetString() == "https://example.com/mcp-compatibility"), "Actual QR decoding must return the synthetic payload");
            string edited = Path.Combine(root, "edited.png");
            byte[] original = File.ReadAllBytes(image);
            await Call("killershot_edit_image", new { path = image, output = edited, rotate = "right" });
            using var turned = Image.FromFile(edited);
            Require(turned.Width == 200 && turned.Height == 900, "Actual image rotation must swap dimensions");
            Require(File.ReadAllBytes(image).AsSpan().SequenceEqual(original), "Actual edit must preserve source bytes");
            var recent = await Call("killershot_recent", new { search = "nonexistent synthetic item", limit = 5 });
            Require(recent.GetProperty("items").GetArrayLength() == 0, "Isolated synthetic library search must return no matches");
            Console.WriteLine("PASS released KillerShot discovery, OCR, language listing, QR decoding, image edit and isolated library search");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_REAL_SHOT", oldBridge.Length == 0 ? null : oldBridge);
            Environment.SetEnvironmentVariable("KILLERMCP_REAL_SHOT_ROOT", oldRoot.Length == 0 ? null : oldRoot);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
