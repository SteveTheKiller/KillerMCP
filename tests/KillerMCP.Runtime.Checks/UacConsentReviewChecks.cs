using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using KillerMCP;

internal static class UacConsentReviewChecks
{
    public static Process StartFixture(ProcessStartInfo start, string behavior = "normal", string marker = "")
    {
        var request = start.ArgumentList.Last();
        start.ArgumentList.Clear();
        start.ArgumentList.Add("--uac-helper-fixture");
        start.ArgumentList.Add(request);
        start.ArgumentList.Add(behavior);
        start.ArgumentList.Add(marker);
        start.UseShellExecute = false;
        start.Verb = string.Empty;
        return Process.Start(start)!;
    }

    public static async Task<bool> ServeAsync(string[] args)
    {
        if (args.Length == 1 && args[0] == "--uac-token-fixture")
        {
            RequireGuest();
            if (!OperatingSystem.IsWindows()) throw new Exception("Windows fixture required.");
            Console.Write(JsonSerializer.Serialize(new { Administrator = Environment.IsPrivilegedProcess, UserSid = WindowsIdentity.GetCurrent().User!.Value }));
            return true;
        }
        if (args.Length != 4 || args[0] != "--uac-helper-fixture") return false;
        RequireGuest();
        if (!OperatingSystem.IsWindows()) throw new Exception("Windows fixture required.");
        using var request = JsonDocument.Parse(Convert.FromBase64String(args[1]));
        var pipeName = request.RootElement.GetProperty("Pipe").GetString()!;
        var elevate = !request.RootElement.TryGetProperty("Elevate", out var flag) || flag.GetBoolean();
        using var pipe = new NamedPipeClientStream(".", pipeName, (PipeAccessRights)0x0012019b,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous, HandleInheritability.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await pipe.ConnectAsync(deadline.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != request.RootElement.GetProperty("ParentId").GetInt32())
                throw new Exception("Fixture connected to the wrong server.");
            var acl = pipe.GetAccessControl();
            if (!acl.AreAccessRulesProtected || acl.GetOwner(typeof(SecurityIdentifier))?.Value != WindowsIdentity.GetCurrent().User!.Value)
                throw new Exception("Repair pipe does not have a protected original-user owner/DACL.");
            var adminAllowed = false;
            foreach (PipeAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = rule.IdentityReference.Value;
                if (sid != WindowsIdentity.GetCurrent().User!.Value && sid != "S-1-5-32-544") throw new Exception("Repair pipe admits an unexpected principal.");
                if (sid == "S-1-5-32-544" && rule.AccessControlType == AccessControlType.Allow && (rule.PipeAccessRights & PipeAccessRights.ReadWrite) == PipeAccessRights.ReadWrite)
                    adminAllowed = true;
            }
            if (!adminAllowed) throw new Exception("Repair pipe does not admit alternate administrators.");
            if (!elevate && (request.RootElement.GetProperty("Executable").GetString() != "" || request.RootElement.GetProperty("Arguments").GetArrayLength() != 0))
                throw new Exception("Consent-only request carried an executable.");
            var ready = new { Kind = args[2] == "bad-ready" ? "Result" : "Ready", Pipe = pipeName, Elevate = elevate, Administrator = true, UserSid = WindowsIdentity.GetCurrent().User!.Value };
            await Write(pipe, ready, deadline.Token);
            using var commit = await Read(pipe, deadline.Token);
            if (commit.RootElement.GetProperty("Kind").GetString() != "Commit" || commit.RootElement.GetProperty("Pipe").GetString() != pipeName || commit.RootElement.GetProperty("Elevate").GetBoolean() != elevate)
                throw new Exception("Fixture received invalid Commit.");
            if (args[3].Length > 0) File.WriteAllText(args[3], "committed");
            if (args[2] == "missing-result") return true;
            await Write(pipe, new KillerBenchRun(args[2] == "bad-consent" ? 3 : 0,
                elevate ? "{\"outcome\":\"Failed\"}" : JsonSerializer.Serialize(ready), "", false), deadline.Token);
        }
        catch (IOException) { }
        return true;
    }

