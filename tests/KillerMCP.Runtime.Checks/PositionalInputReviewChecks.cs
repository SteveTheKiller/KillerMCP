using System.Text.Json;
using KillerMCP;

internal static class PositionalInputReviewChecks
{
    public static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_POSITIONAL_FIXTURE") != "1") return false;
        if (args.SequenceEqual(["--help"])) Console.Write("--search-text <in.pdf>\ncreate --title title\nsearch <query>");
        else if (args.SequenceEqual(["--version"])) Console.Write("1.9.3");
        else
        {
            if (Console.In.ReadToEnd().Length != 0) throw new InvalidOperationException("Fixture received host input.");
            Console.Write("{}");
        }
        return true;
    }

    public static async Task RunPdfAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "killermcp-positional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? previous = Environment.GetEnvironmentVariable("KILLERMCP_POSITIONAL_FIXTURE");
        try
        {
            Environment.SetEnvironmentVariable("KILLERMCP_POSITIONAL_FIXTURE", "1");
            var tool = (await KillerPdfAdapter.CreateAsync(Environment.ProcessPath)).Single(item => item.Tool.Name == "killerpdf_search_text");
            string source = Path.Combine(root, "input.pdf");
            File.WriteAllText(source, "fixture");
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { path = source, query = "--secrets-stdin" }), CancellationToken.None);
            if (!result.IsError || !result.Text.Contains("query", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Flag-shaped PDF query must be refused clearly: " + result.Text);
            Console.WriteLine("PASS PDF flag-shaped query refusal (1 selected regression)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_POSITIONAL_FIXTURE", previous);
            Directory.Delete(root, true);
        }
    }

    public static async Task RunNotesAsync()
    {
        string? previous = Environment.GetEnvironmentVariable("KILLERMCP_POSITIONAL_FIXTURE");
        try
        {
            Environment.SetEnvironmentVariable("KILLERMCP_POSITIONAL_FIXTURE", "1");
            var tool = (await KillerNotesAdapter.CreateAsync(Environment.ProcessPath)).Single(item => item.Tool.Name == "killernotes_search");
            var result = await tool.CallAsync(JsonSerializer.SerializeToElement(new { query = "--database" }), CancellationToken.None);
            if (!result.IsError || !result.Text.Contains("query", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Flag-shaped Notes query must be refused clearly: " + result.Text);
            Console.WriteLine("PASS Notes flag-shaped query refusal (1 selected regression)");
        }
        finally { Environment.SetEnvironmentVariable("KILLERMCP_POSITIONAL_FIXTURE", previous); }
    }

    public static async Task RunBenchAsync()
    {
        var repair = KillerBenchRepairs.Find("restore_point")!;
        foreach (string description in new[] { "--execute", "-execute", "  --execute  " })
        {
            var (plan, error) = await repair.Build(JsonSerializer.SerializeToElement(new { description }), CancellationToken.None);
            if (plan is not null || string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException("A flag-shaped description must not produce a preview plan: " + description);
        }
        var (ordinary, problem) = await repair.Build(JsonSerializer.SerializeToElement(new { description = "Before maintenance" }), CancellationToken.None);
        if (problem is not null || ordinary is null || ordinary.PreviewArguments.Contains("--execute"))
            throw new InvalidOperationException("Ordinary restore-point preview must stay read-only.");
        Console.WriteLine("PASS restore-point description cannot execute a preview (1 selected regression)");
    }
}
