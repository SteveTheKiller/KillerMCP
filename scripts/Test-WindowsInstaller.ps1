param([string]$Installer = '')

$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { throw 'The KillerMCP Windows installer test requires Windows.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Write-Utf8NoBom([string]$Path, [string]$Value) {
    [IO.File]::WriteAllText($Path, $Value, $utf8NoBom)
}

if (-not $Installer) {
    & (Join-Path $PSScriptRoot 'Build-WindowsInstaller.ps1')
}
$setup = if ($Installer) { [IO.Path]::GetFullPath($Installer) } else { Join-Path $root 'artifacts\installer\KillerMCP-Setup.exe' }
dotnet build (Join-Path $root 'tests\KillerMCP.Clients.Checks\KillerMCP.Clients.Checks.csproj') -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'The fake Codex client build failed.' }
$fakeCodex = Join-Path $root 'tests\KillerMCP.Clients.Checks\bin\Release\net10.0\KillerMCP.Clients.Checks.exe'
if (-not (Test-Path -LiteralPath $setup -PathType Leaf) -or -not (Test-Path -LiteralPath $fakeCodex -PathType Leaf)) { throw 'Installer test prerequisites are missing.' }

$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-native-installer-' + [guid]::NewGuid().ToString('N'))
$installed = Join-Path $scratch 'Installed'
$codexState = Join-Path $scratch 'codex-state.json'
$previous = @{}
$names = @('CLAUDE', 'CURSOR', 'COPILOT', 'GEMINI', 'WINDSURF', 'CLAUDE_DESKTOP')
$variables = @('KILLERMCP_TEST_INSTALL_ROOT', 'KILLERMCP_TEST_RUNTIME', 'KILLERMCP_FAKE_CODEX_STATE', 'CODEX_CLI_PATH') + ($names | ForEach-Object { "KILLERMCP_TEST_${_}_CONFIG" })
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$success = $false

