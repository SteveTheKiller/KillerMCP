using System.Text.Json;
using KillerMCP.Services.Mcp;

internal static class DataMoveLimitReviewChecks
{
    public static void Run()
    {
        var text = new string('x', 100_000);
        var document = JsonSerializer.Serialize(new { report = text });
        var requests = new (string Name, object Arguments)[]
        {
            ("format_json", new { text = document }),
            ("minify_json", new { text = document }),
            ("diff_text", new { left = text, right = text + "changed" }),
            ("diff_json", new { left = document, right = document }),
            ("convert_json", new { text = document, to = "yaml" }),
            ("json_to_csv", new { rows = new[] { new { report = text } } }),
        };
        var failures = new List<string>();
        foreach (var (name, arguments) in requests)
        {
            var result = JsonSerializer.SerializeToElement(ToolRegistry.Call(JsonSerializer.SerializeToElement(new { name, arguments }), new JsonSerializerOptions()));
            if (result.TryGetProperty("isError", out var error) && error.GetBoolean()) failures.Add(name + ": " + result.GetProperty("content")[0].GetProperty("text").GetString());
        }
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS: 100,000-character app reports through JSON, CSV and text-diff tools.");
    }
}
