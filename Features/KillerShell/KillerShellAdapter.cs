using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KillerMCP;

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
        // Older KillerShell builds do not know these options, so the inputs exist only when the help names them.
        bool detail = help.StandardOutput.Contains("--detail", StringComparison.Ordinal);
        bool filter = help.StandardOutput.Contains("--filter", StringComparison.Ordinal);
        string maskNote = " Masking is best effort: values after password, token, secret and key switches, URL passwords, Authorization headers and Password= style connection string keys are replaced with ***, but detail can still expose secrets in unusual forms, so leave detail off unless it is needed.";
        Add(help, "processes [", adapters,
            new RuntimeTool("killershell_list_processes",
                "List running processes with their process IDs and memory use."
                + (detail ? " Detail is off by default; with detail=true each row also exposes the process path and full command line (cut to " + DetailTextLimit.ToString(CultureInfo.InvariantCulture) + " characters)." + maskNote : "")
                + " This tool cannot start or stop processes.",
                LimitSchema(100,
                    detail ? "Add the parent process ID, path, command line (secret-looking values masked) and start time to each row" : null,
                    filter ? "Only processes whose name contains this text (case-insensitive) or whose PID equals it exactly" + (detail ? "; with detail=true the path and command line are matched as well" : "") : null)),
            (input, token) => ProcessOrServiceAsync(path!, "processes", input, detail, filter, token));
        Add(help, "services [", adapters,
            new RuntimeTool("killershell_list_services",
                "List Windows services and their current status."
                + (detail ? " Detail is off by default; with detail=true each row also exposes the service path, which is its command line, and its logon account (the path is cut to " + DetailTextLimit.ToString(CultureInfo.InvariantCulture) + " characters)." + maskNote : "")
                + " This tool cannot start, stop, or reconfigure services.",
                LimitSchema(100,
                    detail ? "Add the start mode, path (the service command line, secret-looking values masked), logon account and description to each row" : null,
                    filter ? "Only services whose name or display name contains this text (case-insensitive)" : null)),
            (input, token) => ProcessOrServiceAsync(path!, "services", input, detail, filter, token));
        Add(help, "events <", adapters,
            new RuntimeTool("killershell_read_event_log", "Read recent entries from the local Application, System, or Security event log. This tool cannot clear or change logs.", EventSchema()),
            (input, token) => EventsAsync(path!, input, token));
        Add(help, "registry <", adapters,
            new RuntimeTool("killershell_read_registry_key", "List bounded subkeys and values from one local registry key. This tool cannot create, edit, rename, or delete registry data.", RegistrySchema()),
            (input, token) => RegistryAsync(path!, input, token));
        Add(help, "drives", adapters,
            new RuntimeTool("killershell_list_drives", "List local drives with type, readiness, capacity, and free space. This tool does not scan file contents.", EmptySchema()),
            (input, token) => EmptyCommandAsync(path!, "drives", "drives", input, token));
        Add(help, "hash <file>", adapters,
            new RuntimeTool("killershell_hash_file", "Calculate the SHA-256 hash of one local file without changing it.", PathSchema("Absolute path to a local file")),
            (input, token) => HashAsync(path!, input, token));
        if (AppVersion.IsAtLeast(path!, StorageMinimumVersion))
        {
            // A KillerShell build that names --max-seconds stops its own scan, so the host can wait a little longer for it.
            bool timeLimit = help.StandardOutput.Contains("--max-seconds", StringComparison.Ordinal);
            Add(help, "storage <", adapters,
                new RuntimeTool("killershell_storage_report", "Report the largest folders and files under one local directory with KillerShell. Read-only: it scans folder sizes and never changes, moves, or deletes anything." + (timeLimit ? " A scan that runs past " + StorageScanSeconds.ToString(CultureInfo.InvariantCulture) + " seconds stops early and the report says truncated." : ""), PathSchema("Absolute directory path to scan", includeLimit: true)),
                (input, token) => StorageAsync(path!, input, timeLimit, token));
        }
        return adapters;
    }

    private static readonly Version StorageMinimumVersion = new(1, 5, 1);

    // KillerShell stops a storage scan after this many seconds and reports it truncated; the host waits ten seconds longer.
    private const int StorageScanSeconds = 25;
    internal static readonly TimeSpan StorageTimeout = TimeSpan.FromSeconds(35);

    internal static List<string> StorageArguments(string target, int limit, bool timeLimit)
    {
        var arguments = new List<string> { "storage", target, "--limit", limit.ToString(CultureInfo.InvariantCulture) };
        if (timeLimit) arguments.AddRange(["--max-seconds", StorageScanSeconds.ToString(CultureInfo.InvariantCulture)]);
        return arguments;
    }

    // A detail row can carry a command line of up to 32,767 characters and a listing holds 100 rows,
    // so detail reads more than the plain cap. What reaches the model is cut down again below.
    internal static int OutputLimit(bool detail) => detail ? 4194304 : 262144;

    private const int DetailTextLimit = 1024;

    private static readonly TimeSpan MaskTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions MaskOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex[] SecretShapes =
    [
        // Any switch whose name holds a secret word (--password x, --db-password=x, --client-secret x,
        // /token x, --api-key "x y", --password "a\"b c"), with - -- or / in front. The value is a quoted
        // string with backslash escapes, a single-quoted string, or text up to the next space.
        new(@"(?<![\w-])((?:--?|/)[\w-]*?(?:password|passwd|pwd|token|secret|api-?key|api_key|credential)[\w-]*(?:\s*[=:]\s*|\s+))(""(?:\\.|[^""\\])*""|'[^']*'|[^\s""]+|""[^\r\n]*)", MaskOptions, MaskTimeout),
        // https://user:pass@host: only the password part goes.
        new(@"(\b[A-Za-z][A-Za-z0-9+.-]*://[^\s/:@""']*:)[^\s/""']*(?=@)", MaskOptions, MaskTimeout),
        // Password=x; Pwd=x; AccountKey=x; SharedAccessKey=x; SharedAccessSignature=x; client_secret=x and similar
        // keys in a connection string. A value is {braces} (}} is a brace), a quoted string, or text up to the next semicolon or quote.
        new(@"(?<![\w-])([A-Za-z0-9_]*(?:Password|Passwd|Pwd|AccountKey|SharedAccessKey|SharedAccessSignature|client[-_]?secret|api[-_]?key|secret|token)\s*=\s*)(\{(?:[^}]|\}\})*\}|""(?:\\.|[^""\\])*""|'[^']*'|[^;""]*)", MaskOptions, MaskTimeout),
    ];

    private static readonly Regex AuthorizationStart = new(@"(?<q>[""'])?Authorization(?<k>[""'])?\s*[:=]\s*(?<v>[""'])?", MaskOptions, MaskTimeout);
    private static readonly Regex AuthorizationScheme = new(@"^(?:Bearer|Basic|Digest|Negotiate|NTLM|Token|AWS4-[\w-]+)\s+(?=\S)", MaskOptions, MaskTimeout);

    // Authorization: everything after the colon up to the end of the header value or of the quoted argument it sits in
    // is masked. A known scheme word stays, since it is not secret and says what kind of credential was there.
    private static string MaskAuthorization(string value)
    {
        var result = new System.Text.StringBuilder();
        int position = 0;
        foreach (Match match in AuthorizationStart.Matches(value))
        {
            if (match.Index < position) continue;
            char quote = match.Groups["v"].Success ? match.Groups["v"].Value[0] : match.Groups["q"].Success ? match.Groups["q"].Value[0] : '\0';
            int start = match.Index + match.Length;
            int end = start;
            while (end < value.Length)
            {
                char c = value[end];
                if (quote == '\0' ? c is '\r' or '\n' : c == quote) break;
                if (quote == '"' && c == '\\' && end + 1 < value.Length) end++;
                end++;
            }
            end = Math.Min(end, value.Length);
            if (end == start) continue;
            var scheme = AuthorizationScheme.Match(value.Substring(start, end - start));
            int keep = scheme.Success ? scheme.Length : 0;
            result.Append(value, position, start + keep - position).Append("***");
            position = end;
        }
        return position == 0 ? value : result.Append(value, position, value.Length - position).ToString();
    }

    /// <summary>Replaces the value of a secret-looking argument with ***. It matches the common shapes
    /// only, so it is a safety net and not a promise that a command line holds nothing private.</summary>
    internal static string MaskSecrets(string value)
    {
        try
        {
            value = MaskAuthorization(value);
            foreach (var shape in SecretShapes) value = shape.Replace(value, "$1***");
        }
        catch (RegexMatchTimeoutException) { return "*** (command line withheld)"; }
        return value;
    }

    private static string ProtectText(string value)
    {
        value = MaskSecrets(value);
        if (value.Length <= DetailTextLimit) return value;
        // Never cut between the halves of a surrogate pair, which would make the row impossible to write as JSON.
        int cut = char.IsHighSurrogate(value[DetailTextLimit - 1]) ? DetailTextLimit - 1 : DetailTextLimit;
        return value[..cut] + "...";
    }

    /// <summary>Masks and shortens the command line of each process row and the path (the command line)
    /// of each service row. A response in any other shape goes back unchanged.</summary>
    internal static string ProtectDetailRows(string json, string command)
    {
        string field = command == "processes" ? "commandLine" : "path";
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root[command] is not JsonArray rows) return json;
            foreach (var row in rows.OfType<JsonObject>())
                if (row[field] is JsonValue value && value.TryGetValue<string>(out var text))
                    row[field] = ProtectText(text);
            return root.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
        catch (JsonException) { return json; }
    }

    private static Task<AppCallResult> StorageAsync(string path, JsonElement input, bool timeLimit, CancellationToken cancellationToken)
    {
        if (!ValidatePathInput(input, allowLimit: true, allowMaximum: false, out var target, out var limit))
            return Task.FromResult(new AppCallResult("Expected an absolute directory path and an optional limit from 1 to 100", true));
        return RunJsonAsync(path, StorageArguments(target!, limit, timeLimit), "topFolders", JsonValueKind.Array, 262144, cancellationToken,
            timeout: timeLimit ? StorageTimeout : null);
    }

    private static async Task<AppCallResult> ProcessOrServiceAsync(string path, string command, JsonElement input, bool detailAllowed, bool filterAllowed, CancellationToken cancellationToken)
    {
        const string Expected = "Expected an optional limit from 1 to 100, an optional detail flag, and an optional filter text";
        if (input.ValueKind != JsonValueKind.Object
            || input.EnumerateObject().Any(item => item.Name is not ("limit" or "detail" or "filter")
                || (item.Name == "detail" && !detailAllowed) || (item.Name == "filter" && !filterAllowed))
            || !ReadOptionalLimit(input, 100, 100, out var limit))
            return new AppCallResult(Expected, true);
        var arguments = new List<string> { command, "--limit", limit.ToString(CultureInfo.InvariantCulture) };
        bool detail = false;
        if (input.TryGetProperty("detail", out var detailValue))
        {
            if (detailValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return new AppCallResult(Expected, true);
            detail = detailValue.GetBoolean();
            if (detail) arguments.Add("--detail");
        }
        if (input.TryGetProperty("filter", out _))
        {
            if (!ReadString(input, "filter", 256, out var text)) return new AppCallResult(Expected, true);
            arguments.AddRange(["--filter", text!]);
        }
        var result = await RunJsonAsync(path, arguments, command, JsonValueKind.Array, OutputLimit(detail), cancellationToken).ConfigureAwait(false);
        return detail && !result.IsError ? new AppCallResult(ProtectDetailRows(result.Text, command)) : result;
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

    private static Task<AppCallResult> LimitedCommandAsync(string path, string command, string property, JsonElement input, int maximum, CancellationToken cancellationToken)
    {
        if (!ValidateLimitOnly(input, maximum, out var limit))
            return Task.FromResult(new AppCallResult("Expected an optional limit from 1 to " + maximum.ToString(CultureInfo.InvariantCulture), true));
        return RunJsonAsync(path, [command, "--limit", limit.ToString(CultureInfo.InvariantCulture)], property, JsonValueKind.Array, 262144, cancellationToken);
    }

    private static Task<AppCallResult> EventsAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(item => item.Name is not ("log" or "limit"))
            || !ReadString(input, "log", 16, out var log) || log is not ("Application" or "System" or "Security")
            || !ReadOptionalLimit(input, 100, 50, out var limit))
            return Task.FromResult(new AppCallResult("Expected log Application, System, or Security and an optional limit from 1 to 100", true));
        return RunJsonAsync(path, ["events", log!, "--limit", limit.ToString(CultureInfo.InvariantCulture)], "events", JsonValueKind.Array, 524288, cancellationToken);
    }

    private static Task<AppCallResult> RegistryAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(item => item.Name is not ("path" or "limit"))
            || !ReadString(input, "path", 1024, out var key) || !ReadOptionalLimit(input, 100, 100, out var limit))
            return Task.FromResult(new AppCallResult("Expected a registry key path and an optional limit from 1 to 100", true));
        return RunJsonAsync(path, ["registry", key!, "--limit", limit.ToString(CultureInfo.InvariantCulture)], "values", JsonValueKind.Array, 524288, cancellationToken);
    }

    private static Task<AppCallResult> EmptyCommandAsync(string path, string command, string property, JsonElement input, CancellationToken cancellationToken)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any())
            return Task.FromResult(new AppCallResult("Expected no arguments", true));
        return RunJsonAsync(path, [command], property, JsonValueKind.Array, 65536, cancellationToken);
    }

    private static Task<AppCallResult> HashAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (!ValidatePathInput(input, false, false, out var target, out _))
            return Task.FromResult(new AppCallResult("Expected one absolute file path", true));
        return RunJsonAsync(path, ["hash", target!], "hash", JsonValueKind.String, 32768, cancellationToken);
    }

    private static async Task<AppCallResult> RunJsonAsync(string path, IEnumerable<string> arguments, string property, JsonValueKind kind, int maximumOutput, CancellationToken cancellationToken, JsonValueKind? alternateKind = null, TimeSpan? timeout = null)
    {
        try
        {
            var response = await RunAsync(path, arguments, timeout ?? TimeSpan.FromSeconds(10), maximumOutput, cancellationToken).ConfigureAwait(false);
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

    // A null description leaves that input out, for a KillerShell build that does not offer it.
    private static JsonObject LimitSchema(int maximum, string? detailDescription = null, string? filterDescription = null)
    {
        var properties = new JsonObject { ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = maximum, ["default"] = Math.Min(100, maximum) } };
        if (detailDescription != null) properties["detail"] = new JsonObject { ["type"] = "boolean", ["default"] = false, ["description"] = detailDescription };
        if (filterDescription != null) properties["filter"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256, ["description"] = filterDescription };
        return new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
    }

    private static JsonObject EventSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["log"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Application", "System", "Security") },
            ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["default"] = 50 },
        },
        ["required"] = new JsonArray("log"),
        ["additionalProperties"] = false,
    };

    private static JsonObject RegistrySchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["path"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1024, ["description"] = "Registry path beginning with a full HKEY name" },
            ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["default"] = 100 },
        },
        ["required"] = new JsonArray("path"),
        ["additionalProperties"] = false,
    };

    private static JsonObject EmptySchema() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };

    private static bool ValidateLimitOnly(JsonElement input, int maximum, out int limit)
    {
        limit = Math.Min(100, maximum);
        return input.ValueKind == JsonValueKind.Object
            && !input.EnumerateObject().Any(item => item.Name != "limit")
            && ReadOptionalLimit(input, maximum, limit, out limit);
    }

    private static bool ReadOptionalLimit(JsonElement input, int maximum, int fallback, out int limit)
    {
        limit = fallback;
        return !input.TryGetProperty("limit", out var value)
            || value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out limit) && limit >= 1 && limit <= maximum;
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
