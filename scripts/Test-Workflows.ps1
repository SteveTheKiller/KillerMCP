param([string]$WorkflowFolder = '', [switch]$LintOnly)

# Release workflows hold signing and publishing tokens, so everything they download must be pinned:
# PowerShell modules by exact version, actions by commit, and tools from other projects by exact
# release and SHA-256. Event data reaches scripts only through env, every workflow limits its token,
# and a KillerMCP installer is published only after its signature is checked.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$folder = if ($WorkflowFolder) { $WorkflowFolder } else { Join-Path $root '.github\workflows' }
$problems = New-Object System.Collections.Generic.List[string]

foreach ($workflow in Get-ChildItem -LiteralPath $folder -Filter '*.yml') {
    $text = Get-Content -LiteralPath $workflow.FullName -Raw
    if ($text -notmatch '(?m)^\s*permissions:') { $problems.Add("$($workflow.Name) does not limit its token with permissions.") }
    foreach ($line in ($text -split '\r?\n')) {
        if ($line -match '\$\{\{\s*(github\.event\.|inputs\.)' -and $line -notmatch '^\s*(?!(run|script|shell)\s*:)[A-Za-z_][A-Za-z0-9_]*:\s*\$\{\{[^}]*\}\}\s*$') { $problems.Add("$($workflow.Name) expands event data inside a script; pass it through env: $($line.Trim())") }
        if ($line -match '^\s*-?\s*uses:\s*(?<action>[^@\s]+)@(?<ref>\S+)' -and $Matches['ref'] -notmatch '^[0-9a-f]{40}$') { $problems.Add("$($workflow.Name) uses $($Matches['action']) by tag instead of by commit.") }
        if ($line -match '(?<![\w-])-i?(not)?match\s+''\^v') { $problems.Add("$($workflow.Name) checks a version tag without case sensitivity; use -cmatch or -cnotmatch: $($line.Trim())") }
    }
    $steps = [regex]::Split($text, '(?m)^\s*- (?=name:|uses:)')
    foreach ($step in $steps) {
        $name = ([regex]::Match($step, '^name:\s*(.+)$', 'Multiline')).Groups[1].Value.Trim()
        $label = if ($name) { "$($workflow.Name): $name" } else { $workflow.Name }
        foreach ($install in [regex]::Matches($step, '(?m)^.*\bInstall-Module\b.*$')) {
            if ($install.Value -notmatch '-RequiredVersion\s+\S+') { $problems.Add("$label installs a module without -RequiredVersion.") }
        }
        if ($step -match 'releases/latest') { $problems.Add("$label looks up a latest release instead of a pinned one.") }
        foreach ($download in [regex]::Matches($step, 'https://github\.com/(?<owner>[^/\s"'']+)/[^\s"'']+/releases/download/[^\s"'']+')) {
            if ($download.Groups['owner'].Value -eq 'SteveTheKiller') {
                if ($download.Value -match 'KillerMCP-Setup\.exe' -and $step -notmatch 'Assert-ReleaseInstaller') { $problems.Add("$label publishes a KillerMCP installer without checking its signature.") }
                continue
            }
            if ($step -notmatch 'Get-FileHash' -or $step -notmatch "(?i)'[0-9a-f]{64}'") { $problems.Add("$label downloads $($download.Value) without checking a pinned SHA-256.") }
            if ($download.Value -match '\$tag\b|latest') { $problems.Add("$label downloads a release that is not pinned.") }
        }
    }
}

if ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'New-WinGetManifest.ps1') -Raw) -notmatch 'Assert-ReleaseInstaller') { $problems.Add('New-WinGetManifest.ps1 submits an installer without checking its signature.') }

if ($problems.Count -gt 0) {
    if ($LintOnly) { return $problems }
    $problems | ForEach-Object { Write-Host $_ }
    throw "Release workflows have $($problems.Count) problem(s)."
}
if ($LintOnly) { return }
Write-Output 'Release workflows pin every download and action, keep event data out of scripts and check installer signatures.'

# The checks catch event data run directly as a step's script, not only inside a longer script.
if (-not $WorkflowFolder) {
    $fixture = Join-Path ([IO.Path]::GetTempPath()) ('killermcp-workflow-lint-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    try {
        [IO.File]::WriteAllText((Join-Path $fixture 'fixture.yml'), "name: Fixture`non: workflow_dispatch`njobs:`n  run:`n    runs-on: windows-latest`n    permissions:`n      contents: read`n    steps:`n      - name: Run input`n        run: `${{ github.event.inputs.tag }}`n")
        $lint = @(& $PSCommandPath -WorkflowFolder $fixture -LintOnly 2>&1 | ForEach-Object { "$_" })
        if (-not ($lint -like '*expands event data*')) { throw 'The workflow check accepted event data run as a step script.' }
    }
    finally { Remove-Item -LiteralPath $fixture -Recurse -Force }
    Write-Output 'The workflow check refuses event data run as a step script.'
}

