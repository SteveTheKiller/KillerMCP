param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Package,
    [int]$PollSeconds = 300,
    [int]$Polls = 32,
    [int]$ConfirmFailPolls = 6,
    [scriptblock]$ReadState,
    [scriptblock]$Push
)

# A successful push is not an accepted package. Chocolatey validates it later, and its validator
# sometimes fails with "We were unable to find a package" while its own service is degraded; its
# instruction is to upload the package again. This watches the result, pushes again once on a failure,
# and fails the job if validation fails again, so a failed package never goes unnoticed. The package
# page is cached and can show Pending for hours after validation passes, so the result is read from
# the package feed instead.
$ErrorActionPreference = 'Stop'
$page = "https://community.chocolatey.org/packages/killermcp/$Version"
if (-not $ReadState) {
    $feed = "https://community.chocolatey.org/api/v2/Packages(Id='killermcp',Version='$Version')"
    $ReadState = {
        try { $entry = (Invoke-WebRequest $feed -UseBasicParsing -TimeoutSec 90).Content }
        catch { Write-Host "Package feed unavailable: $($_.Exception.Message). Retrying."; return $null }
        [regex]::Match($entry, '<d:PackageValidationResultStatus>(\w+)</d:PackageValidationResultStatus>').Groups[1].Value
    }.GetNewClosure()
}
if (-not $Push) {
    $Push = {
        choco push $Package --source https://push.chocolatey.org --api-key $env:CHOCO_API_KEY
        if ($LASTEXITCODE -ne 0) { throw 'Re-push to Chocolatey failed.' }
    }.GetNewClosure()
}

$repushed = $false
$pendingSinceRepush = $false
$failStreak = 0
$last = ''
for ($poll = 1; $poll -le $Polls; $poll++) {
    Start-Sleep -Seconds $PollSeconds
    $state = & $ReadState
    if ($null -eq $state) { continue }
    Write-Host "$(Get-Date -Format HH:mm:ss) validation: $(if ($state) { $state } else { 'not shown' })"
    if (-not $state) { continue }
    $last = $state
    if ($state -like 'Pass*') { return }
    if ($state -notlike 'Fail*') {
        if ($repushed) { $pendingSinceRepush = $true }
        $failStreak = 0
        continue
    }
    if (-not $repushed) {
        Write-Host 'Validation failed. Pushing the same package again as Chocolatey instructs.'
        & $Push
        $repushed = $true
        continue
    }
    # The feed can keep showing the first failure for a while after the re-push. A failure counts as the
    # second one once validation was seen running again, or when it lasts several polls in a row.
    $failStreak++
    if ($pendingSinceRepush -or $failStreak -ge $ConfirmFailPolls) { throw "Chocolatey validation failed twice. See $page" }
}
if ($last -like 'Fail*') { throw "Chocolatey validation still showed a failure when the wait ended. See $page" }
# Chocolatey's queue often takes longer than this. The package is already pushed, so a validation that
# is still pending is a warning, not a failed release.
Write-Host "::warning::Chocolatey validation was still pending when the wait ended. The package was pushed. See $page"
