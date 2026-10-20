using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KillerMCP.Runtime;

/// <summary>The captured result of one KillerBench command.</summary>
public sealed record KillerBenchRun(int ExitCode, string Output, string Error, bool Truncated);

/// <summary>What the desktop confirmation shows for a disruptive repair.</summary>
public sealed record KillerBenchConfirmation(string Action, string Title, string Tier, string Effects, string Target, string Command);

/// <summary>Replaceable pieces of the KillerBench integration. Tests supply fakes; the server uses the defaults.</summary>
public sealed class KillerBenchOptions
{
    /// <summary>Asks a person at the desktop to approve one disruptive repair. Returns true only on approval.</summary>
    public Func<KillerBenchConfirmation, CancellationToken, Task<bool>>? Confirm { get; init; }

    /// <summary>Runs one KillerBench command with administrator rights.</summary>
    public Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<KillerBenchRun>>? RunElevated { get; init; }

    public Func<DateTimeOffset>? Now { get; init; }

    /// <summary>How long a call waits before it hands back a job ID instead of the finished result.</summary>
    public TimeSpan Wait { get; init; } = TimeSpan.FromSeconds(40);

    public string? Notifier { get; init; }
}

public static partial class KillerBenchAdapter
{
    private const int MaximumOutput = 4 * 1024 * 1024;

    private static readonly string[] Checks =
    [
        "vitals", "restart", "dhcp", "printers", "print_jobs", "printer_queue", "dns", "tcp", "volume_capacity", "storage",
        "crash_review", "update_history", "update_search", "servicing_logs", "defender", "firewall", "security", "services",
        "time", "bits_jobs", "recipes",
    ];

    public static async Task<IReadOnlyList<AppAdapter>> CreateAsync(string? path, string? notes, KillerBenchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (!IsFile(path)) return [];
        try
        {
            var help = await ProcessRunner.RunAsync(path!, ["help"], TimeSpan.FromSeconds(15), 262144, cancellationToken).ConfigureAwait(false);
            if (help.ExitCode != 0 || !help.StandardOutput.Contains("full-scan", StringComparison.Ordinal)) return [];
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or Win32Exception)
        {
            return [];
        }
        return new Session(path!, IsFile(notes) ? notes : null, options ?? new KillerBenchOptions()).Adapters();
    }

