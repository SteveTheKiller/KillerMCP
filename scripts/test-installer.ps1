param(
    [string]$Installer = (Join-Path $PSScriptRoot '..\artifacts\installer\KillerMCP-Setup.exe')
)

$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path -LiteralPath $Installer).Path
$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-test-' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $scratch 'Installed'
$codexHome = Join-Path $scratch 'CodexHome'
$claudeHome = Join-Path $scratch 'ClaudeHome'
$cursorConfiguration = Join-Path $scratch 'CursorHome\mcp.json'
$copilotConfiguration = Join-Path $scratch 'CopilotHome\mcp-config.json'
$geminiConfiguration = Join-Path $scratch 'GeminiHome\settings.json'
$windsurfConfiguration = Join-Path $scratch 'WindsurfHome\mcp_config.json'
$claudeDesktopConfiguration = Join-Path $scratch 'ClaudeDesktopHome\claude_desktop_config.json'
$previousInstallRoot = $env:KILLERMCP_TEST_INSTALL_ROOT
$previousRegister = $env:KILLERMCP_TEST_REGISTER_CODEX
$previousClaudeRegister = $env:KILLERMCP_TEST_REGISTER_CLAUDE
$previousCodexHome = $env:CODEX_HOME
$previousClaudeHome = $env:CLAUDE_CONFIG_DIR
$previousCursorConfiguration = $env:KILLERMCP_TEST_CURSOR_CONFIG
$previousCopilotConfiguration = $env:KILLERMCP_TEST_COPILOT_CONFIG
$previousGeminiConfiguration = $env:KILLERMCP_TEST_GEMINI_CONFIG
$previousWindsurfConfiguration = $env:KILLERMCP_TEST_WINDSURF_CONFIG
$previousClaudeDesktopConfiguration = $env:KILLERMCP_TEST_CLAUDE_DESKTOP_CONFIG
$success = $false

