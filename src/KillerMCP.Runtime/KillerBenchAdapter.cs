using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP.Runtime;

public static partial class KillerBenchAdapter
{
    public static IReadOnlyList<AppAdapter> Create(string? path)
    {
        if (!IsFile(path))
        {
            return [];
        }

        return
        [
            Create(path!, "killerbench_device_code", "Look up a Device Manager problem code in KillerBench without changing the device.", "device-code"),
            Create(path!, "killerbench_win32_code", "Look up a Windows system error code in KillerBench without changing the system.", "win32-code"),
        ];
    }

    private static AppAdapter Create(string path, string name, string description, string command) => new(
        new RuntimeTool(name, description, CodeSchema()),
        (input, token) => CallAsync(path, command, input, token));

    private static async Task<AppCallResult> CallAsync(string path, string command, JsonElement input, CancellationToken cancellationToken)
    {
        if (!ValidCode(input, out var code))
        {
            return new AppCallResult("Code must be an unsigned decimal or 0x hexadecimal value", true);
        }

        try
        {
            var response = await ProcessRunner.RunAsync(path, [command, code!], TimeSpan.FromSeconds(15), 65536, cancellationToken).ConfigureAwait(false);
            if (response.ExitCode is not (0 or 3))
            {
                return new AppCallResult(Truncate(string.IsNullOrWhiteSpace(response.StandardError) ? "KillerBench command failed" : response.StandardError.Trim()), true);
            }
            if (response.OutputExceeded)
            {
                return InvalidResponse();
            }

            using var document = JsonDocument.Parse(response.StandardOutput);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("code", out _))
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

    private static bool ValidCode(JsonElement input, out string? code)
    {
        code = null;
        if (input.ValueKind != JsonValueKind.Object
            || input.EnumerateObject().Any(property => property.Name != "code")
            || !input.TryGetProperty("code", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        code = value.GetString();
        if (code is null || !CodePattern().IsMatch(code))
        {
            return false;
        }

        return code.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(code.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _)
            : uint.TryParse(code, out _);
    }

    private static JsonObject CodeSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["code"] = new JsonObject { ["type"] = "string", ["pattern"] = "^(?:[0-9]{1,10}|0[xX][0-9a-fA-F]{1,8})$" },
        },
        ["required"] = new JsonArray("code"),
        ["additionalProperties"] = false,
    };

    private static bool IsFile(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); }
        catch { return false; }
    }

    private static AppCallResult InvalidResponse() => new("KillerBench returned an invalid lookup response", true);
    private static string Truncate(string value) => value.Length <= 1024 ? value : value[..1024];

    [GeneratedRegex("^(?:[0-9]{1,10}|0[xX][0-9a-fA-F]{1,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