    private sealed class Job
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        public required string Name { get; init; }
        public required DateTimeOffset Started { get; init; }
        public Task<JsonObject> Work { get; set; } = null!;
        public volatile string Status = "running";
    }

    private sealed record Ticket(KillerBenchRepairPlan Plan, DateTimeOffset Issued);

    private sealed class Session(string path, string? notes, KillerBenchOptions options)
    {
        private readonly ConcurrentDictionary<string, Job> jobs = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Ticket> tickets = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim mutation = new(1, 1);
        private DateTimeOffset Now => options.Now?.Invoke() ?? DateTimeOffset.UtcNow;

        public IReadOnlyList<AppAdapter> Adapters()
        {
            var list = new List<AppAdapter>
            {
                Tool("killerbench_code_lookup", "Look up a Windows result code offline with KillerBench: a Device Manager problem code, a Win32 error, a Windows Update result or a Windows Setup result. Accepts decimal or 0x hexadecimal.", Schema(new JsonObject { ["kind"] = Enum("device", "win32", "update", "setup"), ["code"] = Text(24) }, "kind", "code"), CodeLookup),
                Tool("killerbench_diagnostic_scopes", "List KillerBench diagnostic scopes for killerbench_scan. Set all to list every read-only diagnostic area instead of the recommended outcome groups.", Schema(new JsonObject { ["all"] = Boolean(false) }), Scopes),
                Tool("killerbench_check", "Run one read-only KillerBench check on this PC and return its JSON evidence. Nothing is changed. dns needs host, tcp needs host and port, volume_capacity and storage need drive, printer_queue needs queues. Long checks return a jobId to poll with killerbench_job.", CheckSchema(), Check),
                Tool("killerbench_scan", "Run a read-only KillerBench diagnosis of this PC, either every area or the selected scopes from killerbench_diagnostic_scopes. Nothing is repaired. A full scan takes minutes, so this usually returns a jobId to poll with killerbench_job.", Schema(new JsonObject { ["scopes"] = new JsonObject { ["type"] = "array", ["items"] = Text(64), ["maxItems"] = 16 } }), Scan),
                Tool("killerbench_job", "Get the status or finished result of a KillerBench job started by another killerbench tool.", Schema(new JsonObject { ["jobId"] = Text(32) }, "jobId"), JobStatus),
                Tool("killerbench_repair_actions", "List the KillerBench repairs KillerMCP can request, their safety tier and options, the repair level the user chose in the KillerMCP section of the KillerBench About window, and any repair lock. Repairs are off until the user turns them on there.", Schema(new JsonObject()), Actions),
                Tool("killerbench_repair_preview", "Preview one KillerBench repair without changing anything. When repairs are allowed and KillerBench finds something to fix, the result includes a single-use token that expires in 10 minutes. Show the preview to the user before executing.", Schema(new JsonObject { ["action"] = Text(40), ["options"] = new JsonObject { ["type"] = "object" } }, "action"), Preview),
                Tool("killerbench_repair_execute", "Execute one previewed KillerBench repair by its token. Changes this PC. Only one repair runs at a time, Windows asks for administrator approval, and disruptive repairs also need a person to approve them in a KillerMCP window on the desktop. An uncertain outcome locks further repairs until the user clears the lock.", Schema(new JsonObject { ["token"] = Text(64) }, "token"), Execute),
                Tool("killerbench_repair_recover", "Reconcile an interrupted or uncertain KillerBench repair without repeating it. Read-only. Use the same action and options as the original preview.", Schema(new JsonObject { ["action"] = Text(40), ["options"] = new JsonObject { ["type"] = "object" } }, "action"), Recover),
            };
            if (notes is not null)
            {
                list.Add(Tool("killerbench_save_scan_note", "Save the finished result of a KillerBench scan or check job as a new KillerNotes note.", Schema(new JsonObject { ["jobId"] = Text(32), ["title"] = Text(120), ["group"] = Text(240) }, "jobId"), SaveNote));
            }
            return list;
        }

        private async Task<AppCallResult> CodeLookup(JsonElement input, CancellationToken token)
        {
            if (!KillerBenchRepairs.Allowed(input, "kind", "code")) return Error("Expected kind and code");
            var kind = KillerBenchRepairs.Optional(input, "kind");
            var code = KillerBenchRepairs.Optional(input, "code")?.Trim();
            if (kind is not ("device" or "win32" or "update" or "setup")) return Error("kind must be device, win32, update or setup");
            if (code is null || !Code().IsMatch(code)) return Error("code must be decimal or 0x hexadecimal");
            var result = await Run([kind + "-code", code], 30, token).ConfigureAwait(false);
            return Ok(result);
        }

        private async Task<AppCallResult> Scopes(JsonElement input, CancellationToken token)
        {
            if (!KillerBenchRepairs.Allowed(input, "all")) return Error("Only all is accepted");
            var all = input.TryGetProperty("all", out var value) && value.ValueKind == JsonValueKind.True;
            return Ok(await Run(all ? ["diagnostic-scopes", "--all"] : ["diagnostic-scopes"], 30, token).ConfigureAwait(false));
        }

        private async Task<AppCallResult> Check(JsonElement input, CancellationToken token)
        {
            if (!KillerBenchRepairs.Allowed(input, "check", "host", "port", "drive", "queues")) return Error("Unknown option. Allowed: check, host, port, drive, queues");
            var check = KillerBenchRepairs.Optional(input, "check");
            if (check is null || !Checks.Contains(check)) return Error("check must be one of: " + string.Join(", ", Checks));
            string[]? args = check switch
            {
                "vitals" => ["vitals-check"],
                "restart" => ["restart-check"],
                "dhcp" => ["dhcp-check"],
                "printers" => ["printer-list"],
                "print_jobs" => ["print-job-list"],
                "update_history" => ["update-history"],
                "update_search" => ["update-search"],
                "crash_review" => ["crash-review"],
                "servicing_logs" => ["servicing-logs"],
                "defender" => ["defender-overview"],
                "firewall" => ["network-firewall"],
                "security" => ["security-check"],
                "services" => ["service-check", "--all"],
                "time" => ["time-config"],
                "bits_jobs" => ["bits-jobs"],
                "recipes" => ["recipes"],
                _ => null,
            };
            if (args is null)
            {
                switch (check)
                {
                    case "dns":
                        if (!KillerBenchRepairs.Host(input, "host", out var host)) return Error("dns needs host");
                        args = ["dns-check", host!];
                        break;
                    case "tcp":
                        if (!KillerBenchRepairs.Host(input, "host", out var target) || !KillerBenchRepairs.Integer(input, "port", 1, 65535, out var port)) return Error("tcp needs host and port");
                        args = ["tcp-check", target!, port.ToString()];
                        break;
                    case "volume_capacity":
                        if (!KillerBenchRepairs.Drive(input, "drive", out var volume)) return Error("volume_capacity needs drive, such as C:");
                        args = ["volume-capacity", volume!];
                        break;
                    case "storage":
                        if (!KillerBenchRepairs.Drive(input, "drive", out var drive)) return Error("storage needs drive, such as C:");
                        args = ["storage-check", drive![..1]];
                        break;
                    case "printer_queue":
                        if (!input.TryGetProperty("queues", out var queues) || queues.ValueKind != JsonValueKind.Array || queues.GetArrayLength() is < 1 or > 8) return Error("printer_queue needs 1 to 8 queue names");
                        var list = new List<string> { "printer-check" };
                        foreach (var queue in queues.EnumerateArray())
                        {
                            if (queue.ValueKind != JsonValueKind.String || !QueueName().IsMatch(queue.GetString()!)) return Error("Queue names must be plain printer names");
                            list.AddRange(["--queue", queue.GetString()!]);
                        }
                        args = [.. list];
                        break;
                }
            }
            return await StartJob("check", check, job => RunJob(args!, 1800, token), token).ConfigureAwait(false);
        }

        private Task<AppCallResult> Scan(JsonElement input, CancellationToken token)
        {
            if (!KillerBenchRepairs.Allowed(input, "scopes")) return Task.FromResult(Error("Only scopes is accepted"));
            var args = new List<string> { "full-scan" };
            if (input.TryGetProperty("scopes", out var scopes))
            {
                if (scopes.ValueKind != JsonValueKind.Array || scopes.GetArrayLength() > 16) return Task.FromResult(Error("scopes must list up to 16 scope names"));
                foreach (var scope in scopes.EnumerateArray())
                {
                    if (scope.ValueKind != JsonValueKind.String || !ScopeName().IsMatch(scope.GetString()!)) return Task.FromResult(Error("Scope names look like StorageReadiness"));
                    args.AddRange(["--scope", scope.GetString()!]);
                }
            }
            args.Add("--json");
            return StartJob("scan", "full-scan", job => RunJob(args, 3600, token), token);
        }

        private Task<AppCallResult> JobStatus(JsonElement input, CancellationToken token)
        {
            var id = KillerBenchRepairs.Optional(input, "jobId");
            if (!KillerBenchRepairs.Allowed(input, "jobId") || id is null || !jobs.TryGetValue(id, out var job)) return Task.FromResult(Error("Unknown or expired jobId"));
            return Task.FromResult(Ok(Describe(job)));
        }

        private async Task<AppCallResult> Actions(JsonElement input, CancellationToken token)
        {
            if (!KillerBenchRepairs.Allowed(input)) return Error("This tool takes no arguments");
            var level = KillerBenchSettings.ReadLevel();
            var catalog = new JsonArray();
            foreach (var definition in KillerBenchRepairs.Catalog)
            {
                catalog.Add(new JsonObject
                {
                    ["action"] = definition.Name,
                    ["title"] = definition.Title,
                    ["tier"] = definition.Tier is null ? "depends on the selected recipes" : TierName(definition.Tier.Value),
                    ["effects"] = definition.Effects,
                    ["options"] = new JsonArray(definition.Options.Select(option => (JsonNode)JsonValue.Create(option)!).ToArray()),
                    ["recoverable"] = KillerBenchRepairs.CanRecover(definition.Name),
                });
            }
            var result = new JsonObject
            {
                ["repairLevel"] = KillerBenchSettings.Name(level),
                ["allowedTiers"] = level switch { KillerBenchRepairLevel.All => "safe and disruptive", KillerBenchRepairLevel.Safe => "safe only", _ => "none" },
                ["changeLevel"] = "The user turns agent repairs on or off with the checkboxes in the KillerMCP section of the KillerBench About window. An MCP client cannot change it.",
                ["lock"] = KillerBenchSettings.ReadLock() is { } repairLock ? JsonSerializer.SerializeToNode(repairLock) : null,
                ["actions"] = catalog,
                ["neverThroughMcp"] = new JsonArray(KillerBenchRepairs.NeverThroughMcp.Select(item => (JsonNode)JsonValue.Create(item)!).ToArray()),
            };
            var recipes = await Recipes(token).ConfigureAwait(false);
            if (recipes is { ValueKind: JsonValueKind.Array } list)
            {
                result["recipes"] = new JsonArray(list.EnumerateArray()
                    .Where(recipe => recipe.TryGetProperty("canAutoExecute", out var auto) && auto.ValueKind == JsonValueKind.True)
                    .Select(recipe => (JsonNode)new JsonObject { ["id"] = recipe.GetProperty("id").GetString(), ["title"] = recipe.TryGetProperty("title", out var title) ? title.GetString() : null })
                    .ToArray());
            }
            return Ok(result.ToJsonString());
        }

        private async Task<AppCallResult> Preview(JsonElement input, CancellationToken token)
        {
            var (plan, problem) = await Plan(input, token).ConfigureAwait(false);
            if (plan is null) return Error(problem!);
            var level = KillerBenchSettings.ReadLevel();
            var blockedBy = Permission(plan, level);
            return await StartJob("preview", plan.Action, async job =>
            {
                var preview = await RunJob(plan.PreviewArguments, plan.PreviewSeconds, token).ConfigureAwait(false);
                var result = new JsonObject
                {
                    ["action"] = plan.Action,
                    ["title"] = plan.Title,
                    ["tier"] = TierName(plan.Tier),
                    ["target"] = plan.Target,
                    ["effects"] = plan.Effects,
                    ["needsAdministrator"] = plan.Elevate,
                    ["needsDesktopApproval"] = plan.Tier == KillerBenchTier.B,
                    ["preview"] = preview,
                };
                var exit = preview["exitCode"]?.GetValue<int>() ?? -1;
                if (blockedBy is not null) result["canExecute"] = false;
                else if (exit != 0) { result["canExecute"] = false; blockedBy = "KillerBench did not approve this repair in its preview."; }
                else if (NothingToDo(preview["data"])) { result["canExecute"] = false; blockedBy = "KillerBench found nothing to repair."; }
                else
                {
                    Prune();
                    var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                    tickets[id] = new Ticket(plan, Now);
                    result["canExecute"] = true;
                    result["token"] = id;
                    result["expiresUtc"] = Now.AddMinutes(10).ToString("O");
                }
                if (blockedBy is not null) result["reason"] = blockedBy;
                return result;
            }, token).ConfigureAwait(false);
        }

        private async Task<AppCallResult> Execute(JsonElement input, CancellationToken token)
        {
            var id = KillerBenchRepairs.Optional(input, "token");
            if (!KillerBenchRepairs.Allowed(input, "token") || id is null || !tickets.TryRemove(id, out var ticket)) return Error("Unknown, used or expired token. Run killerbench_repair_preview again.");
            if (Now - ticket.Issued > TimeSpan.FromMinutes(10)) return Error("This token expired. Run killerbench_repair_preview again.");
            var plan = ticket.Plan;
            var problem = Permission(plan, KillerBenchSettings.ReadLevel());
            if (problem is not null) return Error(problem);
            if (!await mutation.WaitAsync(0, token).ConfigureAwait(false)) return Error("Another KillerBench repair is running. Wait for it to finish.");
            try
            {
                return await StartJob("repair", plan.Action, async job =>
                {
                    try
                    {
                        return await RunRepair(job, plan, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        mutation.Release();
                    }
                }, token).ConfigureAwait(false);
            }
            catch
            {
                mutation.Release();
                throw;
            }
        }

        private async Task<JsonObject> RunRepair(Job job, KillerBenchRepairPlan plan, CancellationToken token)
        {
            var command = "killerbench-cli " + string.Join(" ", plan.ExecuteArguments.Select(argument => argument.Contains(' ') ? "\"" + argument + "\"" : argument));
            var result = new JsonObject { ["action"] = plan.Action, ["tier"] = TierName(plan.Tier), ["target"] = plan.Target };
            Audit(plan, "requested", null);
            // Any program that can write a file can write the repair setting, so the first repair after
            // it appears or changes needs a person at the desktop, even a safe one. The approval is
            // remembered by the setting's hash in the KillerMCP state folder, where agents cannot write.
            var (level, settingsHash) = KillerBenchSettings.ReadSettings();
            var problem = Permission(plan, level);
            if (problem is not null)
            {
                Audit(plan, "denied", null);
                result["outcome"] = "NotApproved";
                result["detail"] = problem;
                return result;
            }
            var settingChanged = settingsHash is null || !KillerBenchSettings.IsApproved(settingsHash);
            if (plan.Tier == KillerBenchTier.B || settingChanged)
            {
                job.Status = "waiting for approval on the desktop";
                var effects = settingChanged
                    ? plan.Effects + " The KillerBench agent repair setting is new or has changed since a repair was last approved here. It now allows "
                        + (level == KillerBenchRepairLevel.All ? "safe and disruptive" : "safe") + " repairs. Approving also accepts that setting."
                    : plan.Effects;
                bool approved;
                try
                {
                    approved = await Confirm(new KillerBenchConfirmation(plan.Action, plan.Title, plan.Tier == KillerBenchTier.B ? "Disruptive" : "Safe", effects, plan.Target, command), token).ConfigureAwait(false);
                    if (approved && settingChanged) KillerBenchSettings.Approve(settingsHash!);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception or TimeoutException)
                {
                    approved = false;
                }
                if (!approved)
                {
                    Audit(plan, "denied", null);
                    result["outcome"] = "NotApproved";
                    result["detail"] = "Nobody approved this repair on the desktop, so nothing changed.";
                    return result;
                }
            }

            job.Status = "running";
            KillerBenchRun run;
            try
            {
                run = plan.Elevate
                    ? await Elevated(path, plan.ExecuteArguments, TimeSpan.FromSeconds(plan.ExecuteSeconds), token).ConfigureAwait(false)
                    : await Direct(plan.ExecuteArguments, plan.ExecuteSeconds, token).ConfigureAwait(false);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                Audit(plan, "declined", null);
                result["outcome"] = "NotApproved";
                result["detail"] = "Windows administrator approval was declined, so nothing changed.";
                return result;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or InvalidOperationException or Win32Exception or ArgumentException)
            {
                Lock(plan, "The repair did not report a result: " + exception.Message, null);
                result["outcome"] = "Uncertain";
                result["detail"] = "KillerBench did not report a result. Repairs are locked until the user checks this PC with killerbench_repair_recover and clears the lock in the KillerMCP section of the KillerBench About window.";
                return result;
            }

            var parsed = Parse(run);
            result["result"] = parsed;
            var outcome = OutcomeOf(parsed["data"]);
            if (run.ExitCode == 0)
            {
                Audit(plan, "completed", 0);
                result["outcome"] = "Completed";
            }
            else if (run.ExitCode == 2 || (run.ExitCode == 3 && outcome is "Blocked" or "NotEligible" or "Preview"))
            {
                Audit(plan, "blocked", run.ExitCode);
                result["outcome"] = "Blocked";
                result["detail"] = "KillerBench refused the repair before changing anything.";
            }
            else
            {
                Lock(plan, "KillerBench exited with code " + run.ExitCode + (outcome is null ? string.Empty : " and outcome " + outcome), run.ExitCode);
                result["outcome"] = "Uncertain";
                result["detail"] = "KillerBench could not verify the result. Repairs are locked until the user checks this PC with killerbench_repair_recover and clears the lock in the KillerMCP section of the KillerBench About window.";
            }
            return result;
        }

        private async Task<AppCallResult> Recover(JsonElement input, CancellationToken token)
        {
            var (plan, problem) = await Plan(input, token).ConfigureAwait(false);
            if (plan is null) return Error(problem!);
            if (plan.RecoverArguments is null) return Error(plan.Action + " has no separate recovery command. Run killerbench_scan to check the current state.");
            return await StartJob("recover", plan.Action, job => RunJob(plan.RecoverArguments, 900, token), token).ConfigureAwait(false);
        }

        private async Task<AppCallResult> SaveNote(JsonElement input, CancellationToken token)
        {
            if (!KillerBenchRepairs.Allowed(input, "jobId", "title", "group")) return Error("Expected jobId, title and group");
            var id = KillerBenchRepairs.Optional(input, "jobId");
            if (id is null || !jobs.TryGetValue(id, out var job) || job.Kind is not ("scan" or "check")) return Error("jobId must name a scan or check job");
            if (!job.Work.IsCompletedSuccessfully) return Error("That job has not finished");
            var title = KillerBenchRepairs.Optional(input, "title") ?? "KillerBench report " + job.Started.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var group = KillerBenchRepairs.Optional(input, "group");
            if (title.Length > 120 || group?.Length > 240) return Error("title or group is too long");
            var content = Markdown(job.Work.Result["data"] ?? job.Work.Result["text"]);
            // A full scan report can run past what a command line holds; NotesContent sends it in a file then.
            var args = new List<string> { "create", "--title", title };
            if (!string.IsNullOrWhiteSpace(group)) args.AddRange(["--group", group]);
            var (contentArguments, folder, problem) = await NotesContent.ArgumentsAsync(notes!, content, token, args).ConfigureAwait(false);
            if (problem is not null) return Error(problem);
            try
            {
                args.AddRange(contentArguments);
                var response = await ProcessRunner.RunAsync(notes!, NotesContent.Prefix(notes!).Concat(args), TimeSpan.FromSeconds(30), 1_048_576, token).ConfigureAwait(false);
                return response.ExitCode == 0 ? Ok(response.StandardOutput.Trim()) : Error(Limit(string.IsNullOrWhiteSpace(response.StandardError) ? "KillerNotes command failed" : response.StandardError.Trim()));
            }
            finally { NotesContent.Delete(folder); }
        }

        private async Task<(KillerBenchRepairPlan? Plan, string? Error)> Plan(JsonElement input, CancellationToken token)
        {
            if (!KillerBenchRepairs.Allowed(input, "action", "options")) return (null, "Expected action and options");
            var definition = KillerBenchRepairs.Find(KillerBenchRepairs.Optional(input, "action"));
            if (definition is null) return (null, "Unknown action. killerbench_repair_actions lists every repair KillerMCP can request; anything else is not available through MCP.");
            using var empty = JsonDocument.Parse("{}");
            var options = input.TryGetProperty("options", out var value) ? value : empty.RootElement;
            if (options.ValueKind != JsonValueKind.Object) return (null, "options must be an object");
            return await definition.Build(options, Recipes, token).ConfigureAwait(false);
        }

        private static string? Permission(KillerBenchRepairPlan plan, KillerBenchRepairLevel level)
        {
            if (level == KillerBenchRepairLevel.Off) return "KillerBench repairs through KillerMCP are off. The user can turn them on in the KillerMCP section of the KillerBench About window.";
            if (plan.Tier == KillerBenchTier.B && level != KillerBenchRepairLevel.All) return "This is a disruptive repair. The user allowed safe repairs only. Disruptive repairs also need the second checkbox in the KillerMCP section of the KillerBench About window, and each one is approved on the desktop.";
            if (KillerBenchSettings.ReadLock() is { } repairLock) return $"Repairs are locked because {repairLock.Title ?? repairLock.Action} ended with an uncertain result ({repairLock.Reason}). Check it with killerbench_repair_recover, then the user clears the lock in the KillerMCP section of the KillerBench About window.";
            return null;
        }

        private async Task<AppCallResult> StartJob(string kind, string name, Func<Job, Task<JsonObject>> work, CancellationToken token)
        {
            Prune();
            if (kind != "repair" && jobs.Values.Count(item => !item.Work.IsCompleted) >= 4) return Error("Four KillerBench jobs are already running. Poll them with killerbench_job first.");
            var job = new Job { Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), Kind = kind, Name = name, Started = Now };
            job.Work = Task.Run(async () =>
            {
                try
                {
                    var result = await work(job).ConfigureAwait(false);
                    job.Status = "completed";
                    return result;
                }
                catch (Exception exception) when (exception is TimeoutException or IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
                {
                    job.Status = "failed";
                    return new JsonObject { ["error"] = exception.Message };
                }
            }, CancellationToken.None);
            jobs[job.Id] = job;
            await Task.WhenAny(job.Work, Task.Delay(options.Wait, token)).ConfigureAwait(false);
            return Ok(Describe(job));
        }

        private string Describe(Job job)
        {
            var result = new JsonObject { ["jobId"] = job.Id, ["kind"] = job.Kind, ["name"] = job.Name, ["status"] = job.Status, ["startedUtc"] = job.Started.ToString("O") };
            if (job.Work.IsCompletedSuccessfully) result["result"] = job.Work.Result.DeepClone();
            else result["next"] = "Call killerbench_job with this jobId to get the result.";
            return result.ToJsonString();
        }

        private void Prune()
        {
            foreach (var item in jobs.Where(item => item.Value.Work.IsCompleted && Now - item.Value.Started > TimeSpan.FromHours(4)).ToArray()) jobs.TryRemove(item.Key, out _);
            foreach (var item in tickets.Where(item => Now - item.Value.Issued > TimeSpan.FromMinutes(10)).ToArray()) tickets.TryRemove(item.Key, out _);
        }

        private async Task<JsonElement?> Recipes(CancellationToken token)
        {
            var result = await Run(["recipes"], 60, token).ConfigureAwait(false);
            return result["data"] is JsonArray array ? JsonDocument.Parse(array.ToJsonString()).RootElement.Clone() : null;
        }

        private async Task<JsonObject> Run(IReadOnlyList<string> args, int seconds, CancellationToken token) => Parse(await Direct(args, seconds, token).ConfigureAwait(false));
        private Task<JsonObject> RunJob(IReadOnlyList<string> args, int seconds, CancellationToken token) => Run(args, seconds, token);

        private async Task<KillerBenchRun> Direct(IReadOnlyList<string> args, int seconds, CancellationToken token)
        {
            var result = await ProcessRunner.RunAsync(path, args, TimeSpan.FromSeconds(seconds), MaximumOutput, token).ConfigureAwait(false);
            return new KillerBenchRun(result.ExitCode, result.StandardOutput, result.StandardError, result.OutputExceeded);
        }

        private Task<KillerBenchRun> Elevated(string executable, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token) =>
            options.RunElevated is { } custom ? custom(executable, args, timeout, token) : DefaultElevated(executable, args, timeout, token);

        private Task<bool> Confirm(KillerBenchConfirmation confirmation, CancellationToken token) =>
            options.Confirm is { } custom ? custom(confirmation, token) : DefaultConfirm(options.Notifier, confirmation, token);

        private static void Audit(KillerBenchRepairPlan plan, string stage, int? exitCode) =>
            KillerBenchSettings.Audit(new { atUtc = DateTimeOffset.UtcNow, action = plan.Action, tier = TierName(plan.Tier), target = plan.Target, stage, exitCode });

        private static void Lock(KillerBenchRepairPlan plan, string reason, int? exitCode)
        {
            KillerBenchSettings.WriteLock(new RepairLock(plan.Action, reason, DateTimeOffset.UtcNow, exitCode, plan.Title));
            Audit(plan, "uncertain", exitCode);
        }
    }

    private static JsonObject Parse(KillerBenchRun run)
    {
        var result = new JsonObject
        {
            ["exitCode"] = run.ExitCode,
            ["meaning"] = run.ExitCode switch { 0 => "ok", 2 => "invalid arguments", 3 => "blocked, failed or unverified", 130 => "stopped", _ => "unexpected exit code" },
        };
        var text = run.Output.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = -1;
        var lines = text.Split('\n');
        var offset = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith('{') || line.StartsWith('[')) { start = offset; break; }
            offset += line.Length + 1;
        }
        if (start >= 0 && !run.Truncated)
        {
            try
            {
                result["data"] = JsonNode.Parse(text[start..]);
                var preamble = text[..start].Trim();
                if (preamble.Length > 0) result["notes"] = Limit(preamble);
            }
            catch (JsonException)
            {
                result["text"] = Limit(text, 65536);
            }
        }
        else if (text.Trim().Length > 0)
        {
            result["text"] = Limit(text.Trim(), 65536);
        }
        if (run.Truncated) result["truncated"] = true;
        if (run.Error.Trim().Length > 0) result["error"] = Limit(run.Error.Trim());
        return result;
    }

    private static string? OutcomeOf(JsonNode? data) => data is JsonObject value
        ? (value["outcome"] ?? value["effectiveState"] ?? value["state"]) is JsonValue state && state.TryGetValue<string>(out var text) ? text : null
        : null;

    private static bool NothingToDo(JsonNode? data) => OutcomeOf(data) is "Clear" or "Healthy" or "NotNeeded";

    private static async Task<KillerBenchRun> DefaultElevated(string executable, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token)
    {
        if (Environment.IsPrivilegedProcess)
        {
            var direct = await ProcessRunner.RunAsync(executable, args, timeout, MaximumOutput, token).ConfigureAwait(false);
            return new KillerBenchRun(direct.ExitCode, direct.StandardOutput, direct.StandardError, direct.OutputExceeded);
        }
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Administrator approval is only available on Windows.");
        var folder = Path.Combine(Path.GetTempPath(), "killermcp-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, "out.txt");
        var error = Path.Combine(folder, "err.txt");
        foreach (var value in args.Append(executable).Append(output))
        {
            if (value.IndexOfAny(['"', '%', '&', '|', '<', '>', '^', '!', '\r', '\n', '\0']) >= 0) throw new ArgumentException("A repair argument contains a character KillerMCP will not pass to an elevated command.");
        }
        var line = "\"\"" + executable + "\" " + string.Join(" ", args.Select(value => "\"" + value + "\"")) + " 1>\"" + output + "\" 2>\"" + error + "\"\"";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /s /c " + line,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            }) ?? throw new InvalidOperationException("The elevated command did not start.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("The repair did not finish in time and may still be running.");
            }
            var text = File.Exists(output) ? await File.ReadAllTextAsync(output, CancellationToken.None).ConfigureAwait(false) : string.Empty;
            var problem = File.Exists(error) ? await File.ReadAllTextAsync(error, CancellationToken.None).ConfigureAwait(false) : string.Empty;
            var truncated = text.Length > MaximumOutput;
            return new KillerBenchRun(process.ExitCode, truncated ? text[..MaximumOutput] : text, Limit(problem, 65536), truncated);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<bool> DefaultConfirm(string? notifier, KillerBenchConfirmation confirmation, CancellationToken token)
    {
        notifier ??= Environment.GetEnvironmentVariable("KILLERMCP_NOTIFIER") ?? Path.Combine(AppContext.BaseDirectory, "KillerMCP.Notify.exe");
        if (!IsFile(notifier)) return false;
        var request = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(confirmation)));
        var result = await ProcessRunner.RunAsync(notifier, ["--confirm-repair", request], TimeSpan.FromMinutes(5), 4096, token).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static string Markdown(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(node, 0, text);
        return Limit(text.ToString(), 200_000);

        static void Write(JsonNode? node, int depth, StringBuilder text)
        {
            if (text.Length > 200_000 || depth > 6) return;
            var indent = new string(' ', depth * 2);
            switch (node)
            {
                case JsonObject value:
                    var name = (value["title"] ?? value["name"] ?? value["scope"] ?? value["id"])?.ToString();
                    var state = (value["effectiveState"] ?? value["state"] ?? value["outcome"])?.ToString();
                    var detail = value["detail"]?.ToString();
                    if (name is not null || state is not null)
                    {
                        text.Append(indent).Append("- **").Append(name ?? "Result").Append("**");
                        if (state is not null) text.Append(": ").Append(state);
                        if (detail is not null) text.Append(". ").Append(detail);
                        text.AppendLine();
                    }
                    foreach (var property in value)
                    {
                        if (property.Key is "title" or "name" or "scope" or "id" or "state" or "effectiveState" or "outcome" or "detail" or "evidence" or "output") continue;
                        if (property.Value is JsonArray or JsonObject)
                        {
                            if (property.Value is JsonArray { Count: 0 }) continue;
                            text.Append(indent).Append("- ").Append(property.Key).AppendLine(":");
                            Write(property.Value, depth + 1, text);
                        }
                        else if (property.Value is not null && name is null)
                        {
                            text.Append(indent).Append("- ").Append(property.Key).Append(": ").AppendLine(property.Value.ToString());
                        }
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array) Write(item, depth, text);
                    break;
                case null:
                    break;
                default:
                    text.Append(indent).Append("- ").AppendLine(node.ToString());
                    break;
            }
        }
    }

    private static string TierName(KillerBenchTier tier) => tier == KillerBenchTier.A ? "safe" : "disruptive";
    private static AppAdapter Tool(string name, string description, JsonObject schema, Func<JsonElement, CancellationToken, Task<AppCallResult>> call) => new(new RuntimeTool(name, description, schema), call);
    private static AppCallResult Ok(JsonObject result) => new(result.ToJsonString(), result["exitCode"]?.GetValue<int>() is int code && code is not (0 or 3));
    private static AppCallResult Ok(string text) => new(text);
    private static AppCallResult Error(string text) => new(text, true);
    private static string Limit(string text, int maximum = 4096) => text.Length <= maximum ? text : text[..maximum];
    private static bool IsFile(string? path) { try { return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && File.Exists(path); } catch { return false; } }

    private static JsonObject CheckSchema() => Schema(new JsonObject
    {
        ["check"] = Enum(Checks),
        ["host"] = Text(253),
        ["port"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 65535 },
        ["drive"] = Text(2),
        ["queues"] = new JsonObject { ["type"] = "array", ["items"] = Text(128), ["minItems"] = 1, ["maxItems"] = 8 },
    }, "check");

    private static JsonObject Schema(JsonObject properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        if (required.Length > 0) schema["required"] = new JsonArray(required.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray());
        return schema;
    }

    private static JsonObject Text(int maximum) => new() { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = maximum };
    private static JsonObject Boolean(bool fallback) => new() { ["type"] = "boolean", ["default"] = fallback };
    private static JsonObject Enum(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) };

    [GeneratedRegex("^(?:0[xX][0-9A-Fa-f]{1,8}|-?[0-9]{1,10})$", RegexOptions.CultureInvariant)] private static partial Regex Code();
    [GeneratedRegex("^[A-Za-z]{2,64}$", RegexOptions.CultureInvariant)] private static partial Regex ScopeName();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._()#,+'-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex QueueName();
}
