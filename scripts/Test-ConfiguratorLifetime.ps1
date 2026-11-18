param(
    [Parameter(Mandatory = $true)][string]$ConfiguratorPath,
    [Parameter(Mandatory = $true)][string]$ScratchPath,
    [Parameter(Mandatory = $true)][string]$LabMachine,
    [switch]$NestedJob
)

$ErrorActionPreference = 'Stop'
if ($env:COMPUTERNAME -eq 'SHREDDER' -or ($env:COMPUTERNAME -ne $LabMachine -and $env:CI -ne 'true')) { throw 'Run this process test on the explicitly selected lab machine or CI.' }
$configurator = (Resolve-Path -LiteralPath $ConfiguratorPath).Path
$scratch = [IO.Path]::GetFullPath($ScratchPath)
if (Test-Path -LiteralPath $scratch) { throw 'Use a new owned scratch directory for each run.' }
[IO.Directory]::CreateDirectory($scratch) | Out-Null
if ($NestedJob) { . (Join-Path $PSScriptRoot 'ClientJob.ps1') }
$fake = Join-Path $scratch 'OwnedFakeCodex.exe'
Add-Type -OutputType ConsoleApplication -OutputAssembly $fake -TypeDefinition @'
using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
public static class OwnedFakeCodex {
    static void Idle() { Thread.Sleep(60000); }
    public static void Main(string[] args) {
        string root = Environment.GetEnvironmentVariable("KM_LIFETIME_ROOT");
        string self = Process.GetCurrentProcess().MainModule.FileName;
        if (args.Length > 0 && args[0] == "idle") { Idle(); return; }
        if (args.Length > 0 && args[0] == "grandchild") {
            File.WriteAllText(Path.Combine(root, "grandchild.pid"), Process.GetCurrentProcess().Id.ToString());
            Idle(); return;
        }
        if (args.Length > 0 && args[0] == "child") {
            File.WriteAllText(Path.Combine(root, "child.pid"), Process.GetCurrentProcess().Id.ToString());
            while (!File.Exists(Path.Combine(root, "spawn"))) Thread.Sleep(10);
            Process.Start(new ProcessStartInfo(self, "grandchild") { UseShellExecute = false, CreateNoWindow = true });
            Idle(); return;
        }
        if (Environment.GetEnvironmentVariable("KM_LIFETIME_NORMAL") == "1") {
            string host = Environment.GetEnvironmentVariable("KM_LIFETIME_HOST");
            Console.WriteLine("{\"transport\":{\"type\":\"stdio\",\"command\":\"" + host.Replace("\\", "\\\\") + "\",\"args\":[]}}");
            return;
        }
        File.WriteAllText(Path.Combine(root, "codex.pid"), Process.GetCurrentProcess().Id.ToString());
        Process.Start(new ProcessStartInfo(self, "child") { UseShellExecute = false, CreateNoWindow = true });
        Idle();
    }
}
'@
$names = @('USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'ProgramFiles', 'PATH', 'CODEX_CLI_PATH', 'CLAUDE_CLI_PATH', 'KM_LIFETIME_ROOT', 'KM_LIFETIME_NORMAL', 'KM_LIFETIME_HOST')
$clients = @('CLAUDE', 'CURSOR', 'COPILOT', 'GEMINI', 'WINDSURF', 'CLAUDE_DESKTOP')
$names += @($clients | ForEach-Object { 'KILLERMCP_TEST_' + $_ + '_CONFIG' })
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$owned = New-Object 'Collections.Generic.List[Diagnostics.Process]'
$jobs = New-Object 'Collections.Generic.List[IDisposable]'
$failures = New-Object 'Collections.Generic.List[string]'
function Start-Owned([string]$path, [string]$arguments, [bool]$inJob = $false) {
    if ($inJob) {
        $job = New-Object KillerMCPTests.ClientJob
        $jobs.Add($job)
        $process = [Diagnostics.Process]::GetProcessById($job.Start($path, $arguments))
    } else {
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $path
        $info.Arguments = $arguments
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $process = [Diagnostics.Process]::Start($info)
    }
    # Open a retained handle before a short-lived nested-job process exits.
    $null = $process.Handle
    $owned.Add($process)
    return $process
}
function Wait-Marker([string]$name) {
    $path = Join-Path $scratch $name
    $end = [DateTime]::UtcNow.AddSeconds(10)
    while (!(Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $end) { Start-Sleep -Milliseconds 20 }
    if (!(Test-Path -LiteralPath $path)) { throw ('Fixture did not reach ' + $name) }
    $number = [int](Get-Content -LiteralPath $path -Raw)
    $process = [Diagnostics.Process]::GetProcessById($number)
    $owned.Add($process)
    return $process
}
try {
    foreach ($name in @('USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'ProgramFiles')) { [Environment]::SetEnvironmentVariable($name, (Join-Path $scratch $name)) }
    $env:PATH = $scratch
    $env:CODEX_CLI_PATH = $fake
    $env:CLAUDE_CLI_PATH = Join-Path $scratch 'absent.exe'
    $env:KM_LIFETIME_ROOT = $scratch
    $env:KM_LIFETIME_NORMAL = '0'
    # The configurator registers only the KillerMCP.exe beside it, so the fixture gets its own copy of the
    # configurator folder with the owned fake host standing in as KillerMCP.exe.
    $configureCopy = Join-Path $scratch 'configure'
    Copy-Item -LiteralPath (Split-Path -Parent $configurator) -Destination $configureCopy -Recurse
    $configurator = Join-Path $configureCopy (Split-Path -Leaf $configurator)
    $env:KM_LIFETIME_HOST = Join-Path $configureCopy 'KillerMCP.exe'
    Copy-Item -LiteralPath $fake -Destination $env:KM_LIFETIME_HOST -Force
    foreach ($client in $clients) { [Environment]::SetEnvironmentVariable(('KILLERMCP_TEST_' + $client + '_CONFIG'), (Join-Path $scratch ($client + '.json'))) }
    $unrelated = Start-Owned $fake 'idle'
    $root = Start-Owned $configurator ('register "' + $env:KM_LIFETIME_HOST + '"') $NestedJob.IsPresent
    $codex = Wait-Marker 'codex.pid'
    $child = Wait-Marker 'child.pid'
    $root.Kill()
    if (!$root.WaitForExit(5000)) { throw 'Owned configurator did not terminate.' }
    [IO.File]::WriteAllText((Join-Path $scratch 'spawn'), 'create after configurator termination')
    Start-Sleep -Milliseconds 1000
    $grandchild = $null
    if (Test-Path -LiteralPath (Join-Path $scratch 'grandchild.pid')) { $grandchild = Wait-Marker 'grandchild.pid' }
    if (!$codex.HasExited -or !$child.HasExited -or ($null -ne $grandchild -and !$grandchild.HasExited)) {
        $failures.Add('An owned descendant survived configurator termination; late grandchild created: ' + ($null -ne $grandchild))
    } else { Write-Output 'PASS: termination ended owned descendants before the gated late grandchild launch.' }
    if ($unrelated.HasExited) { $failures.Add('Unrelated owned process was terminated.') } else { Write-Output 'PASS: unrelated process survived.' }
    $env:KM_LIFETIME_NORMAL = '1'
    $normal = Start-Owned $configurator ('register "' + $env:KM_LIFETIME_HOST + '"') $NestedJob.IsPresent
    if (!$normal.WaitForExit(10000)) { throw 'Normal registration timed out.' }
    if ($normal.ExitCode -ne 0) { $failures.Add('Normal registration exit: ' + $normal.ExitCode) } else { Write-Output 'PASS: normal registration exited zero.' }
    Write-Output ('Selected 3 checks; failed ' + $failures.Count + '; nested job: ' + $NestedJob.IsPresent)
    foreach ($failure in $failures) { Write-Output ('FAIL: ' + $failure) }
} finally {
    foreach ($process in $owned) {
        try { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null } } catch { }
        $process.Dispose()
    }
    foreach ($job in $jobs) { $job.Dispose() }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
if ($failures.Count -gt 0) { throw ($failures.Count.ToString() + ' configurator lifetime checks failed.') }
