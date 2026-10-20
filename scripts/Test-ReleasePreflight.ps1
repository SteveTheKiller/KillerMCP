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

# Runs a release with stand-in commands. Its error output is part of the result, not a failure of this test.
function Invoke-Release([string]$Command) {
    $ErrorActionPreference = 'Continue'
    $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass -Command $Command 2>&1
    [pscustomobject]@{ Exit = $LASTEXITCODE; Text = ($output | ForEach-Object { "$_" }) -join "`n" }
}

# The dash check stops the release when git grep cannot run, instead of passing because it found nothing.
$result = Invoke-Release "function global:git { if (`$args[0] -eq 'grep') { [Console]::Error.WriteLine('fatal: fixture grep failure'); `$global:LASTEXITCODE = 128; return }; & git.exe @args }; function global:dotnet { Write-Output 'BUILD_STARTED'; `$global:LASTEXITCODE = 17 }; & '$matched\release.ps1' -DryRun"
$text = $result.Text
if ($result.Exit -ne 1 -or $text -like '*BUILD_STARTED*' -or $text -notlike '*dash check could not run*') {
    throw "A failed dash check did not stop the release:`n$text`nFixture retained at $scratch"
}
Write-Output 'PASS: a dash check that cannot run stops the release.'

# Vulnerable dependencies stop the release before the tests and the installer.
$vulnerable = Join-Path $scratch 'vulnerable.json'
FixtureFile $vulnerable '{"version":1,"projects":[{"path":"KillerMCP.csproj","frameworks":[{"framework":"net10.0","topLevelPackages":[{"id":"Fixture.Package","resolvedVersion":"1.0.0","vulnerabilities":[{"severity":"High","advisoryurl":"https://example.invalid/advisory"}]}]}]}]}'
$result = Invoke-Release "function global:dotnet { if (`$args -contains 'list') { Get-Content -LiteralPath '$vulnerable'; `$global:LASTEXITCODE = 0; return }; if (`$args[0] -eq 'run') { Write-Output 'TESTS_STARTED'; `$global:LASTEXITCODE = 17; return }; `$global:LASTEXITCODE = 0 }; & '$matched\release.ps1' -DryRun"
$text = $result.Text
if ($result.Exit -ne 1 -or $text -like '*TESTS_STARTED*' -or $text -notlike '*Fixture.Package*') {
    throw "A vulnerable dependency did not stop the release:`n$text`nFixture retained at $scratch"
}
Write-Output 'PASS: a vulnerable dependency stops the release.'

# The exact release installer goes through every Windows suite Test-All runs, including the update paths.
$testAll = Get-Content -LiteralPath (Join-Path $repository 'scripts\Test-All.ps1') -Raw
$releaseScript = Get-Content -LiteralPath (Join-Path $repository 'release.ps1') -Raw
$windowsSuites = [regex]::Matches($testAll.Substring($testAll.IndexOf('IsOSPlatform')), 'Test-[A-Za-z]+\.ps1') | ForEach-Object Value | Select-Object -Unique
foreach ($suite in $windowsSuites) {
    if ($releaseScript -notmatch [regex]::Escape($suite)) { throw "release.ps1 does not run $suite on the release installer." }
}
Write-Output 'PASS: the release installer runs every Windows suite.'

# A GitHub release that cannot be created removes the tag it pushed, so the release can run again.
# The fixture checkout's origin is the fixture's own bare repository.
try {
    function global:gh { [Console]::Error.WriteLine('fixture release failure'); $global:LASTEXITCODE = 1 }
    $notes = Join-Path $scratch 'notes.md'
    FixtureFile $notes 'Fixture notes'
    $published = $null
    try { & (Join-Path $PSScriptRoot 'Publish-Release.ps1') -Repository $matched -Tag 'v0.6.0' -Assets @($notes) -NotesFile $notes } catch { $published = $_.Exception.Message }
    if (-not $published -or $published -notlike '*removed from origin*') { throw "A failed GitHub release did not report its tag cleanup: $published`nFixture retained at $scratch" }
    if (& git.exe -C $remote tag --list 'v0.6.0') { throw "A failed GitHub release left its tag on origin.`nFixture retained at $scratch" }
    if (& git.exe -C $matched tag --list 'v0.6.0') { throw "A failed GitHub release left its local tag.`nFixture retained at $scratch" }
}
finally {
    Remove-Item -Path Function:\gh -ErrorAction SilentlyContinue
}
Write-Output 'PASS: a failed GitHub release removes the tag it pushed.'
Write-Output "Fixture retained at $scratch"
