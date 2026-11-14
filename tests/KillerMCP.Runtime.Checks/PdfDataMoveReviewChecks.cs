using System.Text.Json;
using KillerMCP;

internal static class PdfDataMoveReviewChecks
{
    public static bool Serve(string[] args)
    {
        if (Environment.GetEnvironmentVariable("KILLERMCP_PDF_MOVE_FIXTURE") != "1") return false;
        if (args.SequenceEqual(["--version"])) Console.Write("1.9.3");
        else if (args.SequenceEqual(["--help"])) Console.Write("--merge <out.pdf>\n--document-info <in.pdf>\n--secrets-stdin");
        else if (args.Length > 1 && args[0] == "--merge") File.WriteAllText(args[1], "%PDF-1.7\n%%EOF\n");
        else Console.Write("{\"pages\":225}");
        return true;
    }

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "killermcp-pdf-moves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("KILLERMCP_PDF_MOVE_FIXTURE");
        try
        {
            Environment.SetEnvironmentVariable("KILLERMCP_PDF_MOVE_FIXTURE", "1");
            var adapters = (await KillerPdfAdapter.CreateAsync(Environment.ProcessPath)).ToDictionary(item => item.Tool.Name);
            var large = Path.Combine(root, "flattened.pdf");
            using (var file = File.Create(large)) file.SetLength(65L * 1024 * 1024);
            var read = await adapters["killerpdf_document_info"].CallAsync(JsonSerializer.SerializeToElement(new { path = large }), CancellationToken.None);
            var failures = new List<string>();
            if (read.IsError) failures.Add("Flattened output cannot be read: " + read.Text);
            var inputs = Enumerable.Range(0, 225).Select(i => Path.Combine(root, i + ".pdf")).ToArray();
            foreach (var input in inputs) File.WriteAllText(input, "%PDF-1.7\n%%EOF\n");
            var merged = await adapters["killerpdf_merge"].CallAsync(JsonSerializer.SerializeToElement(new { inputs, output = Path.Combine(root, "merged.pdf") }), CancellationToken.None);
            if (merged.IsError) failures.Add("225 split parts cannot be merged: " + merged.Text);
            var deep = Path.Combine(root, new string('d', 150), new string('e', 150));
            Directory.CreateDirectory(deep);
            var longInputs = inputs.Select(input => Path.Combine(deep, Path.GetFileName(input))).ToArray();
            foreach (var input in longInputs) File.WriteAllText(input, "%PDF-1.7\n%%EOF\n");
            try
            {
                var longMerge = await adapters["killerpdf_merge"].CallAsync(JsonSerializer.SerializeToElement(new { inputs = longInputs, output = Path.Combine(root, "long-merged.pdf") }), CancellationToken.None);
                if (longMerge.IsError) failures.Add("Long-path split parts cannot be merged: " + longMerge.Text);
            }
            catch (InvalidOperationException exception) { failures.Add("Long-path merge exceeded the Windows command limit: " + exception.Message); }
            if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
            Console.WriteLine("PASS: flattened PDF above 64 MiB readable and 225 split parts merged.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KILLERMCP_PDF_MOVE_FIXTURE", previous);
            Directory.Delete(root, true);
        }
    }
}