# The WinGet manifest takes the same exact version tags as the Chocolatey workflow.
$tagParameter = (Get-Command (Join-Path $PSScriptRoot 'New-WinGetManifest.ps1')).Parameters['Tag']
$tagRule = $tagParameter.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidatePatternAttribute] }
if (-not $tagRule) { throw 'New-WinGetManifest.ps1 does not check its tag.' }
foreach ($badTag in @('V1.2.3', "v1.2.3`n", "v1.2.$([char]0x0663)")) {
    if ([regex]::IsMatch($badTag, $tagRule.RegexPattern, $tagRule.Options)) { throw "New-WinGetManifest.ps1 accepts the tag '$($badTag.Trim())'." }
}
if (-not [regex]::IsMatch('v1.2.3', $tagRule.RegexPattern, $tagRule.Options)) { throw 'New-WinGetManifest.ps1 refuses a version tag.' }
Write-Output 'Release tags are exact version tags in every release workflow.'

# The installer check accepts only the KillerMCP publisher's timestamped signature.
$assert = Join-Path $PSScriptRoot 'Assert-ReleaseInstaller.ps1'
$unsigned = Join-Path ([IO.Path]::GetTempPath()) ('killermcp-unsigned-' + [guid]::NewGuid().ToString('N') + '.exe')
[IO.File]::WriteAllText($unsigned, 'not signed')
try {
    $refused = $false
    try { & $assert -Path $unsigned } catch { $refused = $true }
    if (-not $refused) { throw 'The installer check accepted an unsigned file.' }
}
finally { Remove-Item -LiteralPath $unsigned -Force }
$otherPublisher = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $otherPublisher) {
    $refused = $false
    try { & $assert -Path $otherPublisher } catch { $refused = $true }
    if (-not $refused) { throw 'The installer check accepted another publisher.' }
}
if ($env:KILLERMCP_SIGNED_SETUP -and (Test-Path -LiteralPath $env:KILLERMCP_SIGNED_SETUP)) { & $assert -Path $env:KILLERMCP_SIGNED_SETUP }
Write-Output 'The release installer check accepts only the KillerMCP signature.'

# Chocolatey validation fails the job when it fails again after the one re-push, whether or not a
# poll saw it pending in between, and passes or warns otherwise. A failure seen only before the
# re-push is not a second failure, and the wait ends inside its time budget even when every poll
# would fit.
$wait = Join-Path $PSScriptRoot 'Wait-ChocolateyValidation.ps1'
function Invoke-Wait([string[]]$States, [int]$Minutes = 155) {
    $queue = [Collections.Generic.Queue[string]]::new([string[]]$States)
    $pushes = [ref]0
    $outcome = try { & $wait -Version '0.0.0' -Package 'fixture.nupkg' -PollSeconds 0 -Polls $States.Count -Minutes $Minutes -ConfirmFailPolls 3 -ReadState { if ($queue.Count) { $queue.Dequeue() } else { 'Pending' } }.GetNewClosure() -Push { $pushes.Value++ }.GetNewClosure(); 'ok' } catch { 'failed' }
    [pscustomobject]@{ Outcome = $outcome; Pushes = $pushes.Value }
}
$cases = @(
    @{ States = @('Pending', 'Passed'); Outcome = 'ok'; Pushes = 0 },
    @{ States = @('Failed', 'Pending', 'Passed'); Outcome = 'ok'; Pushes = 1 },
    @{ States = @('Failed', 'Pending', 'Failed'); Outcome = 'failed'; Pushes = 1 },
    @{ States = @('Failed', 'Failed', 'Failed', 'Failed'); Outcome = 'failed'; Pushes = 1 },
    @{ States = @('Pending', 'Pending'); Outcome = 'ok'; Pushes = 0 },
    @{ States = @('Failed', 'Failed'); Outcome = 'failed'; Pushes = 1 },
    @{ States = @('Pending', 'Failed'); Outcome = 'ok'; Pushes = 1 },
    @{ States = @('Failed', 'Failed', 'Failed', 'Failed'); Minutes = 0; Outcome = 'ok'; Pushes = 0 }
)
foreach ($case in $cases) {
    $minutes = if ($case.ContainsKey('Minutes')) { $case.Minutes } else { 155 }
    $result = Invoke-Wait $case.States $minutes
    if ($result.Outcome -ne $case.Outcome -or $result.Pushes -ne $case.Pushes) { throw "Chocolatey validation wait for $($case.States -join ', ') ended $($result.Outcome) after $($result.Pushes) push(es)." }
}
Write-Output 'Chocolatey validation fails the job on a second validation failure.'
