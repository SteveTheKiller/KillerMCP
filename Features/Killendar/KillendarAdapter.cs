using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP;

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
            help.StandardOutput.Contains("app-pipe-v1", StringComparison.Ordinal)
                ? "Read appointments, including recurring events, from the currently open and unlocked Killendar. Supports encrypted calendars without exposing their keys."
                : "Read appointments from the active Killendar calendar without changing it. This older CLI cannot read encrypted calendars; update Killendar for unlocked-app reads.",
            schema);
        bool pipeReads = help.StandardOutput.Contains("app-pipe-v1", StringComparison.Ordinal);
        return new AppAdapter(tool, (input, token) => CallAsync(path!, input, token, pipeReads));
    }

    public static async Task<AppAdapter?> CreateAppointmentAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path) || !AppVersion.IsAtLeast(path!, MinimumVersion)) return null;
        var help = await RunAsync(path!, ["--help"], TimeSpan.FromSeconds(8), 8192, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0 || !help.StandardOutput.Contains("appointment-request-id-v1", StringComparison.Ordinal)) return null;

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200 },
                ["requestId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid", ["description"] = "Generate one nonzero UUID for this appointment. Reuse it unchanged for every retry." },
                ["start"] = new JsonObject { ["type"] = "string", ["description"] = "Local start time in YYYY-MM-DDTHH:MM format, or YYYY-MM-DD for an all-day appointment" },
                ["end"] = new JsonObject { ["type"] = "string", ["description"] = "Local end time, or the exclusive end date for an all-day appointment" },
                ["allDay"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["location"] = new JsonObject { ["type"] = "string", ["maxLength"] = 500 },
                ["description"] = new JsonObject { ["type"] = "string", ["maxLength"] = 4000 },
                ["categories"] = new JsonObject { ["type"] = "string", ["maxLength"] = 500 },
            },
            ["required"] = new JsonArray("requestId", "title", "start", "end"),
            ["additionalProperties"] = false,
        };
        return new AppAdapter(new RuntimeTool("killendar_create_appointment",
            "Create one appointment in the currently open and unlocked Killendar. This writes calendar data. For all-day appointments, end is the next date after the last included day.", schema),
            CreateAppointmentCallAsync);
    }

    private static async Task<AppCallResult> CreateAppointmentCallAsync(JsonElement input, CancellationToken token)
    {
        if (!ValidAppointment(input)) return new AppCallResult("Provide a nonzero requestId UUID, title, and valid local start and end times", true);
        if (CalendarPipeMissing()) return new AppCallResult("Open and unlock Killendar before using calendar commands", true);
        const string uncertain = "Killendar appointment outcome unknown. Retry with the same requestId and appointment details.";
        bool dispatched = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var pipe = new NamedPipeClientStream(".", NotesContent.ResolvePipeName(CalendarPipeName), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(2000, timeout.Token).ConfigureAwait(false);
            byte[] request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input) + "\n");
            timeout.Token.ThrowIfCancellationRequested();
            dispatched = true;
            await pipe.WriteAsync(request, timeout.Token).ConfigureAwait(false);
            byte[] response = new byte[32768];
            int length = 0;
            while (length < response.Length)
            {
                int count = await pipe.ReadAsync(response.AsMemory(length), timeout.Token).ConfigureAwait(false);
                if (count == 0) break;
                int end = Array.IndexOf(response, (byte)'\n', length, count);
                if (end >= 0)
                {
                    using var document = JsonDocument.Parse(response.AsMemory(0, end));
                    var reply = document.RootElement;
                    if (reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("id", out _))
                        return new AppCallResult(reply.GetRawText());
                    if (reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                        return new AppCallResult(Truncate(error.GetString()!), true);
                    break;
                }
                length += count;
            }
            return new AppCallResult(uncertain, true);
        }
        catch (Exception exception) when (exception is IOException or JsonException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            return new AppCallResult(dispatched ? uncertain : Truncate(exception.Message), true);
        }
    }

    private static bool ValidAppointment(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any(property =>
                property.Name is not ("requestId" or "title" or "start" or "end" or "allDay" or "location" or "description" or "categories")) ||
            !input.TryGetProperty("requestId", out var key) || key.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(key.GetString(), "D", out var requestId) || requestId == Guid.Empty ||
            !BoundedText(input, "title", 200, true) || !BoundedText(input, "start", 19, true) ||
            !BoundedText(input, "end", 19, true) || !BoundedText(input, "location", 500, false) ||
            !BoundedText(input, "description", 4000, false) || !BoundedText(input, "categories", 500, false)) return false;
        var allDay = false;
        if (input.TryGetProperty("allDay", out var value))
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            allDay = value.GetBoolean();
        }
        var formats = allDay ? new[] { "yyyy-MM-dd" } : new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };
        return DateTime.TryParseExact(input.GetProperty("start").GetString(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
               DateTime.TryParseExact(input.GetProperty("end").GetString(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && end > start;
    }

    private static bool BoundedText(JsonElement input, string name, int max, bool required)
    {
        if (!input.TryGetProperty(name, out var value)) return !required;
        return value.ValueKind == JsonValueKind.String && value.GetString() is string text &&
               text.Length <= max && (!required || !string.IsNullOrWhiteSpace(text));
    }

    private static async Task<AppCallResult> CallAsync(string path, JsonElement input, CancellationToken cancellationToken, bool pipeReads)
    {
        if (!ValidInput(input, out var date, out var days, out var limit))
        {
            return new AppCallResult("Provide a valid date, up to 31 days, and a limit up to 100 events", true);
        }
        if (pipeReads && CalendarPipeMissing()) return new AppCallResult("Open and unlock Killendar before using calendar commands", true);

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

    private static string CalendarPipeName => "Killendar-" + System.Diagnostics.Process.GetCurrentProcess().SessionId + "-CalendarCommands";
    private static bool CalendarPipeMissing() => NotesContent.PipeMissing(CalendarPipeName);

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
