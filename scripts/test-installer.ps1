param(
    [string]$Installer = (Join-Path $PSScriptRoot '..\artifacts\installer\KillerMCP-Setup.exe')
)

$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path -LiteralPath $Installer).Path
$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-test-' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $scratch 'Installed'
$codexHome = Join-Path $scratch 'CodexHome'
$claudeHome = Join-Path $scratch 'ClaudeHome'
$previousInstallRoot = $env:KILLERMCP_TEST_INSTALL_ROOT
$previousRegister = $env:KILLERMCP_TEST_REGISTER_CODEX
$previousClaudeRegister = $env:KILLERMCP_TEST_REGISTER_CLAUDE
$previousCodexHome = $env:CODEX_HOME
$previousClaudeHome = $env:CLAUDE_CONFIG_DIR
$success = $false

try {
    New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
    New-Item -ItemType Directory -Path $claudeHome -Force | Out-Null
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $env:KILLERMCP_TEST_REGISTER_CODEX = '1'
    $env:KILLERMCP_TEST_REGISTER_CLAUDE = '1'
    $env:CODEX_HOME = $codexHome
    $env:CLAUDE_CONFIG_DIR = $claudeHome

    foreach ($pass in 1..2) {
        $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "Installer pass $pass failed with exit code $($process.ExitCode)." }
        node (Join-Path $PSScriptRoot 'installer-smoke.mjs') $installed
        if ($LASTEXITCODE -ne 0) { throw "Installed MCP check failed on pass $pass." }
    }

    $setupCopy = "$installed-Setup.exe"
    $uninstallKey = 'HKCU:\Software\KillerMCP\InstallerTests\' + (Split-Path $scratch -Leaf)
    $installedEntry = Get-ItemProperty -LiteralPath $uninstallKey
    if (-not (Test-Path -LiteralPath $setupCopy -PathType Leaf) -or
        $installedEntry.DisplayName -ne 'KillerMCP' -or
        $installedEntry.DisplayVersion -ne '0.1.0' -or
        $installedEntry.InstallLocation -ne $installed -or
        $installedEntry.UninstallString -ne ('"' + $setupCopy + '" /uninstall') -or
        $installedEntry.QuietUninstallString -ne ('"' + $setupCopy + '" /silent /uninstall')) {
        throw 'The installed setup copy or Windows uninstall entry is incorrect.'
    }

    $sentinel = Join-Path $installed 'user-file.txt'
    Set-Content -LiteralPath $sentinel -Value 'Keep this file'
    $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -eq 0 -or -not (Test-Path -LiteralPath $sentinel)) {
        throw 'Reinstall did not preserve an installation folder with an extra file.'
    }
    Remove-Item -LiteralPath $sentinel

    $runtimeFile = Join-Path $installed 'killermcp.mjs'
    $originalRuntime = [IO.File]::ReadAllBytes($runtimeFile)
    try {
        [IO.File]::AppendAllText($runtimeFile, '// modified')
        $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -eq 0) { throw 'Reinstall replaced a modified runtime file.' }
        $process = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -eq 0) { throw 'Uninstall removed a modified runtime file.' }
    }
    finally { [IO.File]::WriteAllBytes($runtimeFile, $originalRuntime) }

    $originalSetup = [IO.File]::ReadAllBytes($setupCopy)
    try {
        [IO.File]::AppendAllText($setupCopy, 'modified')
        $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -eq 0) { throw 'Reinstall replaced a modified setup file.' }
    }
    finally { [IO.File]::WriteAllBytes($setupCopy, $originalSetup) }

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
    $claudeRegistration = (Get-Content -LiteralPath (Join-Path $claudeHome '.claude.json') -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($claudeRegistration.type -ne 'stdio' -or
        $claudeRegistration.command -ne (Join-Path $installed 'node.exe') -or
        $claudeRegistration.args.Count -ne 1 -or
        $claudeRegistration.args[0] -ne (Join-Path $installed 'killermcp.mjs')) {
        throw 'Claude Code did not retain the expected one-connection configuration.'
    }

    $claudeSettings = Join-Path $claudeHome '.claude.json'
    $originalClaudeSettings = Get-Content -LiteralPath $claudeSettings -Raw
    $differentClaudeSettings = $originalClaudeSettings | ConvertFrom-Json
    $differentClaudeSettings.mcpServers.killermcp.command = 'C:\DifferentApp\node.exe'
    Set-Content -LiteralPath $claudeSettings -Value ($differentClaudeSettings | ConvertTo-Json -Depth 30)
    $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    $preserved = (Get-Content -LiteralPath $claudeSettings -Raw | ConvertFrom-Json).mcpServers.killermcp.command
    if ($process.ExitCode -eq 0 -or $preserved -ne 'C:\DifferentApp\node.exe') {
        throw 'Install replaced a different Claude Code connection.'
    }
    Set-Content -LiteralPath $claudeSettings -Value $originalClaudeSettings

    Set-Content -LiteralPath $sentinel -Value 'Keep this file'
    $process = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -eq 0 -or -not (Test-Path -LiteralPath $sentinel)) {
        throw 'Uninstall did not preserve an installation folder with an extra file.'
    }
    Remove-Item -LiteralPath $sentinel
    $extraDirectory = Join-Path $installed 'user-folder'
    New-Item -ItemType Directory -Path $extraDirectory | Out-Null
    $process = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -eq 0 -or -not (Test-Path -LiteralPath $extraDirectory)) {
        throw 'Uninstall did not preserve an extra installation directory.'
    }
    Remove-Item -LiteralPath $extraDirectory
    $process = Start-Process -FilePath $setupCopy -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    for ($attempt = 0; $attempt -lt 50 -and (Test-Path -LiteralPath $setupCopy); $attempt++) {
        Start-Sleep -Milliseconds 100
    }
    if ($process.ExitCode -ne 0 -or (Test-Path -LiteralPath $installed) -or
        (Test-Path -LiteralPath $setupCopy) -or (Test-Path -LiteralPath $uninstallKey)) {
        throw 'The installed KillerMCP setup did not remove its runtime and uninstall entry.'
    }
    $ErrorActionPreference = 'Continue'
    $removed = codex mcp get killermcp --json 2>$null
    $codexExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($codexExit -eq 0) { throw 'Uninstall left the KillerMCP Codex connection in place.' }
    $claudeRegistration = (Get-Content -LiteralPath (Join-Path $claudeHome '.claude.json') -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($null -ne $claudeRegistration) { throw 'Uninstall left the KillerMCP Claude Code connection in place.' }
    $success = $true
    Write-Output 'KillerMCP isolated install, reinstall, Installed Apps entry, Codex and Claude Code registration, MCP calls, and uninstall passed.'
}
finally {
    $env:KILLERMCP_TEST_INSTALL_ROOT = $previousInstallRoot
    $env:KILLERMCP_TEST_REGISTER_CODEX = $previousRegister
    $env:KILLERMCP_TEST_REGISTER_CLAUDE = $previousClaudeRegister
    $env:CODEX_HOME = $previousCodexHome
    $env:CLAUDE_CONFIG_DIR = $previousClaudeHome
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
