using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KillerMCP;

public static partial class KillerBenchAdapter
{
    private static async Task<HashSet<string>> ReadCapabilities(string path, CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var run = await ProcessRunner.RunAsync(path, ["capabilities"], TimeSpan.FromSeconds(15), 262144, token).ConfigureAwait(false);
            if (run.ExitCode != 0 || run.OutputExceeded) return result;
            using var document = JsonDocument.Parse(run.StandardOutput);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema) ||
                !schema.TryGetInt32(out var version) || version != 1 ||
                !root.TryGetProperty("version", out var appVersion) || appVersion.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("commands", out var names) || names.ValueKind != JsonValueKind.Array || names.GetArrayLength() > 512) return result;
            foreach (var name in names.EnumerateArray())
            {
                if (name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 and <= 80 } text ||
                    text.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')) return [];
                result.Add(text);
            }
        }
        catch (Exception error) when (error is JsonException or TimeoutException or InvalidOperationException or Win32Exception or IOException) { }
        return result;
    }

    // Only parameter-free diagnostics whose handlers inspect state are listed here.
    private static readonly Dictionary<string, string> DiagnosticChecks = new(StringComparer.Ordinal)
    {
        ["task_inventory"] = "task-inventory", ["task_check"] = "task-check", ["event_channels"] = "event-channels",
        ["startup_apps"] = "startup-apps", ["browser_extensions"] = "browser-extensions",
        ["service_binaries"] = "service-binaries", ["service_path_quoting"] = "service-path-quoting",
        ["service_logons"] = "service-logons", ["service_startup"] = "service-startup-config",
        ["service_dependencies"] = "service-dependencies", ["service_dlls"] = "service-dlls",
        ["installer"] = "installer-check", ["installer_events"] = "installer-events",
        ["update_context"] = "update-context", ["update_policy"] = "update-policy", ["hotfixes"] = "installed-hotfixes",
        ["defender_threats"] = "defender-threats", ["defender_history"] = "defender-scan-history",
        ["defender_protection"] = "defender-protection", ["firewall_rules"] = "firewall-rules",
        ["firewall_events"] = "firewall-events", ["network_profiles"] = "network-profiles",
        ["dns_client_config"] = "dns-client-config", ["dns_client_events"] = "dns-client-events",
        ["dns_policy"] = "dns-nrpt-policy", ["route_priority"] = "route-priority",
        ["ip_interfaces"] = "ip-interface-config", ["dhcp_leases"] = "dhcp-leases", ["dhcp_events"] = "dhcp-events",
        ["adapter_bindings"] = "adapter-bindings", ["adapter_statistics"] = "adapter-statistics",
        ["tcp_settings"] = "tcp-settings", ["tcp_pressure"] = "tcp-port-pressure", ["network_offload"] = "network-offload",
        ["vpn"] = "vpn-profiles", ["winrm"] = "winrm-listeners", ["smb_shares"] = "smb-shares",
        ["smb_connections"] = "smb-connections", ["smb_server"] = "smb-server-config", ["smb_client"] = "smb-client-config",
        ["physical_disks"] = "physical-disks", ["storage_reliability"] = "storage-reliability",
        ["storage_jobs"] = "storage-jobs", ["disk_quotas"] = "disk-quotas", ["shadow_storage"] = "shadow-storage",
        ["shadow_copies"] = "shadow-copies", ["vss_writers"] = "vss-writers", ["vss_providers"] = "vss-providers",
        ["backup_summary"] = "backup-summary", ["backup_events"] = "backup-events", ["file_history"] = "file-history-status",
        ["file_history_events"] = "file-history-events", ["time_events"] = "time-events", ["crash_events"] = "crash-events",
        ["crash_config"] = "crash-config", ["crash_dumps"] = "crash-dumps", ["pending_files"] = "pending-file-operations",
        ["licensing"] = "licensing-check", ["pagefile"] = "pagefile-check", ["reserved_storage"] = "reserved-storage",
        ["memory_diagnostics"] = "memory-diagnostic-results", ["service_triggers"] = "service-triggers",
        ["service_recovery_policy"] = "service-recovery-policy", ["service_transitions"] = "service-transitions",
    };

    private sealed record ReadField(string Name, string Kind = "text", int Maximum = 256, string? Flag = null,
        bool Optional = false, string[]? Values = null, int Minimum = 0);
    private sealed record ReadOperation(string Name, string Command, ReadField[] Fields, string[]? Prefix = null, string[]? Suffix = null);

    private static readonly Dictionary<string, ReadOperation[]> ReadCatalog = new(StringComparer.Ordinal)
    {
        ["evidence"] =
        [
            new("investigate", "investigate", [new("path", "file")], Suffix: ["--json"]),
            new("batch", "investigate-batch", [new("paths", "files", 12)]),
            new("workflow_journal", "workflow-journal", [new("path", "file")]),
            new("task_journal", "task-journal", [new("path", "file"), new("completion", "file", Flag: "--completion", Optional: true)]),
            new("capture", "open", [new("path", "file")], Suffix: ["--json"]),
        ],
        ["knowledge"] =
        [
            new("info", "knowledge-info", [new("pack", "file")]),
            new("lookup", "knowledge", [new("pack", "file"), new("context"), new("query", Maximum: 1024)]),
            new("search", "knowledge-search", [new("pack", "file"), new("context"), new("query", Maximum: 1024)]),
            new("log", "knowledge-log", [new("pack", "file"), new("context"), new("path", "file")]),
            new("compare", "knowledge-compare", [new("before", "file"), new("after", "file")]),
            new("active_status", "knowledge-active", [new("root", "activeDirectory"), new("publicKey", "file"), new("packId", "key", 128), new("minimumRevision", "number", int.MaxValue, Minimum: 1)]),
            new("mac", "mac-vendor", [new("pack", "file"), new("identity", Maximum: 64)]),
            new("pci", "pci-vendor", [new("pack", "file"), new("identity", Maximum: 256)]),
            new("usb", "usb-vendor", [new("pack", "file"), new("identity", Maximum: 256)]),
            new("bluetooth", "bluetooth-company", [new("pack", "file"), new("identity", "number", 65535)]),
            new("uefi", "uefi-vendor", [new("pack", "file"), new("registry", Values: ["PNP", "ACPI"]), new("identity", Maximum: 16)]),
            new("driver", "driver-reference", [new("pack", "file"), new("identity", Maximum: 256), new("architecture", Values: ["x86", "x64", "arm64"]), new("platform", Values: ["client", "server"]), new("build", "number", 1000000, Minimum: 1)]),
        ],
        ["review"] =
        [
            new("duplicates", "duplicate-files", [new("root", "directory"), new("minBytes", "number", int.MaxValue, "--min-bytes", true, Minimum: 1), new("maxFiles", "number", 10000, "--max-files", true, Minimum: 1)]),
            new("large_files", "large-files", [new("root", "directory"), new("minBytes", "number", int.MaxValue, "--min-bytes", true, Minimum: 1), new("maxFiles", "number", 10000, "--max-files", true, Minimum: 1), new("limit", "number", 500, "--limit", true, Minimum: 1)]),
            new("applications", "installed-apps", []),
            new("applications_compare", "installed-apps-compare", [new("before", "file"), new("after", "file")]),
            new("browser_extensions", "browser-extensions", []),
            new("device", "device-check", [new("category", Values: ["audio", "network", "storage", "printing", "display", "input", "portable", "bluetooth", "camera", "smartcard", "biometric", "problems"])]),
            new("profiles", "profiles", [new("days", "number", 3650, "--days", true, Minimum: 1), new("dependencies", "boolean", Flag: "--dependencies", Optional: true)]),
            new("task_plan", "task-plan", [new("path", "file")]),
            new("task_definition", "task-definition", [new("folder", "taskFolder", 256), new("name", "taskName", 128)]),
            new("services", "service-check", [new("names", "serviceNames", 16)]),
            new("msi_product", "msi-product", [new("productCode", "guid", 38)]),
            new("time_plan", "time-plan", []),
        ],
        ["catalog"] =
        [
            new("workflows", "workflows", []),
            new("workflow_options", "workflow-options", [new("workflow", "key", 64, Optional: true)]),
            new("recipes", "recipes", [new("workflow", "key", 64, Optional: true)]),
            new("packs", "pack", [], Prefix: ["list"]),
            new("pack_validate", "pack", [new("path", "file")], Prefix: ["validate"]),
            new("validation_plan", "validation-plan", [new("recipe", "key", 128, "--recipe", true)]),
            new("validation_coverage", "validation-coverage", [new("paths", "validationFiles", 32)]),
        ],
    };

    private sealed partial class Session
    {
        private IEnumerable<AppAdapter> ReadOnlyAdapters()
        {
            foreach (var (domain, operations) in ReadCatalog)
            {
                var available = operations.Where(operation => commands.Contains(operation.Command)).ToArray();
                if (available.Length == 0) continue;
                var fields = new JsonObject { ["operation"] = Enum(available.Select(operation => operation.Name).ToArray()) };
                foreach (var group in available.SelectMany(operation => operation.Fields).GroupBy(field => field.Name, StringComparer.Ordinal))
                {
                    var variants = group.Select(ReadFieldSchema).DistinctBy(fieldSchema => fieldSchema.ToJsonString()).ToArray();
                    fields[group.Key] = variants.Length == 1 ? variants[0] : new JsonObject { ["anyOf"] = new JsonArray(variants.Cast<JsonNode?>().ToArray()) };
                }
                var schema = Schema(fields, "operation");
                var alternatives = new JsonArray();
                foreach (var operation in available)
                {
                    var properties = new JsonObject { ["operation"] = Enum(operation.Name) };
                    foreach (var field in operation.Fields) properties[field.Name] = ReadFieldSchema(field);
                    alternatives.Add(Schema(properties, ["operation", .. operation.Fields.Where(field => !field.Optional).Select(field => field.Name)]));
                }
                schema["oneOf"] = alternatives;
                var captured = domain;
                yield return Tool("killerbench_" + domain,
                    "Read-only KillerBench " + domain + ". Only operation-specific fields are accepted. No repair, export or activation. Historical records do not establish current health. Evidence may contain private paths, identities or raw contents; share only as intended. Long reads return a jobId for killerbench_job.",
                    schema, (input, token) => ReadOnly(captured, input, token));
            }
        }

        private async Task<AppCallResult> ReadOnly(string domain, JsonElement input, CancellationToken token)
        {
            if (input.ValueKind != JsonValueKind.Object) return Error("Expected an object.");
            var name = KillerBenchRepairs.Optional(input, "operation");
            var operation = ReadCatalog[domain].SingleOrDefault(item => item.Name == name);
            if (operation is null) return Error("Unknown read-only operation.");
            if (!commands.Contains(operation.Command)) return Error("The installed KillerBench CLI does not advertise this operation. Update KillerBench.");
            var allowed = operation.Fields.Select(field => field.Name).Append("operation").ToArray();
            if (!KillerBenchRepairs.Allowed(input, allowed) || input.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != input.EnumerateObject().Count())
                return Error("Unknown, duplicate or unused option for this operation.");
            var args = new List<string> { operation.Command };
            if (operation.Prefix is not null) args.AddRange(operation.Prefix);
            foreach (var field in operation.Fields)
            {
                if (!input.TryGetProperty(field.Name, out var value))
                {
                    if (field.Optional) continue;
                    return Error("Missing " + field.Name + ".");
                }
                var (values, error) = ValidateReadField(field, value);
                if (error is not null) return Error(field.Name + ": " + error);
                if (field.Kind == "boolean")
                {
                    if (value.ValueKind == JsonValueKind.True) args.Add(field.Flag!);
                    continue;
                }
                if (values!.Length == 0) continue;
                if (field.Flag is not null) args.Add(field.Flag);
                args.AddRange(values);
            }
            if (operation.Suffix is not null) args.AddRange(operation.Suffix);
            return await ReadJob(operation.Command, args, token).ConfigureAwait(false);
        }

        private Task<AppCallResult> ReadJob(string command, IReadOnlyList<string> args, CancellationToken token)
        {
            if (!commands.Contains(command)) return Task.FromResult(Error("The installed KillerBench CLI does not advertise this operation. Update KillerBench."));
            return StartJob("read", command, async _ =>
            {
                var run = await Direct(args, 180, token).ConfigureAwait(false);
                var result = Parse(run);
                if (command == "investigate-batch" && !run.Truncated)
                {
                    try
                    {
                        var records = new JsonArray();
                        foreach (var line in run.Output.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))) records.Add(JsonNode.Parse(line));
                        result["records"] = records;
                    }
                    catch (JsonException) { result["incomplete"] = true; }
                }
                result["scope"] = "Read-only evidence. Historical records and reference matches are not current-health or repair authorization. Private paths, identities or raw evidence may be present.";
                return result;
            }, token);
        }
    }

    private static JsonObject ReadFieldSchema(ReadField field)
    {
        if (field.Kind == "boolean") return Boolean(false);
        if (field.Kind == "number") return new JsonObject { ["type"] = "integer", ["minimum"] = field.Minimum, ["maximum"] = field.Maximum };
        if (field.Kind is "files" or "validationFiles" or "serviceNames") return new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = field.Maximum, ["items"] = Text(field.Kind == "serviceNames" ? 128 : 2048) };
        return field.Values is not null ? Enum(field.Values) : Text(field.Kind is "file" or "directory" or "activeDirectory" ? 2048 : field.Maximum);
    }

    private static (string[]? Values, string? Error) ValidateReadField(ReadField field, JsonElement value)
    {
        if (field.Kind == "boolean") return value.ValueKind is JsonValueKind.True ? ([""], null) : value.ValueKind is JsonValueKind.False ? ([], null) : (null, "must be boolean");
        if (field.Kind == "number")
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= field.Minimum && number <= field.Maximum
                ? ([number.ToString(System.Globalization.CultureInfo.InvariantCulture)], null) : (null, "integer outside allowed bounds");
        if (field.Kind is "files" or "validationFiles" or "serviceNames")
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 1 || value.GetArrayLength() > field.Maximum) return (null, "list outside allowed bounds");
            var list = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                var (part, error) = ValidateReadField(new(field.Name, field.Kind == "serviceNames" ? "service" : "file", 128), item);
                if (error is not null) return (null, error);
                if (field.Kind == "validationFiles" && ValidateNestedRecord(part![0]) is { } nestedError) return (null, nestedError);
                list.AddRange(part!);
            }
            return (list.ToArray(), null);
        }
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text ||
            text.Length > (field.Kind is "file" or "directory" or "activeDirectory" ? 2048 : field.Maximum) || string.IsNullOrWhiteSpace(text) ||
            text.StartsWith('-') || text.Any(char.IsControl) || text.IndexOfAny(['"', ';', '|', '<', '>']) >= 0) return (null, "invalid text");
        if (field.Values is not null && !field.Values.Contains(text, StringComparer.Ordinal)) return (null, "unknown value");
        if (field.Kind is "file" or "directory" or "activeDirectory")
        {
            if (!Path.IsPathFullyQualified(text)) return (null, "use a fully qualified path");
            var refusal = AgentPaths.AccessRefusal(text);
            if (refusal is not null) return (null, refusal);
            try
            {
                text = Path.GetFullPath(text);
                if (field.Kind == "file" ? !File.Exists(text) : !Directory.Exists(text)) return (null, "path does not exist");
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return (null, "path unavailable"); }
            if (field.Kind == "activeDirectory" && ValidateActiveDirectory(text) is { } nestedError) return (null, nestedError);
        }
        if (field.Kind == "guid" && !Guid.TryParseExact(text.Trim('{', '}'), "D", out _)) return (null, "expected a product GUID");
        if (field.Kind == "key" && text.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '-' or '_'))) return (null, "invalid catalog key");
        if (field.Kind is "service" or "taskName" && (text.Contains('/') || text.Contains('\\') || text is "." or "..")) return (null, "expected a name, not a path");
        if (field.Kind == "taskFolder" && (!text.StartsWith('\\') || text.Contains('/') || text.Contains(':') || text.Contains("..", StringComparison.Ordinal))) return (null, "expected a local Task Scheduler folder");
        return ([text], null);
    }

    private static bool LocalFilename(string name) => name.Length is > 0 and <= 255 && name is not ("." or "..") &&
        name.IndexOfAny(['/', '\\', ':']) < 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string? NestedRefusal(string path)
    {
        var refusal = AgentPaths.AccessRefusal(path);
        if (refusal is not null) return refusal;
        // Refuse redirected ancestors even if their current target is allowed. The CLI follows references later.
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return "Nested evidence cannot use redirected files or directories.";
        return null;
    }

    private static string? ValidateActiveDirectory(string root)
    {
        try
        {
            string pointer = Path.Combine(root, "active.json");
            if (NestedRefusal(pointer) is { } refusal) return refusal;
            if (!File.Exists(pointer)) return null; // The CLI reports unavailable status.
            using var document = ReadBoundedJson(pointer, 16384, 4);
            if (!document.RootElement.TryGetProperty("FileName", out var name) || name.ValueKind != JsonValueKind.String ||
                name.GetString() is not { } text || !LocalFilename(text) || !text.StartsWith("pack-", StringComparison.Ordinal))
                return "Active pointer must name one local pack file.";
            return NestedRefusal(Path.Combine(root, text));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return "Active pointer could not be safely checked."; }
    }

    private static string? ValidateNestedRecord(string path)
    {
        try
        {
            if (NestedRefusal(path) is { } refusal) return refusal;
            using var document = ReadBoundedJson(path, 1048576, 16);
            var record = document.RootElement;
            string directory = Path.GetDirectoryName(path)!;
            // Native validation records use the owning serializer's camelCase names.
            if (record.TryGetProperty("artifactDirectory", out var subdirectory) && subdirectory.ValueKind != JsonValueKind.Null)
            {
                if (subdirectory.ValueKind != JsonValueKind.String || subdirectory.GetString() is not { } text || !LocalFilename(text)) return "Invalid artifact directory.";
                directory = Path.Combine(directory, text);
            }
            if (!record.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array || artifacts.GetArrayLength() is < 1 or > 32)
                return "Validation record must contain 1 to 32 artifacts.";
            foreach (var artifact in artifacts.EnumerateArray())
            {
                if (artifact.ValueKind != JsonValueKind.Object || !artifact.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String || name.GetString() is not { } text || !LocalFilename(text)) return "Invalid artifact name.";
                if (NestedRefusal(Path.Combine(directory, text)) is { } artifactRefusal) return artifactRefusal;
            }
            return null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return "Validation references could not be safely checked."; }
    }

    private static JsonDocument ReadBoundedJson(string path, int maximumBytes, int depth)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximumBytes) throw new InvalidDataException("Evidence exceeds its byte bound.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Evidence changed beyond its byte bound.");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = depth });
    }
}
