using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

public static class KillerShellAdapter
{
    private static readonly Version MinimumVersion = new(1, 2, 6);

    public static async Task<AppAdapter?> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path) || !AppVersion.IsAtLeast(path!, MinimumVersion))
        {
            return null;
        }

        var help = await RunAsync(path!, ["--help"], TimeSpan.FromSeconds(8), 8192, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0 || !help.StandardOutput.Contains("search <folder>", StringComparison.Ordinal))
        {
            return null;
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["root"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Absolute directory path to search" },
                ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256, ["description"] = "Filename text or wildcard pattern" },
                ["content"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 512, ["description"] = "Text to find inside files" },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["default"] = 100 },
            },
            ["required"] = new JsonArray("root"),
            ["additionalProperties"] = false,
        };
        return new AppAdapter(
            new RuntimeTool("killershell_search_files", "Search local files with KillerShell by filename, file content, or both. Returns bounded JSON results.", schema),
            (input, token) => CallAsync(path!, input, token));
    }

    private static async Task<AppCallResult> CallAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        var problem = Validate(input, out var root, out var name, out var content, out var limit);
        if (problem is not null)
        {
            return new AppCallResult(problem, true);
        }

        var arguments = new List<string> { "search", root! };
        if (name is not null) arguments.AddRange(["--name", name]);
        if (content is not null) arguments.AddRange(["--content", content]);
        arguments.AddRange(["--limit", limit.ToString(CultureInfo.InvariantCulture)]);
        try
        {
            var response = await RunAsync(path, arguments, TimeSpan.FromSeconds(30), 262144, cancellationToken).ConfigureAwait(false);
            if (response.ExitCode != 0)
            {
                return new AppCallResult(ErrorText(response), true);
            }
            if (response.OutputExceeded)
            {
                return InvalidResponse();
            }

            using var document = JsonDocument.Parse(response.StandardOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return InvalidResponse();
            }
            return new AppCallResult(document.RootElement.GetRawText());
        }
        catch (Exception exception) when (exception is JsonException or TimeoutException or InvalidOperationException)
        {
            return exception is JsonException ? InvalidResponse() : new AppCallResult(Truncate(exception.Message), true);
        }
    }

    private static string? Validate(JsonElement input, out string? root, out string? name, out string? content, out int limit)
    {
        root = null;
        name = null;
        content = null;
        limit = 100;
        if (input.ValueKind != JsonValueKind.Object) return "Expected search arguments";
        if (input.EnumerateObject().Any(property => property.Name is not ("root" or "name" or "content" or "limit"))) return "Unknown search argument";
        if (!ReadString(input, "root", 1024, out root) || root is null || !Path.IsPathFullyQualified(root)) return "Root must be an absolute directory path";
        if (input.TryGetProperty("name", out _) && !ReadString(input, "name", 256, out name)) return "Invalid name";
        if (input.TryGetProperty("content", out _) && !ReadString(input, "content", 512, out content)) return "Invalid content";
        if (name is null && content is null) return "Provide a name or content search";
        if (input.TryGetProperty("limit", out var limitValue)
            && (limitValue.ValueKind != JsonValueKind.Number || !limitValue.TryGetInt32(out limit) || limit is < 1 or > 100)) return "Limit must be between 1 and 100";
        return null;
    }

    private static bool ReadString(JsonElement input, string property, int maximum, out string? value)
    {
        value = null;
        if (!input.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    }

    private static Task<ProcessResult> RunAsync(string path, IEnumerable<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken cancellationToken)
    {
        var prefix = path.EndsWith("KillerShell.exe", StringComparison.OrdinalIgnoreCase) ? new[] { "--cli" } : Array.Empty<string>();
        return ProcessRunner.RunAsync(path, prefix.Concat(arguments), timeout, maximumCharacters, cancellationToken);
    }

    private static bool IsFile(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); }
        catch { return false; }
    }

    private static AppCallResult InvalidResponse() => new("KillerShell returned an invalid search response", true);
    private static string ErrorText(ProcessResult response) => Truncate(string.IsNullOrWhiteSpace(response.StandardError) ? "KillerShell command failed" : response.StandardError.Trim());
    private static string Truncate(string value) => value.Length <= 1024 ? value : value[..1024];
}
