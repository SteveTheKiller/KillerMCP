param([string]$Installer = '')

# Updates started by the update window or the update check run setup with /silent /notify, so
# nobody watches its console. When such an update cannot install, setup must show why.
$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { throw 'The update failure test requires Windows.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Installer) { & (Join-Path $PSScriptRoot 'Build-WindowsInstaller.ps1') | Out-Null }
$setup = if ($Installer) { [IO.Path]::GetFullPath($Installer) } else { Join-Path $root 'artifacts\installer\KillerMCP-Setup.exe' }
dotnet build (Join-Path $root 'tests\KillerMCP.Clients.Checks\KillerMCP.Clients.Checks.csproj') -c Release -v:q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The fake Codex client build failed.' }
$fakeCodex = Join-Path $root 'tests\KillerMCP.Clients.Checks\bin\Release\net10.0\KillerMCP.Clients.Checks.exe'

$utf8NoBom = New-Object Text.UTF8Encoding($false)
$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-notify-failure-' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $scratch 'Installed'
$names = @('CLAUDE', 'CURSOR', 'COPILOT', 'GEMINI', 'WINDSURF', 'CLAUDE_DESKTOP')
$variables = @('KILLERMCP_TEST_INSTALL_ROOT', 'KILLERMCP_TEST_RUNTIME', 'KILLERMCP_FAKE_CODEX_STATE', 'KILLERMCP_TOOL_CALL_LOG', 'CODEX_CLI_PATH') + ($names | ForEach-Object { "KILLERMCP_TEST_${_}_CONFIG" })
$previous = @{}
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$uninstallKey = 'HKCU:\Software\KillerMCP\InstallerTests\' + (Split-Path $scratch -Leaf)
$sentinel = Join-Path $installed 'user-file.txt'

$testStart = Get-Date
# Only setup processes this test started, never one the user is running. Setup reruns itself
# through WMI inside an agent client, and this process cannot open that copy, so it is found
# and closed through WMI as well.
function Get-TestSetups {
    Get-CimInstance Win32_Process -Filter "Name = 'KillerMCP-Setup.exe'" | Where-Object { $_.CreationDate -ge $testStart }
}
if (-not ('KillerMCPTests.TopWindows' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace KillerMCPTests {
public static class TopWindows {
    delegate bool EnumProc(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr state);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    // Titles of the visible top-level windows that belong to the given process.
    public static string[] Titles(int processId) {
        var titles = new List<string>();
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window)) {
                var text = new StringBuilder(256);
                GetWindowText(window, text, text.Capacity);
                if (text.Length > 0) titles.Add(text.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }
}
}
'@
}
function Get-SetupWindowTitles {
    Get-TestSetups | ForEach-Object { [KillerMCPTests.TopWindows]::Titles([int]$_.ProcessId) }
}

try {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $env:KILLERMCP_TEST_RUNTIME = '1'
    $env:KILLERMCP_TOOL_CALL_LOG = Join-Path $scratch 'tool-calls.jsonl'
    $env:KILLERMCP_FAKE_CODEX_STATE = Join-Path $scratch 'codex-state.json'
    $env:CODEX_CLI_PATH = $fakeCodex
    foreach ($name in $names) {
        $path = Join-Path $scratch "$name\settings.json"
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, '{}', $utf8NoBom)
        [Environment]::SetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG", $path)
    }
    $install = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    if ($install.ExitCode -ne 0) { throw "The first install failed with exit code $($install.ExitCode)." }

    # A file setup does not own makes the next install refuse, as the installer test checks.
    Set-Content -LiteralPath $sentinel -Value 'keep'
    Start-Process -FilePath $setup -ArgumentList '/silent', '/notify'
    $deadline = (Get-Date).AddSeconds(30)
    $title = $null
    while ((Get-Date) -lt $deadline -and -not $title) { $title = Get-SetupWindowTitles | Select-Object -First 1; if (-not $title) { Start-Sleep -Milliseconds 100 } }
    if (-not $title) { throw 'A failed /notify update closed without telling the user why.' }
    if ($title -ne 'KillerMCP Setup') { throw "The failure appeared in an unexpected window: $title." }
    Write-Output 'A failed update started with /notify showed its error.'
}
finally {
    Get-TestSetups | ForEach-Object { $null = Invoke-CimMethod -InputObject $_ -MethodName Terminate }
    Start-Sleep -Milliseconds 300
    Remove-Item -LiteralPath $sentinel -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $installed) { Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -WindowStyle Hidden }
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}
