using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KillerMCP.Runtime;

public static partial class KillerScanAdapter
{
    public static async Task<IReadOnlyList<AppAdapter>> CreateAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path)) return [];
        var help = await Run(path!, ["/help"], 8, 65536, cancellationToken).ConfigureAwait(false);
        if (help.ExitCode != 0) return [];
        var adapters = new List<AppAdapter>();
        Add(help, "/network", adapters, Tool("killerscan_local_network", "Use KillerScan to identify the active local IPv4 network, interface, gateway, and DNS server without scanning hosts.", EmptySchema()), (input, token) => Network(path!, input, token));
        Add(help, "/scan [targets]", adapters, Tool("killerscan_scan_network", "Scan the active local IPv4 network, an IPv4 host, or a CIDR with KillerScan and return discovered devices as JSON. Omit target for requests such as \"killerscan my network\". Runs quick discovery by default; set full for fingerprinting and port checks. Scanning sends network probes.", ScanSchema()), (input, token) => Scan(path!, input, token));
        Add(help, "/probe <IPv4>", adapters, Tool("killerscan_probe_host", "Deep probe one IPv4 host with KillerScan, including ports 1 through 1024, and return its device details as JSON. Probing sends network traffic to the selected host.", ProbeSchema()), (input, token) => Probe(path!, input, token));
        Add(help, "/vendor <MAC>", adapters, Tool("killerscan_mac_vendor", "Look up the manufacturer of a MAC address in KillerScan's bundled offline OUI database.", VendorSchema()), (input, token) => Vendor(path!, input, token));
        Add(help, "/ping <target>", adapters, Tool("killerscan_ping", "Send a bounded set of ICMP checks to one IPv4 address or hostname with KillerScan and return latency and packet loss as JSON.", TargetIntegerSchema("count", 1, 20, 4)), (input, token) => Ping(path!, input, token));
        Add(help, "/trace <target>", adapters, Tool("killerscan_trace_route", "Trace the network path to one IPv4 address or hostname with KillerScan and return up to 64 hops as JSON.", TargetIntegerSchema("maxHops", 1, 64, 30)), (input, token) => Trace(path!, input, token));
        Add(help, "/diagnose <target>", adapters, Tool("killerscan_diagnose_host", "Check DNS, ping, route selection, and selected TCP ports for one IPv4 address or hostname with KillerScan.", DiagnoseSchema()), (input, token) => Diagnose(path!, input, token));
        Add(help, "/watch <targets>", adapters, Tool("killerscan_watch_hosts", "Sample availability and latency for 1 to 16 IPv4 addresses with KillerScan. Sends repeated ICMP checks for the requested bounded interval.", WatchSchema()), (input, token) => Watch(path!, input, token));
        Add(help, "/speedtest", adapters, Tool("killerscan_speed_test", "Run KillerScan's native KillerSpeed test. This contacts speed.killerscan.net and can transfer up to 6 GiB of generated test data plus network overhead.", EmptySchema()), (input, token) => JsonCommand(path!, input, [], ["/speedtest", "/json"], 120, _ => null, token));
        return adapters;
    }

    private static async Task<AppCallResult> Network(string path, JsonElement input, CancellationToken token)
    {
        if (input.ValueKind != JsonValueKind.Object || input.EnumerateObject().Any()) return Error("This tool takes no arguments");
        var response = await Run(path, ["/network"], 15, 8192, token).ConfigureAwait(false);
        if (response.ExitCode != 0) return ProcessError(response);
        var labels = new Dictionary<string, string> { ["INTERFACE"] = "interface", ["LOCAL IP"] = "localIp", ["SUBNET"] = "subnet", ["GATEWAY"] = "gateway", ["DNS"] = "dns" };
        var result = new JsonObject();
        foreach (var line in response.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = NetworkLine().Match(line);
            if (match.Success) result[labels[match.Groups[1].Value]] = match.Groups[2].Value;
        }
        return result.Count == labels.Count ? Ok(result.ToJsonString()) : Error("KillerScan returned an incomplete network response");
    }

    private static Task<AppCallResult> Scan(string path, JsonElement input, CancellationToken token)
    {
        if (!ObjectWith(input, "target", "full", "limit")) return Task.FromResult(Error("Expected scan arguments"));
        string? target = null;
        if (input.TryGetProperty("target", out var targetValue))
        {
            if (targetValue.ValueKind != JsonValueKind.String || !ValidScanTarget(targetValue.GetString()!, out target)) return Task.FromResult(Error("Target must be an IPv4 host or CIDR with at most 1024 addresses"));
        }
        if (!OptionalBoolean(input, "full", out var full)) return Task.FromResult(Error("Full must be true or false"));
        if (!OptionalInteger(input, "limit", 1, 100, 100, out var limit)) return Task.FromResult(Error("Limit must be between 1 and 100"));
        var args = new List<string> { "/scan" };
        if (target is not null) args.Add(target);
        args.AddRange(["/json", "/timeout", "60", "/limit", limit.ToString()]);
        if (!full) args.Add("/quick");
        return JsonArray(path, args, 70, 100, "scan", token);
    }

    private static Task<AppCallResult> Probe(string path, JsonElement input, CancellationToken token)
    {
        if (!ObjectWith(input, "target", "timeout") || !RequiredString(input, "target", out var target) || !IsIpv4(target!)) return Task.FromResult(Error("Target must be one IPv4 address"));
        if (!OptionalInteger(input, "timeout", 5, 300, 60, out var timeout)) return Task.FromResult(Error("Timeout must be between 5 and 300 seconds"));
        return JsonArray(path, ["/probe", target!, "/json", "/timeout", timeout.ToString(), "/limit", "1"], timeout + 10, 1, "probe", token);
    }

    private static async Task<AppCallResult> Vendor(string path, JsonElement input, CancellationToken token)
    {
        if (!ObjectWith(input, "mac") || !RequiredString(input, "mac", out var mac) || mac!.Count(Uri.IsHexDigit) != 12) return Error("MAC address must contain 12 hexadecimal digits");
        var response = await Run(path, ["/vendor", mac!], 15, 8192, token).ConfigureAwait(false);
        var value = response.StandardOutput.Trim();
        if (response.ExitCode != 0 && value.Length == 0) return ProcessError(response);
        return Ok(JsonSerializer.Serialize(new { mac, vendor = value.Length == 0 ? "Unknown" : value }));
    }

    private static Task<AppCallResult> Ping(string path, JsonElement input, CancellationToken token) => TargetCommand(path, input, "count", 1, 20, 4, "/ping", "/count", 120, "Count must be between 1 and 20", token);
    private static Task<AppCallResult> Trace(string path, JsonElement input, CancellationToken token) => TargetCommand(path, input, "maxHops", 1, 64, 30, "/trace", "/max-hops", 180, "Max hops must be between 1 and 64", token);

    private static Task<AppCallResult> TargetCommand(string path, JsonElement input, string property, int minimum, int maximum, int defaultValue, string command, string flag, int timeout, string rangeError, CancellationToken token)
    {
        if (!ObjectWith(input, "target", property) || !RequiredString(input, "target", out var target) || !ValidTarget(target!)) return Task.FromResult(Error("Target must be one IPv4 address or hostname"));
        if (!OptionalInteger(input, property, minimum, maximum, defaultValue, out var value)) return Task.FromResult(Error(rangeError));
        return JsonCommand(path, input, ["target", property], [command, target!, flag, value.ToString(), "/json"], timeout, _ => null, token);
    }

    private static Task<AppCallResult> Diagnose(string path, JsonElement input, CancellationToken token)
    {
        if (!ObjectWith(input, "target", "ports") || !RequiredString(input, "target", out var target) || !ValidTarget(target!)) return Task.FromResult(Error("Target must be one IPv4 address or hostname"));
        var args = new List<string> { "/diagnose", target! };
        if (input.TryGetProperty("ports", out var ports))
        {
            if (ports.ValueKind != JsonValueKind.Array || ports.GetArrayLength() > 32 || ports.EnumerateArray().Any(port => port.ValueKind != JsonValueKind.Number || !port.TryGetInt32(out var number) || number is < 1 or > 65535)) return Task.FromResult(Error("Ports must contain up to 32 port numbers from 1 to 65535"));
            if (ports.GetArrayLength() > 0) args.AddRange(["/ports", string.Join(',', ports.EnumerateArray().Select(port => port.GetInt32()))]);
        }
        args.Add("/json");
        return JsonCommand(path, input, ["target", "ports"], args, 120, _ => null, token);
    }

    private static Task<AppCallResult> Watch(string path, JsonElement input, CancellationToken token)
    {
        if (!ObjectWith(input, "targets", "count", "interval") || !input.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() is < 1 or > 16 || targets.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || !IsIpv4(item.GetString()!))) return Task.FromResult(Error("Targets must contain 1 to 16 IPv4 addresses"));
        if (!OptionalInteger(input, "count", 1, 20, 4, out var count)) return Task.FromResult(Error("Count must be between 1 and 20"));
        if (!OptionalInteger(input, "interval", 1, 10, 1, out var interval)) return Task.FromResult(Error("Interval must be between 1 and 10 seconds"));
        var args = new List<string> { "/watch" };
        args.AddRange(targets.EnumerateArray().Select(item => item.GetString()!));
        args.AddRange(["/count", count.ToString(), "/interval", interval.ToString(), "/json"]);
        return JsonCommand(path, input, ["targets", "count", "interval"], args, 220, _ => null, token);
    }

    private static async Task<AppCallResult> JsonArray(string path, IEnumerable<string> args, int timeout, int maximum, string label, CancellationToken token)
    {
        var response = await Run(path, args, timeout, 1048576, token).ConfigureAwait(false);
        if (response.ExitCode != 0) return ProcessError(response);
        try
        {
            using var document = JsonDocument.Parse(response.StandardOutput);
            return !response.OutputExceeded && document.RootElement.ValueKind == JsonValueKind.Array && document.RootElement.GetArrayLength() <= maximum
                ? Ok(document.RootElement.GetRawText())
                : Error($"KillerScan returned an invalid {label} response");
        }
        catch (JsonException) { return Error($"KillerScan returned an invalid {label} response"); }
    }

    private static async Task<AppCallResult> JsonCommand(string path, JsonElement input, string[] allowed, IEnumerable<string> args, int timeout, Func<JsonElement, string?> validate, CancellationToken token)
    {
        if (!ObjectWith(input, allowed)) return Error("Invalid arguments");
        var problem = validate(input);
        if (problem is not null) return Error(problem);
        var response = await Run(path, args, timeout, 1048576, token).ConfigureAwait(false);
        if (response.ExitCode != 0) return ProcessError(response);
        try
        {
            using var document = JsonDocument.Parse(response.StandardOutput);
            return response.OutputExceeded ? Error("KillerScan returned invalid JSON") : Ok(document.RootElement.GetRawText());
        }
        catch (JsonException) { return Error("KillerScan returned invalid JSON"); }
    }

    private static Task<ProcessResult> Run(string path, IEnumerable<string> args, int seconds, int maximum, CancellationToken token) => ProcessRunner.RunAsync(path, args, TimeSpan.FromSeconds(seconds), maximum, token);
    private static void Add(ProcessResult help, string marker, List<AppAdapter> adapters, RuntimeTool tool, Func<JsonElement, CancellationToken, Task<AppCallResult>> call) { if (help.StandardOutput.Contains(marker, StringComparison.Ordinal)) adapters.Add(new AppAdapter(tool, call)); }
    private static RuntimeTool Tool(string name, string description, JsonObject schema) => new(name, description, schema);
    private static bool ObjectWith(JsonElement input, params string[] allowed) => input.ValueKind == JsonValueKind.Object && input.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal));
    private static bool RequiredString(JsonElement input, string name, out string? value) { value = null; return input.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value = element.GetString()); }
    private static bool OptionalBoolean(JsonElement input, string name, out bool value) { value = false; return !input.TryGetProperty(name, out var element) || (element.ValueKind is JsonValueKind.True or JsonValueKind.False && (value = element.GetBoolean()) == value); }
    private static bool OptionalInteger(JsonElement input, string name, int min, int max, int defaultValue, out int value) { value = defaultValue; return !input.TryGetProperty(name, out var element) || (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= min && value <= max); }
    private static bool IsIpv4(string value) => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    private static bool ValidTarget(string value) => value.Length is > 0 and <= 253 && (IsIpv4(value) || Hostname().IsMatch(value));
    private static bool ValidScanTarget(string value, out string? target) { target = value; var parts = value.Split('/'); return value.Length <= 43 && parts.Length <= 2 && IsIpv4(parts[0]) && (parts.Length == 1 || int.TryParse(parts[1], out var prefix) && prefix is >= 22 and <= 32); }
    private static bool IsFile(string? path) { try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); } catch { return false; } }
    private static AppCallResult Ok(string text) => new(text);
    private static AppCallResult Error(string text) => new(text, true);
    private static AppCallResult ProcessError(ProcessResult response) { var text = string.IsNullOrWhiteSpace(response.StandardError) ? "KillerScan command failed" : response.StandardError.Trim(); return Error(text.Length <= 1024 ? text : text[..1024]); }

    private static JsonObject EmptySchema() => new() { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false };
    private static JsonObject ScanSchema() => Schema(new JsonObject { ["target"] = new JsonObject { ["type"] = "string" }, ["full"] = new JsonObject { ["type"] = "boolean", ["default"] = false }, ["limit"] = Integer(1, 100, 100) });
    private static JsonObject ProbeSchema() => Schema(new JsonObject { ["target"] = new JsonObject { ["type"] = "string" }, ["timeout"] = Integer(5, 300, 60) }, "target");
    private static JsonObject VendorSchema() => Schema(new JsonObject { ["mac"] = new JsonObject { ["type"] = "string" } }, "mac");
    private static JsonObject TargetIntegerSchema(string property, int min, int max, int defaultValue) => Schema(new JsonObject { ["target"] = new JsonObject { ["type"] = "string" }, [property] = Integer(min, max, defaultValue) }, "target");
    private static JsonObject DiagnoseSchema() => Schema(new JsonObject { ["target"] = new JsonObject { ["type"] = "string" }, ["ports"] = new JsonObject { ["type"] = "array", ["items"] = Integer(1, 65535), ["maxItems"] = 32 } }, "target");
    private static JsonObject WatchSchema() => Schema(new JsonObject { ["targets"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["minItems"] = 1, ["maxItems"] = 16 }, ["count"] = Integer(1, 20, 4), ["interval"] = Integer(1, 10, 1) }, "targets");
    private static JsonObject Schema(JsonObject properties, params string[] required) { var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false }; if (required.Length > 0) schema["required"] = new JsonArray(required.Select(value => JsonValue.Create(value)).ToArray()); return schema; }
    private static JsonObject Integer(int min, int max, int? defaultValue = null) { var value = new JsonObject { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max }; if (defaultValue.HasValue) value["default"] = defaultValue.Value; return value; }

    [GeneratedRegex("^(INTERFACE|LOCAL IP|SUBNET|GATEWAY|DNS)\\s{2,}(.+)$", RegexOptions.CultureInvariant)] private static partial Regex NetworkLine();
    [GeneratedRegex("^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex Hostname();
}
