param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path 'C:\Users\steve\scratch' ('killermcp-release-preflight-' + [guid]::NewGuid().ToString('N'))
$seed = Join-Path $scratch 'seed'
$engine = Join-Path $scratch 'engine'
$remote = Join-Path $scratch 'remote.git'
$checkout = Join-Path $scratch 'checkout'
$authorName = (& git.exe -C $repository config user.name).Trim()
$authorEmail = (& git.exe -C $repository config user.email).Trim()
if (-not $authorName -or -not $authorEmail) { throw 'The repository Git identity must be configured.' }

function Git([string]$Directory, [string[]]$Arguments) {
    & git.exe -C $Directory @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture git command failed: $($Arguments -join ' ')" }
}

function FixtureFile([string]$Path, [string]$Content) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Content)
}

[IO.Directory]::CreateDirectory($engine) | Out-Null
Git $engine @('init', '--initial-branch=main', '--quiet')
Git $engine @('config', 'user.name', $authorName)
Git $engine @('config', 'user.email', $authorEmail)
FixtureFile (Join-Path $engine 'src\KillerTools.Engine\KillerTools.Engine.csproj') '<Project />'
Git $engine @('add', '.')
Git $engine @('commit', '--quiet', '-m', 'Fixture engine')

[IO.Directory]::CreateDirectory($seed) | Out-Null
Git $seed @('init', '--initial-branch=main', '--quiet')
Git $seed @('config', 'user.name', $authorName)
Git $seed @('config', 'user.email', $authorEmail)
Copy-Item -LiteralPath (Join-Path $repository 'release.ps1') -Destination (Join-Path $seed 'release.ps1')
FixtureFile (Join-Path $seed 'src\KillerMCP\KillerMCP.csproj') '<Project><PropertyGroup><Version>0.6.0</Version></PropertyGroup></Project>'
FixtureFile (Join-Path $seed 'scripts\Test-PluginVersion.ps1') "Write-Output 'Fixture plugin check'"
FixtureFile (Join-Path $seed 'CHANGELOG.md') '## [0.6.0] - Unreleased'
# release.ps1 checks the Store submission before it builds; the stub records that it ran.
FixtureFile (Join-Path $seed 'store\Submit-StoreRelease.ps1') "param([string]`$ProductId, [string]`$PackageUrl, [switch]`$PreflightOnly) Write-Output ('STORE_PREFLIGHT ' + [bool]`$PreflightOnly)"
Git $seed @('-c', 'protocol.file.allow=always', 'submodule', 'add', '--quiet', $engine, 'dependencies/KillerTools')
Git $seed @('add', '.')
Git $seed @('commit', '--quiet', '-m', 'Fixture release inputs')
Git $scratch @('clone', '--bare', '--quiet', $seed, $remote)
Git $scratch @('-c', 'protocol.file.allow=always', 'clone', '--recurse-submodules', '--quiet', $remote, $checkout)
Git $checkout @('config', 'user.name', $authorName)
Git $checkout @('config', 'user.email', $authorEmail)
FixtureFile (Join-Path $checkout 'unpublished.txt') 'Unpublished fixture commit'
Git $checkout @('add', 'unpublished.txt')
Git $checkout @('commit', '--quiet', '-m', 'Unpublished fixture change')

$hostExecutable = (Get-Process -Id $PID).Path
foreach ($mode in @('-DryRun', '-SkipSign')) {
    $command = "function global:dotnet { Write-Output 'BUILD_STARTED'; `$global:LASTEXITCODE = 17 }; & '$checkout\release.ps1' $mode"
    $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass -Command $command 2>&1
    $exit = $LASTEXITCODE
    $text = $output -join "`n"
    if ($exit -ne 1 -or $text -notlike '*Local main and origin/main differ*' -or $text -like '*BUILD_STARTED*') {
        throw "Unpublished $mode did not stop before the build (exit $exit):`n$text`nFixture retained at $scratch"
    }
    Write-Output "PASS: $mode refuses unpublished commits before building."
}
$matched = Join-Path $scratch 'matched'
Git $scratch @('-c', 'protocol.file.allow=always', 'clone', '--recurse-submodules', '--quiet', $remote, $matched)
$command = "function global:dotnet { Write-Output 'BUILD_STARTED'; `$global:LASTEXITCODE = 17 }; & '$matched\release.ps1' -DryRun"
$output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass -Command $command 2>&1
$text = $output -join "`n"
if ($LASTEXITCODE -ne 1 -or $text -notlike '*BUILD_STARTED*' -or $text -like '*Local main and origin/main differ*') {
    throw "A matching checkout did not reach the build boundary:`n$text`nFixture retained at $scratch"
}
$storeAt = $text.IndexOf('STORE_PREFLIGHT True')
if ($storeAt -lt 0 -or $storeAt -gt $text.IndexOf('BUILD_STARTED')) {
    throw "The Store preflight did not run before the build:`n$text`nFixture retained at $scratch"
}
Write-Output 'PASS: a matching checkout reaches the build boundary.'
Write-Output "Fixture retained at $scratch"
