using System.Text.Json;
using KillerMCP;

internal static class PdfCreationChecks
{
    internal static async Task RunAsync(string root)
    {
        string output = Path.Combine(root, "plain-creation.pdf");
        var tool = IntegrationAdapter.Create(null, null, null, null, null).Single(item => item.Tool.Name == "killer_create_pdf");
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(new { content = "Local PDF creation fixture", output }));
        var result = await tool.CallAsync(request.RootElement, CancellationToken.None);
        if (result.IsError || !File.Exists(output) || !File.ReadAllBytes(output).AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidOperationException("A plain text PDF must be created: " + result.Text);
        Console.WriteLine("PASS actual Edge PDF creation");
    }
}
