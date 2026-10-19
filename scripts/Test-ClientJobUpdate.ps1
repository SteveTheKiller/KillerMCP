param([string]$Installer = '')

# Agent clients such as Codex run each KillerMCP host inside a job object that kills every
# process in it when the client tears the job down. An update started from the host's update
# window runs setup inside that same job, and setup closes the running hosts to replace them.
# This test closes the job the moment the host dies, as the client does, and requires the
# upgrade to finish anyway.
$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { throw 'The client job update test requires Windows.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Installer) { & (Join-Path $PSScriptRoot 'Build-WindowsInstaller.ps1') | Out-Null }
$setup = if ($Installer) { [IO.Path]::GetFullPath($Installer) } else { Join-Path $root 'artifacts\installer\KillerMCP-Setup.exe' }
$setupVersion = [version](Get-Item -LiteralPath $setup).VersionInfo.FileVersion
$version = "$($setupVersion.Major).$($setupVersion.Minor).$($setupVersion.Build)"
dotnet build (Join-Path $root 'tests\KillerMCP.Clients.Checks\KillerMCP.Clients.Checks.csproj') -c Release -v:q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The fake Codex client build failed.' }
$fakeCodex = Join-Path $root 'tests\KillerMCP.Clients.Checks\bin\Release\net10.0\KillerMCP.Clients.Checks.exe'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf) -or -not (Test-Path -LiteralPath $fakeCodex -PathType Leaf)) { throw 'Client job update test prerequisites are missing.' }

if (-not ('KillerMCPTests.ClientJob' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace KillerMCPTests {
public sealed class ClientJob : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long UserTime; public long JobTime; public uint Flags; public UIntPtr MinSet; public UIntPtr MaxSet; public uint ActiveLimit; public UIntPtr Affinity; public uint Priority; public uint Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong A, B, C, D, E, F; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct StartupInfo { public int cb; public string Reserved, Desktop, Title; public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags; public short ShowWindow, Reserved2; public IntPtr Reserved3, StdInput, StdOutput, StdError; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcess(string application, System.Text.StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    IntPtr job;

    // A kill-on-close job without breakaway, the strictest form an agent client can use.
    public ClientJob() {
        job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var limits = new ExtendedLimits();
        limits.Basic.Flags = 0x2000;
        if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(typeof(ExtendedLimits)))) throw new System.ComponentModel.Win32Exception();
    }

    public void Add(IntPtr process) { if (!AssignProcessToJobObject(job, process)) throw new System.ComponentModel.Win32Exception(); }

    // Starts a process already inside the job, as a child of a process in the job would be.
    public int Start(string path, string arguments) {
        var startup = new StartupInfo(); startup.cb = Marshal.SizeOf(typeof(StartupInfo));
        ProcessInfo info;
        var command = new System.Text.StringBuilder("\"" + path + "\" " + arguments);
        if (!CreateProcess(path, command, IntPtr.Zero, IntPtr.Zero, false, 0x4, IntPtr.Zero, null, ref startup, out info)) throw new System.ComponentModel.Win32Exception();
        try {
            Add(info.Process);
            ResumeThread(info.Thread);
            return info.ProcessId;
        } finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
    }

    public bool Closed { get { return job == IntPtr.Zero; } }
    public void Dispose() { if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; } }
}
}
'@
}

$utf8NoBom = New-Object Text.UTF8Encoding($false)
$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-client-job-' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $scratch 'Installed'
$names = @('CLAUDE', 'CURSOR', 'COPILOT', 'GEMINI', 'WINDSURF', 'CLAUDE_DESKTOP')
$variables = @('KILLERMCP_TEST_INSTALL_ROOT', 'KILLERMCP_TEST_RUNTIME', 'KILLERMCP_FAKE_CODEX_STATE', 'KILLERMCP_TOOL_CALL_LOG', 'CODEX_CLI_PATH', 'KILLERMCP_UPDATE_CACHE', 'KILLERMCP_UPDATE_API', 'KILLERMCP_NOTIFIER') + ($names | ForEach-Object { "KILLERMCP_TEST_${_}_CONFIG" })
$previous = @{}
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$uninstallKey = 'HKCU:\Software\KillerMCP\InstallerTests\' + (Split-Path $scratch -Leaf)
$runKey = "$uninstallKey\Run"
$hostProcess = $null
$job = $null
$success = $false

