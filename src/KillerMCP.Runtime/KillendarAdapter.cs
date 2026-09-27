using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

public static class KillendarAdapter
{
    private static readonly Version MinimumVersion = new(1, 1, 4);

    public static async Task<AppAdapter?> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path) || !AppVersion.IsAtLeast(path!, MinimumVersion))
        {
            return null;
        }

        var help = await RunAsync(path!, ["--help"], TimeSpan.FromSeconds(8), 8192, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0 || !help.StandardOutput.Contains("agenda <yyyy-MM-dd>", StringComparison.Ordinal))
        {
            return null;
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["date"] = new JsonObject { ["type"] = "string", ["description"] = "Start date in YYYY-MM-DD format" },
                ["days"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 31, ["default"] = 7 },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["default"] = 50 },
            },
            ["required"] = new JsonArray("date"),
            ["additionalProperties"] = false,
        };
        var tool = new RuntimeTool(
            "killendar_agenda",
            "Read appointments from the active Killendar calendar, including recurring events, without changing the calendar. Use for requests such as \"killer what is on my calendar this week\". Encrypted calendars currently require an app unlock path and are unavailable through this tool.",
            schema);
        return new AppAdapter(tool, (input, token) => CallAsync(path!, input, token));
    }

    private static async Task<AppCallResult> CallAsync(string path, JsonElement input, CancellationToken cancellationToken)
    {
        if (!ValidInput(input, out var date, out var days, out var limit))
        {
            return new AppCallResult("Provide a valid date, up to 31 days, and a limit up to 100 events", true);
        }

        try
        {
            var response = await RunAsync(path, ["agenda", date!, days.ToString(CultureInfo.InvariantCulture), "--limit", limit.ToString(CultureInfo.InvariantCulture)], TimeSpan.FromSeconds(20), 262144, cancellationToken).ConfigureAwait(false);
            if (response.ExitCode != 0)
            {
                return new AppCallResult(ErrorText(response), true);
            }
            if (response.OutputExceeded)
            {
                return InvalidResponse();
            }

            using var document = JsonDocument.Parse(response.StandardOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 100)
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

    private static bool ValidInput(JsonElement input, out string? date, out int days, out int limit)
    {
        date = null;
        days = 7;
        limit = 50;
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(property => property.Name is not ("date" or "days" or "limit")))
        {
            return false;
        }
        if (!input.TryGetProperty("date", out var dateValue) || dateValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        date = dateValue.GetString();
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }
        if (!ReadBoundedInteger(input, "days", 1, 31, ref days) || !ReadBoundedInteger(input, "limit", 1, 100, ref limit))
        {
            return false;
        }

        return true;
    }

    private static bool ReadBoundedInteger(JsonElement input, string name, int minimum, int maximum, ref int value)
    {
        if (!input.TryGetProperty(name, out var element))
        {
            return true;
        }
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= minimum && value <= maximum;
    }

    private static Task<ProcessResult> RunAsync(string path, IEnumerable<string> arguments, TimeSpan timeout, int maximumCharacters, CancellationToken cancellationToken)
    {
        var prefix = path.EndsWith("Killendar.exe", StringComparison.OrdinalIgnoreCase) ? new[] { "--cli" } : Array.Empty<string>();
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

    private static AppCallResult InvalidResponse() => new("Killendar returned an invalid agenda response", true);
    private static string ErrorText(ProcessResult response) => Truncate(string.IsNullOrWhiteSpace(response.StandardError) ? "Killendar command failed" : response.StandardError.Trim());
    private static string Truncate(string value) => value.Length <= 1024 ? value : value[..1024];
}