    public static async Task RunLiveAsync()
    {
        RequireGuest(live: true);
        if (!OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess) throw new Exception("Run this fixture normally on the authorized Windows guest.");
        var original = WindowsIdentity.GetCurrent().User!.Value;
        Console.WriteLine("LIVE: approve two UAC requests. Only token reports and a consent-only exchange will run.");
        var elevated = await ElevatedRepairBridge.RunAsync(Environment.ProcessPath!, ["--uac-token-fixture"], TimeSpan.FromSeconds(60), CancellationToken.None);
        using var admin = JsonDocument.Parse(elevated.Output);
        if (elevated.ExitCode != 0 || !admin.RootElement.GetProperty("Administrator").GetBoolean()) throw new Exception("Elevated token report failed.");
        Console.WriteLine("PASS LIVE: elevated helper executed harmless token report: " + elevated.Output);
        var consent = await ElevatedRepairBridge.RunAsync(Environment.ProcessPath!, ["--uac-token-fixture"], TimeSpan.FromSeconds(60), CancellationToken.None, elevate: false);
        using var receipt = JsonDocument.Parse(consent.Output);
        if (consent.ExitCode != 0 || !receipt.RootElement.GetProperty("Administrator").GetBoolean()) throw new Exception("Consent-only helper was not elevated.");
        var user = await ProcessRunner.RunAsync(Environment.ProcessPath!, ["--uac-token-fixture"], TimeSpan.FromSeconds(15), 4096);
        using var privacy = JsonDocument.Parse(user.StandardOutput);
        if (privacy.RootElement.GetProperty("Administrator").GetBoolean() || privacy.RootElement.GetProperty("UserSid").GetString() != original)
            throw new Exception("Original-user operation changed identity after administrator consent.");
        Console.WriteLine("PASS LIVE: consent-only helper " + consent.Output + "; original-user operation " + user.StandardOutput);
    }

