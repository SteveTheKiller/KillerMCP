# KillerMCP native release workflow.
# Builds, signs with Certum SimplySign, verifies, tests, and optionally publishes.

[CmdletBinding()]
param(
    [string]$CertThumbprint = '',
    [string]$CertName = 'Open Source Developer Stephen Riley',
    [switch]$SkipSign,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][scriptblock]$Command,
        [Parameter(Mandatory)][string]$Failure
    )
    & $Command
    if ($LASTEXITCODE -ne 0) { Fail $Failure }
}

if ($Publish -and $SkipSign) {
    Fail 'An unsigned KillerMCP installer cannot be published.'
}

[xml]$project = Get-Content -LiteralPath 'src\KillerMCP\KillerMCP.csproj' -Raw
$Version = [string]$project.Project.PropertyGroup.Version
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail 'KillerMCP.csproj does not contain an x.y.z version.' }

$Tag = "v$Version"
$installer = Join-Path $PSScriptRoot 'artifacts\installer\KillerMCP-Setup.exe'
$sumsFile = Join-Path $PSScriptRoot 'artifacts\installer\SHA256SUMS.txt'
$changelogFile = Join-Path $PSScriptRoot 'CHANGELOG.md'
$engineProject = Join-Path $PSScriptRoot 'dependencies\KillerTools\src\KillerTools.Engine\KillerTools.Engine.csproj'

Step "Checking KillerMCP $Version release inputs"
if ((git branch --show-current).Trim() -ne 'main') { Fail 'KillerMCP releases must run from main.' }
if (-not (Test-Path -LiteralPath $engineProject -PathType Leaf)) {
    Fail 'The pinned KillerTools submodule is missing. Run git submodule update --init.'
}
$submoduleState = @(git submodule status --recursive)
if ($LASTEXITCODE -ne 0 -or $submoduleState.Count -eq 0 -or @($submoduleState | Where-Object { $_ -match '^[+-]' }).Count) {
    Fail 'The KillerTools submodule is missing or differs from the pinned commit.'
}
$dashMatches = @(git grep -n -I -P '[\x{2013}\x{2014}]' -- . 2>$null)
if ($dashMatches.Count) {
    Write-Host ($dashMatches -join "`n")
    Fail 'Prohibited Unicode dash characters were found.'
}

