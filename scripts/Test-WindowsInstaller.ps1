param([string]$Installer = '', [string]$PreviousSignedSetup = '', [string]$PreviousSignedInstall = '')

$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { throw 'The KillerMCP Windows installer test requires Windows.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$hostProject = Get-Content -LiteralPath (Join-Path $root 'KillerMCP.csproj') -Raw
$releaseVersion = [string]$hostProject.Project.PropertyGroup.Version
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
$variables = @('KILLERMCP_TEST_INSTALL_ROOT', 'KILLERMCP_TEST_RUNTIME', 'KILLERMCP_FAKE_CODEX_STATE', 'KILLERMCP_TOOL_CALL_LOG', 'CODEX_CLI_PATH', 'KILLERMCP_UPDATE_CACHE', 'KILLERMCP_UPDATE_API', 'KILLERMCP_NOTIFIER', 'KILLERMCP_TEST_UPDATE_API', 'KILLERMCP_FAKE_CODEX_HANG', 'KILLERMCP_TEST_CONFIGURE_TIMEOUT_MS') + ($names | ForEach-Object { "KILLERMCP_TEST_${_}_CONFIG" })
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$success = $false

try {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $env:KILLERMCP_TEST_INSTALL_ROOT = $installed
    $env:KILLERMCP_TOOL_CALL_LOG = Join-Path $scratch 'tool-calls.jsonl'
    $env:KILLERMCP_FAKE_CODEX_STATE = $codexState
    $env:CODEX_CLI_PATH = $fakeCodex
    # Installed hosts started by this test must not reach the real release API, cache or update window.
    $env:KILLERMCP_UPDATE_CACHE = Join-Path $scratch 'update-status.json'
    $env:KILLERMCP_UPDATE_API = 'https://127.0.0.1:9/no-release'
    $env:KILLERMCP_NOTIFIER = Join-Path $scratch 'no-notifier.exe'
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
    $runningHosts = @()
    try {
        foreach ($connection in 1..3) {
            $runningHost = [Diagnostics.Process]::new()
            $runningHost.StartInfo = $runningStart
            if (-not $runningHost.Start()) { throw 'The installed MCP host did not start for the active-process reinstall test.' }
            $runningHosts += $runningHost
        }
        Start-Sleep -Milliseconds 500
        if (@($runningHosts | Where-Object HasExited).Count -ne 0) { throw 'An installed MCP host exited before the active-process reinstall test.' }
        # Updates can inherit the installation folder as their working directory.
        $reinstall = Start-Process -FilePath $setup -ArgumentList '/silent' -WorkingDirectory $installed -Wait -PassThru -WindowStyle Hidden
        if ($reinstall.ExitCode -ne 0 -or @($runningHosts | Where-Object { -not $_.HasExited }).Count -ne 0) { throw "Reinstall did not close every active MCP host and replace its working directory (exit $($reinstall.ExitCode))." }
    }
    finally {
        foreach ($runningHost in $runningHosts) {
            if (-not $runningHost.HasExited) { $runningHost.Kill() }
            $runningHost.Dispose()
        }
    }

    $entry = Get-ItemProperty -LiteralPath $uninstallKey
    if (-not (Test-Path -LiteralPath $setupCopy -PathType Leaf) -or $entry.DisplayVersion -ne $releaseVersion -or $entry.InstallLocation -ne $installed) { throw 'Installed Apps registration is incorrect.' }
    $updateRunKey = 'HKCU:\Software\KillerMCP\InstallerTests\' + (Split-Path $scratch -Leaf) + '\Run'
    $updateCommand = (Get-ItemProperty -LiteralPath $updateRunKey).'KillerMCP Update Check'
    if ($updateCommand -ne ('"' + $setupCopy + '" /check-update')) { throw 'Independent update check was not registered.' }
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

    # A setup copy that was deleted or quarantined must not block reinstalling, but an entry for another folder is not adopted.
    Remove-Item -LiteralPath $setupCopy
    Set-ItemProperty -LiteralPath $uninstallKey -Name InstallLocation -Value 'C:\Elsewhere\KillerMCP'
    try {
        $blocked = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($blocked.ExitCode -eq 0) { throw 'Setup adopted an uninstall entry for a different folder.' }
    }
    finally { Set-ItemProperty -LiteralPath $uninstallKey -Name InstallLocation -Value $installed }
    $recovered = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    if ($recovered.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $setupCopy -PathType Leaf) -or (Get-ItemProperty -LiteralPath $uninstallKey).SetupSha256 -ne (Get-FileHash -LiteralPath $setupCopy -Algorithm SHA256).Hash) { throw "Setup could not reinstall after its setup copy went missing (exit $($recovered.ExitCode))." }

    # A backup that an earlier upgrade could not remove is cleared by the next install.
    $leftoverBackup = "$installed.previous-test"
    Copy-Item -LiteralPath $installed -Destination $leftoverBackup -Recurse
    $install = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
    if ($install.ExitCode -ne 0 -or (Test-Path -LiteralPath $leftoverBackup)) { throw "Install did not remove a backup an earlier upgrade left behind (exit $($install.ExitCode))." }

    # A preferences file that holds valid JSON but not an object falls back to checking for updates.
    $preferences = Join-Path $scratch 'update-preferences.json'
    Write-Utf8NoBom $preferences '[]'
    $env:KILLERMCP_TEST_UPDATE_API = 'https://127.0.0.1:9/no-release'
    $check = Start-Process -FilePath $setup -ArgumentList '/check-update', '/silent' -Wait -PassThru -WindowStyle Hidden
    Remove-Item -LiteralPath $preferences
    if ($check.ExitCode -ne 0) { throw "The update check failed on a preferences file that is not an object (exit $($check.ExitCode))." }

    # A client configuration that stops responding is ended at the time limit, Codex with it, and the
    # previous installation stays in place.
    $env:KILLERMCP_FAKE_CODEX_HANG = '1'
    $env:KILLERMCP_TEST_CONFIGURE_TIMEOUT_MS = '3000'
    $hungStarted = [Diagnostics.Stopwatch]::StartNew()
    try { $blocked = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden }
    finally { $env:KILLERMCP_FAKE_CODEX_HANG = $null; $env:KILLERMCP_TEST_CONFIGURE_TIMEOUT_MS = $null }
    $hungSeconds = $hungStarted.Elapsed.TotalSeconds
    $leftCodex = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $fakeCodex })
    $leftCodex | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    if ($blocked.ExitCode -eq 0 -or $hungSeconds -gt 20 -or $leftCodex.Count -gt 0) { throw "A hung client configuration was not ended at the time limit (exit $($blocked.ExitCode), $([int]$hungSeconds) s, $($leftCodex.Count) Codex left)." }
    node (Join-Path $PSScriptRoot 'Test-NativePackage.mjs') $installed
    if ($LASTEXITCODE -ne 0) { throw 'A timed-out install did not keep the previous installation.' }

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

    # Agent clients keep the host running, so uninstall has to close it rather than fail partway through deleting.
    $runningHost = [Diagnostics.Process]::new()
    $runningHost.StartInfo = $runningStart
    try {
        if (-not $runningHost.Start()) { throw 'The installed MCP host did not start for the active-process uninstall test.' }
        Start-Sleep -Milliseconds 500
        if ($runningHost.HasExited) { throw 'The installed MCP host exited before the active-process uninstall test.' }
        $uninstall = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
        if (-not $runningHost.HasExited) { throw 'Uninstall did not close the active MCP host.' }
    }
    finally {
        if (-not $runningHost.HasExited) { $runningHost.Kill() }
        $runningHost.Dispose()
    }
    if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $setupCopy) -or (Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $codexState)) { throw "KillerMCP uninstall did not remove its files and registrations (exit $($uninstall.ExitCode))." }
    if (Get-ChildItem -LiteralPath $scratch -Directory -Filter 'Installed.*') { throw 'Uninstall left a staging, backup or removal folder behind.' }
    if ((Get-ItemProperty -LiteralPath $updateRunKey -ErrorAction SilentlyContinue).'KillerMCP Update Check') { throw 'KillerMCP uninstall kept its update check.' }
    foreach ($name in $names) {
        $configuration = Get-Content -LiteralPath ([Environment]::GetEnvironmentVariable("KILLERMCP_TEST_${name}_CONFIG")) -Raw | ConvertFrom-Json
        if ($configuration.preservedSetting -ne 'keep' -or $null -ne $configuration.mcpServers.killermcp) { throw "$name was not safely disconnected." }
    }

    # Earlier versions could stop uninstalling partway and leave a folder with only some of its files.
    # Setup has to remove such a folder, and reinstall over one, without touching anything it did not install.
    foreach ($recovery in 'uninstall', 'install') {
        $install = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($install.ExitCode -ne 0) { throw "Install before the stranded-folder $recovery check failed with exit code $($install.ExitCode)." }
        $listed = @((Get-Content -LiteralPath (Join-Path $installed 'manifest.json') -Raw | ConvertFrom-Json).files.PSObject.Properties.Name | Sort-Object)
        Remove-Item -LiteralPath (Join-Path $installed ($listed[0] -replace '/', '\'))
        Remove-Item -LiteralPath (Join-Path $installed ($listed[1] -replace '/', '\'))
        $stranded = Start-Process -FilePath $setup -ArgumentList $(if ($recovery -eq 'uninstall') { @('/silent', '/uninstall') } else { @('/silent') }) -Wait -PassThru -WindowStyle Hidden
        if ($stranded.ExitCode -ne 0) { throw "Setup could not $recovery over a partly removed installation (exit $($stranded.ExitCode))." }
        if ($recovery -eq 'install') {
            node (Join-Path $PSScriptRoot 'Test-NativePackage.mjs') $installed
            if ($LASTEXITCODE -ne 0) { throw 'Reinstall over a partly removed installation left a broken runtime.' }
        }
        elseif (Test-Path -LiteralPath $installed) { throw 'Uninstall left a partly removed installation folder.' }
    }
    Set-Content -LiteralPath $sentinel -Value 'keep'
    $blocked = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($blocked.ExitCode -eq 0 -or -not (Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed a file it did not install from a recovered installation.' }
    Remove-Item -LiteralPath $sentinel
    $uninstall = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
    if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath $installed)) { throw 'Final uninstall after the stranded-folder checks failed.' }

    if ($PreviousSignedSetup -and $PreviousSignedInstall) {
        if ((Get-AuthenticodeSignature -LiteralPath $PreviousSignedSetup).Status -ne 'Valid') { throw 'The previous setup fixture is not signed and trusted.' }
        New-Item -ItemType Directory -Path $installed -Force | Out-Null
        Copy-Item -Path (Join-Path $PreviousSignedInstall '*') -Destination $installed -Recurse
        Copy-Item -LiteralPath $PreviousSignedSetup -Destination $setupCopy
        New-Item -Path $uninstallKey -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '0.3.1' -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installed -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('"' + $setupCopy + '" /uninstall') -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $uninstallKey -Name SetupSha256 -Value 'STALE' -PropertyType String -Force | Out-Null
        $previousSetupBytes = [IO.File]::ReadAllBytes($setupCopy)
        try {
            [IO.File]::AppendAllText($setupCopy, 'modified')
            $blocked = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
            if ($blocked.ExitCode -eq 0) { throw 'Upgrade accepted a modified signed previous setup.' }
        }
        finally { [IO.File]::WriteAllBytes($setupCopy, $previousSetupBytes) }
        $upgrade = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru -WindowStyle Hidden
        if ($upgrade.ExitCode -ne 0 -or (Get-ItemProperty -LiteralPath $uninstallKey).DisplayVersion -ne $releaseVersion) { throw 'Upgrade from a signed previous setup with a stale uninstall entry failed.' }
        $uninstall = Start-Process -FilePath $setup -ArgumentList '/silent', '/uninstall' -Wait -PassThru -WindowStyle Hidden
        if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath $installed) -or (Test-Path -LiteralPath $setupCopy)) { throw 'Previous setup upgrade fixture did not uninstall cleanly.' }
        Write-Output 'Signed previous setup and stale uninstall entry upgrade passed.'
    }
    $success = $true
    Write-Output 'KillerMCP runtime prerequisite, legacy migration, install, reinstall, seven client registrations, MCP calls, and uninstall passed.'
}
finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    if ($success -and (Test-Path -LiteralPath $scratch)) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    elseif (Test-Path -LiteralPath $scratch) { Write-Output "Installer test files retained at $scratch" }
}
