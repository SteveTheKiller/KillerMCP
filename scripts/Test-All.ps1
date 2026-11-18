param([string]$ArtifactRoot = '', [string]$LabMachine = '')

$ErrorActionPreference = 'Stop'
if ($env:CI -ne 'true' -and (!$LabMachine -or $LabMachine -ine $env:COMPUTERNAME)) {
    throw 'Executable app checks must run on the leased disposable VM with its explicit -LabMachine name, or in disposable CI.'
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ArtifactRoot = & (Join-Path $PSScriptRoot 'Get-ArtifactRoot.ps1') -ArtifactRoot $ArtifactRoot
& (Join-Path $PSScriptRoot 'Test-WorkflowGate.ps1')
if ($env:CI -ne 'true') {
    $dirty = @(git -C $root status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Could not verify the source working tree.' }
    if ($dirty.Count) { throw 'Commit and push the reviewed source before release validation so GitHub Checks can verify the exact source being tested.' }
    & (Join-Path $PSScriptRoot 'Test-WorkflowReadiness.ps1')
}
& (Join-Path $PSScriptRoot 'Test-PackagingPaths.ps1') -ScratchRoot $ArtifactRoot
& (Join-Path $PSScriptRoot 'Test-PluginVersion.ps1')
& (Join-Path $PSScriptRoot 'Test-ReleasePreflight.ps1') -ScratchRoot $ArtifactRoot

dotnet build (Join-Path $root 'KillerMCP.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP solution build failed.' }

& (Join-Path $PSScriptRoot 'Test-Workflows.ps1')

dotnet run --project (Join-Path $root 'tests\KillerMCP.Clients.Checks\KillerMCP.Clients.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP client checks failed.' }

dotnet run --project (Join-Path $root 'tests\KillerMCP.Runtime.Checks\KillerMCP.Runtime.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP runtime checks failed.' }

dotnet run --project (Join-Path $root 'tests\KillerMCP.Runtime.Checks\KillerMCP.Runtime.Checks.csproj') -c Release --no-build -- --review-adapter-095
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP 0.9.5 adapter checks failed.' }

foreach ($review in @('--review-child-input', '--review-positional-input', '--review-download-paths', '--review-data-moves', '--review-repair-trust', '--review-uac-consent', '--review-async', '--review-adapter-safety', '--review-notes-bridge', '--review-notes-workflows', '--review-integration-pdf', '--review-network-path', '--review-release-gaps', '--review-update-flavor', '--review-open-questions')) {
    $previousPdfRoot = [Environment]::GetEnvironmentVariable('KILLERMCP_PDF_CHECK_ROOT')
    $previousAdapterRoot = [Environment]::GetEnvironmentVariable('KILLERMCP_ADAPTER_CHECK_ROOT')
    $previousTemp = [Environment]::GetEnvironmentVariable('TEMP')
    $previousTmp = [Environment]::GetEnvironmentVariable('TMP')
    try {
        $reviewTemp = Join-Path $ArtifactRoot ($review.TrimStart('-') + '-' + [Guid]::NewGuid().ToString('N'))
        [IO.Directory]::CreateDirectory($reviewTemp) | Out-Null
        [Environment]::SetEnvironmentVariable('TEMP', $reviewTemp)
        [Environment]::SetEnvironmentVariable('TMP', $reviewTemp)
        if ($review -eq '--review-adapter-safety') {
            [Environment]::SetEnvironmentVariable('KILLERMCP_ADAPTER_CHECK_ROOT', (Join-Path $reviewTemp 'adapter-review'))
        }
        if ($review -eq '--review-integration-pdf') {
            $pdfRoot = Join-Path $ArtifactRoot ('integration-pdf-review-' + [Guid]::NewGuid().ToString('N'))
            [IO.Directory]::CreateDirectory($pdfRoot) | Out-Null
            [Environment]::SetEnvironmentVariable('KILLERMCP_PDF_CHECK_ROOT', $pdfRoot)
        }
        dotnet run --project (Join-Path $root 'tests\KillerMCP.Runtime.Checks\KillerMCP.Runtime.Checks.csproj') -c Release --no-build -- $review
        if ($LASTEXITCODE -ne 0) { throw "KillerMCP review checks failed: $review" }
    }
    finally {
        [Environment]::SetEnvironmentVariable('KILLERMCP_PDF_CHECK_ROOT', $previousPdfRoot)
        [Environment]::SetEnvironmentVariable('KILLERMCP_ADAPTER_CHECK_ROOT', $previousAdapterRoot)
        [Environment]::SetEnvironmentVariable('TEMP', $previousTemp)
        [Environment]::SetEnvironmentVariable('TMP', $previousTmp)
    }
}

dotnet run --project (Join-Path $root 'tests\KillerMCP.Setup.Checks\KillerMCP.Setup.Checks.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP setup checks failed.' }

node (Join-Path $PSScriptRoot 'Test-NativeHost.mjs')
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP native host checks failed.' }

node (Join-Path $root 'tests\BrowserCompanionChecks.mjs')
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP browser companion checks failed.' }

node (Join-Path $PSScriptRoot 'Test-ReviewDrivers.mjs') --root (Join-Path $ArtifactRoot 'driver-stub-checks')
if ($LASTEXITCODE -ne 0) { throw 'KillerMCP pure Node review-driver checks failed.' }

& (Join-Path $PSScriptRoot 'Test-NativePackage.ps1') -ArtifactRoot $ArtifactRoot

if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    $configurator = Join-Path $root 'Configuration\bin\Release\net10.0\KillerMCP.Configure.exe'
    foreach ($nested in @($false, $true)) {
        $lifetimeScratch = Join-Path $ArtifactRoot ('configurator-lifetime-' + [Guid]::NewGuid().ToString('N'))
        $lifetimeArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'Test-ConfiguratorLifetime.ps1'), '-ConfiguratorPath', $configurator, '-ScratchPath', $lifetimeScratch, '-LabMachine', $env:COMPUTERNAME)
        if ($nested) { $lifetimeArguments += '-NestedJob' }
        powershell @lifetimeArguments
        if ($LASTEXITCODE -ne 0) { throw 'KillerMCP configurator lifetime checks failed.' }
    }
    powershell -NoProfile -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-ApprovalShadow.ps1') -AssemblyPath (Join-Path $root 'Shell\bin\Release\net48\KillerMCP.Notify.exe') -OutputDirectory (Join-Path $ArtifactRoot ('approval-shadow-' + [Guid]::NewGuid().ToString('N'))) -LabMachine $env:COMPUTERNAME
    if ($LASTEXITCODE -ne 0) { throw 'KillerMCP approval shadow check failed.' }
    & (Join-Path $PSScriptRoot 'Test-InstallerReview.ps1') -ArtifactRoot $ArtifactRoot -LabMachine $LabMachine
    & (Join-Path $PSScriptRoot 'Test-WindowsInstaller.ps1') -ArtifactRoot $ArtifactRoot
    $setupPath = Join-Path $ArtifactRoot 'installer\KillerMCP-Setup.exe'
    & (Join-Path $PSScriptRoot 'Test-UpdatePreferences.ps1') -AssemblyPath $setupPath -ScratchPath (Join-Path $ArtifactRoot 'update-preferences-checks')
    & (Join-Path $PSScriptRoot 'Test-UpdateBootstrapOff.ps1') -SetupPath $setupPath -ScratchPath (Join-Path $ArtifactRoot 'update-bootstrap-off-checks')
    & (Join-Path $PSScriptRoot 'Test-InstallerContainment.ps1') -Installer (Join-Path $ArtifactRoot 'installer\KillerMCP-Setup.exe')
    & (Join-Path $PSScriptRoot 'Test-ClientJobUpdate.ps1') -Installer (Join-Path $ArtifactRoot 'installer\KillerMCP-Setup.exe')
    & (Join-Path $PSScriptRoot 'Test-ClientJobNotice.ps1')
    & (Join-Path $PSScriptRoot 'Test-NoticeRetry.ps1')
    & (Join-Path $PSScriptRoot 'Test-NotifyFailure.ps1') -Installer (Join-Path $ArtifactRoot 'installer\KillerMCP-Setup.exe')
}

# Manual desktop checks: Test-StandaloneWindows.ps1, Test-StandaloneFocus.ps1 and
# Test-InstallerUpdateChoice.ps1 open windows. Run those on the disposable lab desktop.
Write-Output 'All KillerMCP checks passed.'