$changelog = Get-Content -LiteralPath $changelogFile -Raw
if ($Publish) {
    if ($changelog -match "(?m)^## \[$([regex]::Escape($Version))\] - Unreleased$") {
        Fail "CHANGELOG.md section [$Version] is still marked Unreleased."
    }
    if ($changelog -notmatch "(?m)^## \[$([regex]::Escape($Version))\] - \d{4}-\d{2}-\d{2}$") {
        Fail "CHANGELOG.md has no dated [$Version] section."
    }
    $dirty = @(git status --porcelain)
    if ($dirty.Count) { Fail "The working tree is not clean:`n$($dirty -join "`n")" }
    git fetch origin main --quiet
    if ($LASTEXITCODE -ne 0) { Fail 'Could not fetch origin/main.' }
    if ((git rev-parse HEAD).Trim() -ne (git rev-parse origin/main).Trim()) {
        Fail 'Local main and origin/main differ. Push the reviewed source first.'
    }
    if (git tag --list $Tag) { Fail "Tag $Tag already exists locally." }
    if (git ls-remote --tags origin $Tag) { Fail "Tag $Tag already exists on origin." }
}

Step 'Building and testing the native host'
Invoke-Checked { dotnet build KillerMCP.slnx -c Release } 'KillerMCP solution build failed.'
Invoke-Checked { dotnet list src\KillerMCP\KillerMCP.csproj package --vulnerable --include-transitive } 'The dependency vulnerability check failed.'
Invoke-Checked { dotnet run --project tests\KillerMCP.Clients.Checks\KillerMCP.Clients.Checks.csproj -c Release --no-build } 'Client registration checks failed.'
Invoke-Checked { dotnet run --project tests\KillerMCP.Runtime.Checks\KillerMCP.Runtime.Checks.csproj -c Release --no-build } 'Runtime checks failed.'
Invoke-Checked { node scripts\Test-NativeHost.mjs } 'Native host checks failed.'
Invoke-Checked { powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-NativePackage.ps1 } 'Native package checks failed.'

Step 'Building KillerMCP Setup'
Invoke-Checked { powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-WindowsInstaller.ps1 } 'KillerMCP installer build failed.'
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { Fail "Installer was not produced: $installer" }

if ($SkipSign) {
    Write-Host 'WARNING: The installer is unsigned and cannot be published.' -ForegroundColor Red
} else {
    Step 'Signing KillerMCP Setup'
    if (-not (Get-Process -Name 'SimplySignDesktop' -ErrorAction SilentlyContinue)) {
        Fail 'SimplySign Desktop is not running. Start it, sign in, and run the release again.'
    }
    $signtool = (Get-Command signtool -ErrorAction SilentlyContinue).Source
    if (-not $signtool) {
        $kitBase = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        if (Test-Path -LiteralPath $kitBase) {
            $signtool = Get-ChildItem -LiteralPath $kitBase -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if (-not $signtool) { Fail 'signtool.exe was not found. Install the Windows SDK.' }
    $certArgs = if ($CertThumbprint) { @('/sha1', $CertThumbprint) } else { @('/n', $CertName) }
    $signed = $false
    foreach ($timestampServer in @('http://timestamp.digicert.com', 'http://timestamp.sectigo.com', 'http://ts.ssl.com')) {
        & $signtool sign /fd sha256 /tr $timestampServer /td sha256 @certArgs /d 'KillerMCP' /du 'https://github.com/SteveTheKiller/KillerMCP' /v $installer
        if ($LASTEXITCODE -eq 0) { $signed = $true; break }
        Start-Sleep -Seconds 3
    }
    if (-not $signed) { Fail 'Signing failed with every timestamp server.' }
    & $signtool verify /pa /v $installer
    if ($LASTEXITCODE -ne 0) { Fail 'The signed installer failed Authenticode verification.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
        Fail 'The installer does not have a valid timestamped signature.'
    }
}

Step 'Testing the exact release installer'
Invoke-Checked { powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-WindowsInstaller.ps1 -Installer $installer } 'The exact release installer failed its test suite.'
if (-not $SkipSign) {
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
        Fail 'The installer signature changed or became invalid during testing.'
    }
}

Step 'Writing release artifacts'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "${hash}  KillerMCP-Setup.exe"
[IO.File]::WriteAllLines($sumsFile, @($checksumLine), [Text.Encoding]::ASCII)
$changelogLines = Get-Content -LiteralPath $changelogFile
$notes = New-Object System.Collections.Generic.List[string]
$inSection = $false
foreach ($line in $changelogLines) {
    if ($line -match "^## \[$([regex]::Escape($Version))\]") { $inSection = $true; continue }
    if ($inSection -and $line -match '^## \[') { break }
    if ($inSection) { $notes.Add($line) }
}
if ($notes.Count -eq 0) { Fail "Could not extract [$Version] notes from CHANGELOG.md." }
$notesFile = Join-Path $env:TEMP "KillerMCP-$Version-notes.md"
[IO.File]::WriteAllText($notesFile, ($notes -join "`r`n"), [Text.UTF8Encoding]::new($false))

if ($Publish) {
    Step "Publishing KillerMCP $Tag"
    git tag -a $Tag -m "KillerMCP $Tag"
    if ($LASTEXITCODE -ne 0) { Fail "Could not create tag $Tag." }
    git push origin $Tag
    if ($LASTEXITCODE -ne 0) { Fail "Could not push tag $Tag." }
    gh release create $Tag $installer $sumsFile --title "KillerMCP $Tag" --notes-file $notesFile --verify-tag
    if ($LASTEXITCODE -ne 0) { Fail 'GitHub release creation failed.' }
    gh release view $Tag --json url --jq '.url'
} else {
    Write-Host ''
    Write-Host 'Release artifacts are verified. Nothing was published.' -ForegroundColor Green
}