function Get-SetupProcesses {
    # Setup may rerun itself outside the job, so any running setup counts until the upgrade ends.
    Get-CimInstance Win32_Process -Filter "Name = 'KillerMCP-Setup.exe'"
}

try {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $env:KILLERMCP_TEST_RUNTIME = '1'
    $env:KILLERMCP_TOOL_CALL_LOG = Join-Path $scratch 'tool-calls.jsonl'
    $env:KILLERMCP_FAKE_CODEX_STATE = Join-Path $scratch 'codex-state.json'
    $env:CODEX_CLI_PATH = $fakeCodex
    $env:KILLERMCP_UPDATE_CACHE = Join-Path $scratch 'update-status.json'
    $env:KILLERMCP_UPDATE_API = 'https://127.0.0.1:9/no-release'
    $env:KILLERMCP_NOTIFIER = Join-Path $scratch 'no-notifier.exe'
    foreach ($name in $names) {
        $path = Join-Path $scratch "$name\settings.json"
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, '{}', $utf8NoBom)
        [Environment]::SetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG", $path)
    }

    $install = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    if ($install.ExitCode -ne 0) { throw "The first install failed with exit code $($install.ExitCode)." }
    # Markers that only the end of a completed upgrade rewrites.
    Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value '0.0.1'
    Remove-ItemProperty -LiteralPath $runKey -Name 'KillerMCP Update Check'

    $job = New-Object KillerMCPTests.ClientJob
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $installed 'KillerMCP.exe'))
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $hostProcess = [Diagnostics.Process]::Start($start)
    $job.Add($hostProcess.Handle)
    Start-Sleep -Milliseconds 500
    if ($hostProcess.HasExited) { throw 'The installed MCP host exited before the update started.' }

    # Setup starts inside the client's job, as it does when the update window launches it.
    $null = $job.Start($setup, '/silent')
    $deadline = (Get-Date).AddSeconds(120)
    $hostSeenExited = $false
    while ((Get-Date) -lt $deadline) {
        if (-not $job.Closed -and $hostProcess.HasExited) { $hostSeenExited = $true; $job.Dispose() }
        if ($job.Closed -and -not (Get-SetupProcesses)) { break }
        Start-Sleep -Milliseconds 20
    }
    if (-not $hostSeenExited) { throw 'Setup never closed the running MCP host.' }
    if (Get-SetupProcesses) { throw 'Setup was still running after two minutes.' }

    $entry = Get-ItemProperty -LiteralPath $uninstallKey
    $updateCheck = (Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).'KillerMCP Update Check'
    $hostVersion = (Get-Item -LiteralPath (Join-Path $installed 'KillerMCP.exe')).VersionInfo.FileVersion
    if ($entry.DisplayVersion -ne $version) { throw "The upgrade did not finish after the client closed its job: the uninstall entry still says $($entry.DisplayVersion), expected $version." }
    if (-not $updateCheck) { throw 'The upgrade did not finish after the client closed its job: the update check was not registered.' }
    if (-not $hostVersion.StartsWith($version)) { throw "The installed host is $hostVersion, expected $version." }

    $uninstall = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($uninstall.ExitCode -ne 0) { throw "Uninstall after the client job update failed with exit code $($uninstall.ExitCode)." }
    $success = $true
    Write-Output 'Setup finished an upgrade after the agent client closed the job it started in.'
}
finally {
    if ($job) { $job.Dispose() }
    if ($hostProcess) { if (-not $hostProcess.HasExited) { $hostProcess.Kill() }; $hostProcess.Dispose() }
    Get-SetupProcesses | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    if ($success) {
        Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    }
    elseif (Test-Path -LiteralPath $scratch) { Write-Output "Client job update test files retained at $scratch" }
}
