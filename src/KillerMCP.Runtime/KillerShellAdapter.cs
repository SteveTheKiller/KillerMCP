using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

public static class KillerShellAdapter
{
    private static readonly Version MinimumVersion = new(1, 2, 6);

    public static async Task<IReadOnlyList<AppAdapter>> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path) || !AppVersion.IsAtLeast(path!, MinimumVersion))
        {
            return [];
        }

        var help = await RunAsync(path!, ["--help"], TimeSpan.FromSeconds(8), 8192, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0 || !help.StandardOutput.Contains("search <folder>", StringComparison.Ordinal))
        {
            return [];
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
        var adapters = new List<AppAdapter>
        {
            new(
            new RuntimeTool("killershell_search_files", "Search local files with KillerShell by filename, file content, or both. Returns bounded JSON results.", schema),
            (input, token) => SearchAsync(path!, input, token))
        };
        Add(help, "list <folder>", adapters,
            new RuntimeTool("killershell_list_directory", "List the files and folders directly inside one local directory with KillerShell.", PathSchema("Absolute directory path to list", includeLimit: true)),
            (input, token) => ListAsync(path!, input, token));
        Add(help, "info <path>", adapters,
            new RuntimeTool("killershell_file_info", "Read local file or folder details with KillerShell without changing the item.", PathSchema("Absolute file or directory path")),
            (input, token) => InfoAsync(path!, input, token));
        Add(help, "read <file>", adapters,
            new RuntimeTool("killershell_read_text_file", "Read a bounded amount of text from one local file with KillerShell.", ReadSchema()),
            (input, token) => ReadAsync(path!, input, token));
        return adapters;
    }

    private static async Task<AppCallResult> SearchAsync(string path, JsonElement input, CancellationToken cancellationToken)
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

    private static Task<AppCallResult> ListAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (!ValidatePathInput(input, allowLimit: true, allowMaximum: false, out var target, out var limit))
            return Task.FromResult(new AppCallResult("Expected an absolute directory path and an optional limit from 1 to 100", true));
        return RunJsonAsync(path, ["list", target!, "--limit", limit.ToString(CultureInfo.InvariantCulture)], "entries", JsonValueKind.Array, 262144, cancellationToken);
    }

    private static Task<AppCallResult> InfoAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (!ValidatePathInput(input, allowLimit: false, allowMaximum: false, out var target, out _))
            return Task.FromResult(new AppCallResult("Expected one absolute file or directory path", true));
        return RunJsonAsync(path, ["info", target!], "isDirectory", JsonValueKind.True, 32768, cancellationToken, JsonValueKind.False);
    }

    private static Task<AppCallResult> ReadAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (!ValidatePathInput(input, allowLimit: false, allowMaximum: true, out var target, out var maximum))
            return Task.FromResult(new AppCallResult("Expected an absolute file path and optional maxCharacters from 1 to 32768", true));
        return RunJsonAsync(path, ["read", target!, "--max-chars", maximum.ToString(CultureInfo.InvariantCulture)], "text", JsonValueKind.String, 262144, cancellationToken);
    }

    private static async Task<AppCallResult> RunJsonAsync(string path, IEnumerable<string> arguments, string property, JsonValueKind kind, int maximumOutput, CancellationToken cancellationToken, JsonValueKind? alternateKind = null)
    {
        try
        {
            var response = await RunAsync(path, arguments, TimeSpan.FromSeconds(10), maximumOutput, cancellationToken).ConfigureAwait(false);
            if (response.ExitCode != 0) return new AppCallResult(ErrorText(response), true);
            if (response.OutputExceeded) return InvalidResponse();
            using var document = JsonDocument.Parse(response.StandardOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(property, out var value)
                || (value.ValueKind != kind && value.ValueKind != alternateKind)) return InvalidResponse();
            return new AppCallResult(document.RootElement.GetRawText());
        }
        catch (Exception exception) when (exception is JsonException or TimeoutException or InvalidOperationException)
        {
            return exception is JsonException ? InvalidResponse() : new AppCallResult(Truncate(exception.Message), true);
        }
    }

    private static bool ValidatePathInput(JsonElement input, bool allowLimit, bool allowMaximum, out string? path, out int number)
    {
        path = null;
        number = allowMaximum ? 32768 : 100;
        if (input.ValueKind != JsonValueKind.Object) return false;
        if (input.EnumerateObject().Any(property => property.Name != "path"
            && (!allowLimit || property.Name != "limit")
            && (!allowMaximum || property.Name != "maxCharacters"))) return false;
        if (!ReadString(input, "path", 1024, out path) || path is null || !Path.IsPathFullyQualified(path)) return false;
        string option = allowMaximum ? "maxCharacters" : "limit";
        int maximum = allowMaximum ? 32768 : 100;
        return (!allowLimit && !allowMaximum) || !input.TryGetProperty(option, out var value)
            || value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number) && number >= 1 && number <= maximum;
    }

    private static JsonObject PathSchema(string description, bool includeLimit = false)
    {
        var properties = new JsonObject { ["path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = description } };
        if (includeLimit) properties["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["default"] = 100 };
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray("path"), ["additionalProperties"] = false };
    }

    private static JsonObject ReadSchema()
    {
        var schema = PathSchema("Absolute path to a text file");
        ((JsonObject)schema["properties"]!)["maxCharacters"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 32768, ["default"] = 32768 };
        return schema;
    }

    private static void Add(ProcessResult help, string marker, List<AppAdapter> adapters, RuntimeTool tool, Func<JsonElement, CancellationToken, Task<AppCallResult>> call)
    {
        if (help.StandardOutput.Contains(marker, StringComparison.Ordinal)) adapters.Add(new AppAdapter(tool, call));
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

    private static AppCallResult InvalidResponse() => new("KillerShell returned an invalid response", true);
    private static string ErrorText(ProcessResult response) => Truncate(string.IsNullOrWhiteSpace(response.StandardError) ? "KillerShell command failed" : response.StandardError.Trim());
    private static string Truncate(string value) => value.Length <= 1024 ? value : value[..1024];
}