    public static async Task RunAsync()
    {
        RequireGuest();
        var root = Path.Combine(Path.GetTempPath(), "killermcp-uac-" + Guid.NewGuid().ToString("N"));
        using var folders = KillerBenchSettings.UseTestFolders(root, Path.Combine(root, "bench"));
        var previous = Environment.GetEnvironmentVariable("KILLERMCP_FAKE_BENCH");
        Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", "1");
        try
        {
            KillerBenchSettings.WriteLevel(KillerBenchRepairLevel.Safe);
            var hash = KillerBenchSettings.ReadSettings().Hash!;
            var prompts = 0;
            KillerBenchAdapter.ElevatedProcessStartForChecks = _ => { prompts++; throw new Win32Exception(1223); };
            var tools = (await KillerBenchAdapter.CreateAsync(Environment.ProcessPath, null, new KillerBenchOptions
            {
                Confirm = (_, _) => Task.FromResult(true),
            })).ToDictionary(item => item.Tool.Name);
            async Task<JsonElement> Call(string name, object value)
            {
                using var input = JsonDocument.Parse(JsonSerializer.Serialize(value));
                var response = await tools[name].CallAsync(input.RootElement, CancellationToken.None);
                if (response.IsError) throw new Exception(response.Text);
                return JsonDocument.Parse(response.Text).RootElement.GetProperty("result").Clone();
            }
            var preview = await Call("killerbench_repair_preview", new { action = "privacy_setting", options = new { key = "camera-access", state = "off" } });
            var result = await Call("killerbench_repair_execute", new { token = preview.GetProperty("token").GetString() });
            if (prompts != 1 || result.GetProperty("outcome").GetString() != "NotApproved")
                throw new Exception("Privacy repair ran without fresh administrator consent after review acceptance.");
            if (KillerBenchSettings.IsApproved(hash)) throw new Exception("Plain review acceptance cached settings approval without administrator consent.");
            Console.WriteLine("PASS: privacy repair requires fresh administrator consent and review acceptance alone cannot approve settings.");

            KillerBenchAdapter.ElevatedProcessStartForChecks = _ => throw new Win32Exception(5);
            var startup = await Call("killerbench_repair_preview", new { action = "privacy_setting", options = new { key = "camera-access", state = "off" } });
            var failedStart = await Call("killerbench_repair_execute", new { token = startup.GetProperty("token").GetString() });
            if (failedStart.GetProperty("outcome").GetString() != "NotApproved" || KillerBenchSettings.ReadLock() is not null)
                throw new Exception("Helper startup failure created an uncertain-repair lock before any Commit.");
            Console.WriteLine("PASS: helper startup failure is not approval and never creates a repair lock.");

            KillerBenchAdapter.ElevatedProcessStartForChecks = start => StartFixture(start, "bad-consent");
            var badReceipt = await Call("killerbench_repair_preview", new { action = "privacy_setting", options = new { key = "camera-access", state = "off" } });
            var refusedReceipt = await Call("killerbench_repair_execute", new { token = badReceipt.GetProperty("token").GetString() });
            if (refusedReceipt.GetProperty("outcome").GetString() != "NotApproved" || KillerBenchSettings.IsApproved(hash) || KillerBenchSettings.ReadLock() is not null)
                throw new Exception("Failed consent-only receipt authorized original-user execution or approved settings.");
            Console.WriteLine("PASS: failed consent-only receipt cannot authorize original-user execution or approve settings.");

            var marker = Path.Combine(root, "commit.txt");
            KillerBenchSettings.WriteLevel(KillerBenchRepairLevel.Safe);
            KillerBenchAdapter.ElevatedProcessStartForChecks = start =>
            {
                prompts++;
                KillerBenchSettings.WriteLevel(KillerBenchRepairLevel.Off);
                return StartFixture(start, marker: marker);
            };
            var late = await Call("killerbench_repair_preview", new { action = "privacy_setting", options = new { key = "camera-access", state = "off" } });
            var aborted = await Call("killerbench_repair_execute", new { token = late.GetProperty("token").GetString() });
            if (aborted.GetProperty("outcome").GetString() != "NotApproved" || File.Exists(marker) || KillerBenchSettings.ReadLock() is not null)
                throw new Exception("Settings change while UAC waited did not abort before Commit without locking.");
            if (KillerBenchSettings.IsApproved(hash)) throw new Exception("Changed settings were cached before Commit.");
            Console.WriteLine("PASS: settings change during UAC wait aborts before Commit without caching or a repair lock.");

            foreach (var behavior in new[] { "bad-ready", "wrong-pid", "refused-commit" })
            {
                var rejected = false;
                try
                {
                    await ElevatedRepairBridge.RunAsync(Environment.ProcessPath!, ["--uac-token-fixture"], TimeSpan.FromSeconds(8), CancellationToken.None,
                        start =>
                        {
                            var launched = StartFixture(start, behavior, marker);
                            if (behavior == "wrong-pid") { launched.Dispose(); return Process.GetCurrentProcess(); }
                            return launched;
                        }, beforeCommit: () => { if (behavior == "refused-commit") throw new RepairConsentException("Fixture refused Commit."); });
                }
                catch (RepairConsentException) { rejected = true; }
                if (!rejected || File.Exists(marker)) throw new Exception("Invalid helper or refused Commit dispatched: " + behavior);
                Console.WriteLine("PASS: " + behavior + " fails closed before dispatch.");
            }
            var dispatched = false;
            try
            {
                await ElevatedRepairBridge.RunAsync(Environment.ProcessPath!, ["--uac-token-fixture"], TimeSpan.FromSeconds(8), CancellationToken.None,
                    start => StartFixture(start, "missing-result", marker));
            }
            catch (IOException) { dispatched = true; }
            if (!dispatched || !File.Exists(marker)) throw new Exception("Missing result after Commit did not preserve execution uncertainty.");
            File.Delete(marker);
            Console.WriteLine("PASS: missing result after execution Commit remains uncertain.");

            KillerBenchSettings.WriteLevel(KillerBenchRepairLevel.Safe);
            var consentLaunches = 0;
            KillerBenchAdapter.ElevatedProcessStartForChecks = start => { consentLaunches++; return StartFixture(start, marker: marker); };
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var privacyPreview = await Call("killerbench_repair_preview", new { action = "privacy_setting", options = new { key = "camera-access", state = "off" } });
                var completed = await Call("killerbench_repair_execute", new { token = privacyPreview.GetProperty("token").GetString() });
                if (completed.GetProperty("outcome").GetString() != "Completed" || !File.Exists(marker))
                    throw new Exception("Original-user privacy execution did not follow committed administrator consent.");
                File.Delete(marker);
            }
            if (consentLaunches != 2 || !KillerBenchSettings.IsApproved(hash))
                throw new Exception("Cached settings skipped per-repair UAC or successful consent did not approve unchanged settings.");
            Console.WriteLine("PASS: every privacy repair, including cached settings, uses exactly one consent-only helper before original-user execution.");
            Console.WriteLine("Selected UAC consent checks: 9 passed.");
        }
        finally
        {
            KillerBenchAdapter.ElevatedProcessStartForChecks = null;
            Environment.SetEnvironmentVariable("KILLERMCP_FAKE_BENCH", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void RequireGuest(bool live = false)
    {
        if (!OperatingSystem.IsWindows() || string.Equals(Environment.MachineName, "SHREDDER", StringComparison.OrdinalIgnoreCase)
            || (live && !string.Equals(Environment.MachineName, "SN01", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Run automated UAC fixtures in a Windows lab or CI. Live administrator approval is restricted to SN01.");
    }

    private static async Task Write<T>(Stream pipe, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await pipe.WriteAsync(BitConverter.GetBytes(bytes.Length), token);
        await pipe.WriteAsync(bytes, token);
        await pipe.FlushAsync(token);
    }

    private static async Task<JsonDocument> Read(Stream pipe, CancellationToken token)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, token);
        var size = BitConverter.ToInt32(header);
        if (size < 1 || size > 4096) throw new Exception("Fixture frame length is invalid.");
        var bytes = new byte[size];
        await pipe.ReadExactlyAsync(bytes, token);
        return JsonDocument.Parse(bytes);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);
}
