using System.Text.Json;
using KillerMCP;
using KillerMCP.Services.Mcp;

internal static class NetworkPathReviewChecks
{
    public static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_PDF_PATH_FIXTURE") != "1") return false;
        if (args.SequenceEqual(["--help"])) Console.Write("--set-bookmarks <in.pdf>\n--secrets-stdin");
        else if (args.SequenceEqual(["--version"])) Console.Write("1.9.2");
        else throw new InvalidOperationException("A refused path reached the PDF process.");
        return true;
    }

    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-path-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Environment.SetEnvironmentVariable("KILLERMCP_PDF_PATH_FIXTURE", "1");
            ToolRegistry.SetAdapters(await KillerPdfAdapter.CreateAsync(Environment.ProcessPath));
            string source = Path.Combine(root, "source.pdf");
            File.WriteAllText(source, "fixture");
            // A device path exercises the missing pre-open guard without contacting a remote server.
            var input = JsonSerializer.SerializeToElement(new
            {
                name = "killerpdf_set_bookmarks",
                arguments = new { path = source, output = Path.Combine(root, "result.pdf"), bookmarks = @"\\?\GLOBALROOT\Device\MissingVolume\bookmarks.json" },
            });
            var result = JsonSerializer.SerializeToElement(ToolRegistry.Call(input, new JsonSerializerOptions()));
            if (!result.GetProperty("isError").GetBoolean() || result.GetProperty("content")[0].GetProperty("text").GetString() != AgentPaths.DeviceMessage)
                throw new InvalidOperationException("Bookmark input was not refused by the pre-open path guard: " + result);
            Console.WriteLine("PASS bookmark input path guard (1 selected regression)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_PDF_PATH_FIXTURE", null);
            ToolRegistry.SetAdapters([]);
            Directory.Delete(root, true);
        }
    }
}
