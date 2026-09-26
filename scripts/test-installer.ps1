param(
    [string]$Installer = (Join-Path $PSScriptRoot '..\artifacts\installer\KillerMCP-Setup.exe')
)

$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path -LiteralPath $Installer).Path
$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-test-' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $scratch 'Installed'
$codexHome = Join-Path $scratch 'CodexHome'
$previousInstallRoot = $env:KILLERMCP_TEST_INSTALL_ROOT
$previousRegister = $env:KILLERMCP_TEST_REGISTER_CODEX
$previousCodexHome = $env:CODEX_HOME
$success = $false

try {
    New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $env:KILLERMCP_TEST_REGISTER_CODEX = '1'
    $env:CODEX_HOME = $codexHome

    foreach ($pass in 1..2) {
        $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "Installer pass $pass failed with exit code $($process.ExitCode)." }
        node (Join-Path $PSScriptRoot 'installer-smoke.mjs') $installed
        if ($LASTEXITCODE -ne 0) { throw "Installed MCP check failed on pass $pass." }
    }

    $ErrorActionPreference = 'Continue'
    $registration = codex mcp get killermcp --json 2>$null | ConvertFrom-Json
    $codexExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($codexExit -ne 0 -or
        $registration.transport.type -ne 'stdio' -or
        $registration.transport.command -ne (Join-Path $installed 'node.exe') -or
        $registration.transport.args.Count -ne 1 -or
        $registration.transport.args[0] -ne (Join-Path $installed 'killermcp.mjs')) {
        throw 'Codex did not retain the expected one-connection configuration.'
    }
    $success = $true
    Write-Output 'KillerMCP isolated install, reinstall, Codex registration, and MCP calls passed.'
}
finally {
    $env:KILLERMCP_TEST_INSTALL_ROOT = $previousInstallRoot
    $env:KILLERMCP_TEST_REGISTER_CODEX = $previousRegister
    $env:CODEX_HOME = $previousCodexHome
    $temporary = [IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($scratch)
    if ($success -and $target.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($target).StartsWith('KillerMCP-test-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $target) {
        Write-Output "Installer test files retained at $target"
    }
}