try {
    New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
    New-Item -ItemType Directory -Path $claudeHome -Force | Out-Null
    foreach ($configuration in @($cursorConfiguration, $copilotConfiguration, $geminiConfiguration, $windsurfConfiguration, $claudeDesktopConfiguration)) {
        New-Item -ItemType Directory -Path (Split-Path $configuration) -Force | Out-Null
        @{
            preservedSetting = 'keep'
            mcpServers = @{ existing = @{ command = 'existing-command'; args = @('existing-argument') } }
        } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configuration
    }
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $env:KILLERMCP_TEST_REGISTER_CODEX = '1'
    $env:KILLERMCP_TEST_REGISTER_CLAUDE = '1'
    $env:CODEX_HOME = $codexHome
    $env:CLAUDE_CONFIG_DIR = $claudeHome
    $env:KILLERMCP_TEST_CURSOR_CONFIG = $cursorConfiguration
    $env:KILLERMCP_TEST_COPILOT_CONFIG = $copilotConfiguration
    $env:KILLERMCP_TEST_GEMINI_CONFIG = $geminiConfiguration
    $env:KILLERMCP_TEST_WINDSURF_CONFIG = $windsurfConfiguration
    $env:KILLERMCP_TEST_CLAUDE_DESKTOP_CONFIG = $claudeDesktopConfiguration

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
        $installedEntry.DisplayVersion -ne '0.1.2' -or
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
    $cursorRegistration = (Get-Content -LiteralPath $cursorConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($cursorRegistration.command -ne (Join-Path $installed 'node.exe') -or
        $cursorRegistration.args.Count -ne 1 -or
        $cursorRegistration.args[0] -ne (Join-Path $installed 'killermcp.mjs')) {
        throw 'Cursor did not retain the expected one-connection configuration.'
    }
    $copilotRegistration = (Get-Content -LiteralPath $copilotConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($copilotRegistration.command -ne (Join-Path $installed 'node.exe') -or
        $copilotRegistration.args.Count -ne 1 -or
        $copilotRegistration.args[0] -ne (Join-Path $installed 'killermcp.mjs')) {
        throw 'GitHub Copilot did not retain the expected one-connection configuration.'
    }
    $geminiRegistration = (Get-Content -LiteralPath $geminiConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($geminiRegistration.command -ne (Join-Path $installed 'node.exe') -or
        $geminiRegistration.args.Count -ne 1 -or
        $geminiRegistration.args[0] -ne (Join-Path $installed 'killermcp.mjs')) {
        throw 'Gemini CLI did not retain the expected one-connection configuration.'
    }
    $windsurfRegistration = (Get-Content -LiteralPath $windsurfConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($windsurfRegistration.command -ne (Join-Path $installed 'node.exe') -or
        $windsurfRegistration.args.Count -ne 1 -or
        $windsurfRegistration.args[0] -ne (Join-Path $installed 'killermcp.mjs')) {
        throw 'Windsurf did not retain the expected one-connection configuration.'
    }
    $claudeDesktopRegistration = (Get-Content -LiteralPath $claudeDesktopConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($claudeDesktopRegistration.command -ne (Join-Path $installed 'node.exe') -or
        $claudeDesktopRegistration.args.Count -ne 1 -or
        $claudeDesktopRegistration.args[0] -ne (Join-Path $installed 'killermcp.mjs')) {
        throw 'Claude Desktop did not retain the expected one-connection configuration.'
    }
    foreach ($configuration in @($cursorConfiguration, $copilotConfiguration, $geminiConfiguration, $windsurfConfiguration, $claudeDesktopConfiguration)) {
        $preservedConfiguration = Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json
        if ($preservedConfiguration.preservedSetting -ne 'keep' -or
            $preservedConfiguration.mcpServers.existing.command -ne 'existing-command' -or
            $preservedConfiguration.mcpServers.existing.args[0] -ne 'existing-argument') {
            throw "Install changed unrelated client configuration in $configuration."
        }
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

    $originalCursorSettings = Get-Content -LiteralPath $cursorConfiguration -Raw
    $differentCursorSettings = $originalCursorSettings | ConvertFrom-Json
    $differentCursorSettings.mcpServers.killermcp.command = 'C:\DifferentApp\node.exe'
    Set-Content -LiteralPath $cursorConfiguration -Value ($differentCursorSettings | ConvertTo-Json -Depth 30)
    $process = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    $preserved = (Get-Content -LiteralPath $cursorConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp.command
    if ($process.ExitCode -eq 0 -or $preserved -ne 'C:\DifferentApp\node.exe') {
        throw 'Install replaced a different Cursor connection.'
    }
    Set-Content -LiteralPath $cursorConfiguration -Value $originalCursorSettings

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
    $cursorRegistration = (Get-Content -LiteralPath $cursorConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($null -ne $cursorRegistration) { throw 'Uninstall left the KillerMCP Cursor connection in place.' }
    $copilotRegistration = (Get-Content -LiteralPath $copilotConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($null -ne $copilotRegistration) { throw 'Uninstall left the KillerMCP GitHub Copilot connection in place.' }
    $geminiRegistration = (Get-Content -LiteralPath $geminiConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($null -ne $geminiRegistration) { throw 'Uninstall left the KillerMCP Gemini CLI connection in place.' }
    $windsurfRegistration = (Get-Content -LiteralPath $windsurfConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($null -ne $windsurfRegistration) { throw 'Uninstall left the KillerMCP Windsurf connection in place.' }
    $claudeDesktopRegistration = (Get-Content -LiteralPath $claudeDesktopConfiguration -Raw | ConvertFrom-Json).mcpServers.killermcp
    if ($null -ne $claudeDesktopRegistration) { throw 'Uninstall left the KillerMCP Claude Desktop connection in place.' }
    foreach ($configuration in @($cursorConfiguration, $copilotConfiguration, $geminiConfiguration, $windsurfConfiguration, $claudeDesktopConfiguration)) {
        $preservedConfiguration = Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json
        if ($preservedConfiguration.preservedSetting -ne 'keep' -or
            $preservedConfiguration.mcpServers.existing.command -ne 'existing-command' -or
            $preservedConfiguration.mcpServers.existing.args[0] -ne 'existing-argument') {
            throw "Uninstall changed unrelated client configuration in $configuration."
        }
    }
    $success = $true
    Write-Output 'KillerMCP isolated install, reinstall, Installed Apps entry, seven client registrations, MCP calls, and uninstall passed.'
}
finally {
    $env:KILLERMCP_TEST_INSTALL_ROOT = $previousInstallRoot
    $env:KILLERMCP_TEST_REGISTER_CODEX = $previousRegister
    $env:KILLERMCP_TEST_REGISTER_CLAUDE = $previousClaudeRegister
    $env:CODEX_HOME = $previousCodexHome
    $env:CLAUDE_CONFIG_DIR = $previousClaudeHome
    $env:KILLERMCP_TEST_CURSOR_CONFIG = $previousCursorConfiguration
    $env:KILLERMCP_TEST_COPILOT_CONFIG = $previousCopilotConfiguration
    $env:KILLERMCP_TEST_GEMINI_CONFIG = $previousGeminiConfiguration
    $env:KILLERMCP_TEST_WINDSURF_CONFIG = $previousWindsurfConfiguration
    $env:KILLERMCP_TEST_CLAUDE_DESKTOP_CONFIG = $previousClaudeDesktopConfiguration
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
