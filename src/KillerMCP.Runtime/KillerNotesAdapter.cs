using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

public static class KillerNotesAdapter
{
    private static readonly Version MinimumVersion = new(1, 3, 1);

    public static async Task<AppAdapter?> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path) || !AppVersion.IsAtLeast(path!, MinimumVersion))
        {
            return null;
        }

        var help = await RunAsync(path!, ["--help"], TimeSpan.FromSeconds(8), 8192, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0 || !help.StandardOutput.Contains("search <query>", StringComparison.Ordinal))
        {
            return null;
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200 },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 20, ["default"] = 10 },
            },
            ["required"] = new JsonArray("query"),
            ["additionalProperties"] = false,
        };
        var tool = new RuntimeTool(
            "killernotes_search",
            "Search the active KillerNotes database for note titles, tags, and text snippets without changing notes. Use for requests such as \"killer find my notes about subnet plans\". Encrypted databases currently require an app unlock path and are unavailable through this tool.",
            schema);
        return new AppAdapter(tool, (input, token) => CallAsync(path!, input, token));
    }

    private static async Task<AppCallResult> CallAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (!ValidInput(input, out var query, out var limit))
        {
            return new AppCallResult("Provide a search query up to 200 characters and optional limit from 1 to 20", true);
        }

        try
        {
            var response = await RunAsync(path, ["search", query!, "--limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(20), 65536, cancellationToken).ConfigureAwait(false);
            if (response.ExitCode != 0)
            {
                return new AppCallResult(ErrorText(response), true);
            }
            if (response.OutputExceeded)
            {
                return new AppCallResult("KillerNotes returned an invalid search response", true);
            }

            using var document = JsonDocument.Parse(response.StandardOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 20)
            {
                return new AppCallResult("KillerNotes returned an invalid search response", true);
            }

            return new AppCallResult(document.RootElement.GetRawText());
        }
        catch (Exception exception) when (exception is JsonException or TimeoutException or InvalidOperationException)
        {
            return new AppCallResult(exception is JsonException ? "KillerNotes returned an invalid search response" : Truncate(exception.Message), true);
        }
    }

    private static bool ValidInput(JsonElement input, out string? query, out int limit)
    {
        query = null;
        limit = 10;
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(property => property.Name is not ("query" or "limit")))
        {
            return false;
        }
        if (!input.TryGetProperty("query", out var queryValue) || queryValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        query = queryValue.GetString();
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
        {
            return false;
        }
        if (input.TryGetProperty("limit", out var limitValue))
        {
            if (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) || limit is < 1 or > 20)
            {
                return false;
            }
        }

        return true;
    }

    private static Task<ProcessResult> RunAsync(string path, IEnumerable<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken cancellationToken)
    {
        var prefix = path.EndsWith("KillerNotes.exe", StringComparison.OrdinalIgnoreCase) ? new[] { "--cli" } : Array.Empty<string>();
        return ProcessRunner.RunAsync(path, prefix.Concat(arguments), timeout, maximumCharacters, cancellationToken);
    }

    private static bool IsFile(string? path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string ErrorText(ProcessResult response) => Truncate(string.IsNullOrWhiteSpace(response.StandardError) ? "KillerNotes command failed" : response.StandardError.Trim());
    private static string Truncate(string value) => value.Length <= 1024 ? value : value[..1024];
}
