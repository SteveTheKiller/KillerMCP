using System.Text;
using System.Text.Json;
using KillerMCP;
using KillerMCP.Features.Tools;
using KillerMCP.Services.Mcp;

internal static class DownloadWriteReviewChecks
{
    public static void Run()
    {
        var definition = BuiltInTools.Definitions.SingleOrDefault(tool =>
            (string)tool.GetType().GetProperty("name")!.GetValue(tool)! == "write_text_file_local");
        if (definition is null) throw new Exception("Missing direct text-file writing tool.");
        var downloads = DownloadFiles.DownloadsFolder;
        var path = Path.Combine(downloads, "killermcp-check-" + Guid.NewGuid().ToString("N") + ".txt");
        var outside = Path.Combine(Path.GetTempPath(), "killermcp-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        JsonElement Call(string name, object args) => JsonSerializer.SerializeToElement(ToolRegistry.Call(
            JsonSerializer.SerializeToElement(new { name, arguments = args }), json));
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        try
        {
            var text = "Report\n" + new string('x', 500_000) + "\n\u00e9\u65e5";
            var result = Call("write_text_file_local", new { path, text });
            Require(!result.TryGetProperty("isError", out var error) || !error.GetBoolean(), result.ToString());
            Require(File.ReadAllText(path, Encoding.UTF8) == text, "Text round trip changed content.");
            Require(Call("write_text_file_local", new { path, text = "overwrite" }).GetProperty("isError").GetBoolean(), "Existing file accepted.");
            Require(File.ReadAllText(path, Encoding.UTF8) == text, "Existing file changed.");
            // Every written file carries the Internet zone, so Windows treats it as downloaded.
            var zone = path + ":Zone.Identifier";
            Require(File.Exists(zone) && File.ReadAllText(zone).Contains("ZoneId=3"), "Written file has no Internet zone stream.");
            // Only inert data types can be written, never something Windows runs or follows.
            foreach (var extension in new[] { ".url", ".lnk", ".dll", ".exe", ".hta", ".library-ms", ".bat", ".ps1", ".js", ".html", ".svg", ".settingcontent-ms", "" })
            {
                var risky = Path.Combine(downloads, "killermcp-check-" + Guid.NewGuid().ToString("N") + extension);
                foreach (var name in new[] { "write_text_file_local", "decode_file_base64_local" })
                {
                    object args = name == "write_text_file_local" ? new { path = risky, text = "report" } : (object)new { path = risky, base64 = "cmVwb3J0" };
                    var refused = Call(name, args);
                    if (File.Exists(risky)) { File.Delete(risky); throw new Exception(name + " wrote a " + extension + " file."); }
                    Require(refused.GetProperty("isError").GetBoolean(), name + " accepted a " + extension + " name.");
                }
            }
            foreach (var name in new[] { "write_text_file_local", "decode_file_base64_local" })
            {
                object args = name == "write_text_file_local" ? new { path = outside, text = "report" } : (object)new { path = outside, base64 = "cmVwb3J0" };
                Require(Call(name, args).GetProperty("isError").GetBoolean(), name + " accepted a path outside Downloads.");
                Require(!File.Exists(outside), "Refused output created a file.");
            }
            var annotations = JsonSerializer.SerializeToElement(ToolSafety.For("write_text_file_local"));
            Require(!annotations.GetProperty("readOnlyHint").GetBoolean(), "Writer marked read-only.");
            Console.WriteLine("PASS: direct UTF-8 text, large report, no overwrite, Downloads boundaries and annotation.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }
}