try {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $env:KILLERMCP_FAKE_CODEX_STATE = $codexState
    $env:CODEX_CLI_PATH = $fakeCodex
    foreach ($name in $names) {
        $path = Join-Path $scratch "$name\settings.json"
        New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
        Write-Utf8NoBom $path '{"preservedSetting":"keep"}'
        [Environment]::SetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG", $path)
    }

    $env:KILLERMCP_TEST_RUNTIME = '0'
    $missingRuntime = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    if ($missingRuntime.ExitCode -ne 10 -or (Test-Path -LiteralPath $installed)) { throw 'Silent setup did not refuse installation when .NET 10 was unavailable.' }

    $env:KILLERMCP_TEST_RUNTIME = '1'
    New-Item -ItemType Directory -Path $installed -Force | Out-Null
    $legacyFiles = @{
        'node.exe' = 'legacy node runtime'
        'killermcp.mjs' = 'legacy MCP host'
        'killertools.mjs' = 'legacy KillerTools bundle'
    }
    $legacyManifest = @{ files = @{} }
    foreach ($item in $legacyFiles.GetEnumerator()) {
        $path = Join-Path $installed $item.Key
        Write-Utf8NoBom $path $item.Value
        $legacyManifest.files[$item.Key] = @{ bytes = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    }
    Write-Utf8NoBom (Join-Path $installed 'manifest.json') ($legacyManifest | ConvertTo-Json -Depth 5)
    $setupCopy = "$installed-Setup.exe"
    Copy-Item -LiteralPath $setup -Destination $setupCopy
    $uninstallKey = 'HKCU:\Software\KillerMCP\InstallerTests\' + (Split-Path $scratch -Leaf)
    $legacyNode = Join-Path $installed 'node.exe'
    $legacyScript = Join-Path $installed 'killermcp.mjs'
    Write-Utf8NoBom $codexState (@{ transport = @{ type = 'stdio'; command = $legacyNode; args = @($legacyScript) } } | ConvertTo-Json -Depth 5)
    foreach ($name in $names) {
        $path = [Environment]::GetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG")
        $configuration = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $registration = @{ command = $legacyNode; args = @($legacyScript) }
        if ($name -eq 'CLAUDE') { $registration.type = 'stdio' }
        $configuration | Add-Member -NotePropertyName mcpServers -NotePropertyValue @{ killermcp = $registration }
        Write-Utf8NoBom $path ($configuration | ConvertTo-Json -Depth 10)
    }
    foreach ($pass in 1..2) {
        $install = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($install.ExitCode -ne 0) { throw "Installer pass $pass failed with exit code $($install.ExitCode)." }
        node (Join-Path $PSScriptRoot 'Test-NativePackage.mjs') $installed
        if ($LASTEXITCODE -ne 0) { throw "Installed runtime check failed on pass $pass." }
        if (Test-Path -LiteralPath $legacyNode) { throw 'Installer did not remove the legacy Node runtime.' }
        if ($pass -eq 1) {
            New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '0.1.0' -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $uninstallKey -Name SetupSha256 -Value 'STALE' -PropertyType String -Force | Out-Null
            Write-Utf8NoBom $codexState (@{ transport = @{ type = 'stdio'; command = $legacyNode; args = @($legacyScript) } } | ConvertTo-Json -Depth 5)
            foreach ($name in $names) {
                $path = [Environment]::GetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG")
                $configuration = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                $configuration.mcpServers.killermcp.command = $legacyNode
                $configuration.mcpServers.killermcp.args = @($legacyScript)
                Write-Utf8NoBom $path ($configuration | ConvertTo-Json -Depth 10)
            }
        }
    }

    $runningStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $installed 'KillerMCP.exe'))
    $runningStart.UseShellExecute = $false
    $runningStart.CreateNoWindow = $true
    $runningStart.RedirectStandardInput = $true
    $runningStart.RedirectStandardOutput = $true
    $runningStart.RedirectStandardError = $true
    $runningHost = [Diagnostics.Process]::new()
    $runningHost.StartInfo = $runningStart
    try {
        if (-not $runningHost.Start()) { throw 'The installed MCP host did not start for the active-process reinstall test.' }
        Start-Sleep -Milliseconds 500
        if ($runningHost.HasExited) { throw 'The installed MCP host exited before the active-process reinstall test.' }
        $reinstall = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($reinstall.ExitCode -ne 0 -or -not $runningHost.HasExited) { throw 'Reinstall did not close and replace the active MCP host.' }
    }
    finally {
        if (-not $runningHost.HasExited) { $runningHost.Kill() }
        $runningHost.Dispose()
    }

    $entry = Get-ItemProperty -LiteralPath $uninstallKey
    if (-not (Test-Path -LiteralPath $setupCopy -PathType Leaf) -or $entry.DisplayVersion -ne '0.3.1' -or $entry.InstallLocation -ne $installed) { throw 'Installed Apps registration is incorrect.' }
    $codex = Get-Content -LiteralPath $codexState -Raw | ConvertFrom-Json
    if ($codex.transport.command -ne (Join-Path $installed 'KillerMCP.exe') -or $codex.transport.args.Count -ne 0) { throw 'Codex registration is incorrect.' }
    foreach ($name in $names) {
        $path = [Environment]::GetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG")
        $configuration = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if ($configuration.preservedSetting -ne 'keep' -or $configuration.mcpServers.killermcp.command -ne (Join-Path $installed 'KillerMCP.exe') -or $configuration.mcpServers.killermcp.args.Count -ne 0) { throw "$name registration is incorrect." }
    }

    $sentinel = Join-Path $installed 'user-file.txt'
    Set-Content -LiteralPath $sentinel -Value 'keep'
    $blocked = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    if ($blocked.ExitCode -eq 0 -or -not (Test-Path -LiteralPath $sentinel)) { throw 'Reinstall did not preserve an installation folder with an extra file.' }
    Remove-Item -LiteralPath $sentinel

    $runtime = Join-Path $installed 'KillerMCP.exe'
    $runtimeBytes = [IO.File]::ReadAllBytes($runtime)
    try {
        [IO.File]::AppendAllText($runtime, 'modified')
        $blocked = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($blocked.ExitCode -eq 0) { throw 'Reinstall replaced a modified runtime file.' }
    }
    finally { [IO.File]::WriteAllBytes($runtime, $runtimeBytes) }

    $setupBytes = [IO.File]::ReadAllBytes($setupCopy)
    try {
        [IO.File]::AppendAllText($setupCopy, 'modified')
        $blocked = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($blocked.ExitCode -eq 0) { throw 'Reinstall replaced a modified setup file.' }
    }
    finally { [IO.File]::WriteAllBytes($setupCopy, $setupBytes) }

    $cursorPath = [Environment]::GetEnvironmentVariable('KILLERMCP_TEST_CURSOR_CONFIG')
    $cursorOriginal = Get-Content -LiteralPath $cursorPath -Raw
    try {
        $cursorDifferent = $cursorOriginal | ConvertFrom-Json
        $cursorDifferent.mcpServers.killermcp.command = 'C:\DifferentApp\KillerMCP.exe'
        Write-Utf8NoBom $cursorPath ($cursorDifferent | ConvertTo-Json -Depth 20)
        $blocked = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        $preserved = (Get-Content -LiteralPath $cursorPath -Raw | ConvertFrom-Json).mcpServers.killermcp.command
        if ($blocked.ExitCode -eq 0 -or $preserved -ne 'C:\DifferentApp\KillerMCP.exe') { throw 'Reinstall replaced a different Cursor connection.' }
    }
    finally { Write-Utf8NoBom $cursorPath $cursorOriginal }

    Set-Content -LiteralPath $sentinel -Value 'keep'
    $blocked = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($blocked.ExitCode -eq 0 -or -not (Test-Path -LiteralPath $sentinel)) { throw 'Uninstall did not preserve an installation folder with an extra file.' }
    Remove-Item -LiteralPath $sentinel

    $uninstall = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $setupCopy) -or (Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $codexState)) { throw 'KillerMCP uninstall did not remove its files and registrations.' }
    foreach ($name in $names) {
        $configuration = Get-Content -LiteralPath ([Environment]::GetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG")) -Raw | ConvertFrom-Json
        if ($configuration.preservedSetting -ne 'keep' -or $null -ne $configuration.mcpServers.killermcp) { throw "$name was not safely disconnected." }
    }
    $success = $true
    Write-Output 'KillerMCP runtime prerequisite, legacy migration, install, reinstall, seven client registrations, MCP calls, and uninstall passed.'
}
finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    if ($success -and (Test-Path -LiteralPath $scratch)) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    elseif (Test-Path -LiteralPath $scratch) { Write-Output "Installer test files retained at $scratch" }
}
