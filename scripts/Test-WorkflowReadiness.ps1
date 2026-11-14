param(
    [string]$Commit = '',
    [string]$Repository = 'SteveTheKiller/KillerMCP'
)
$ErrorActionPreference = 'Stop'
if (!$Commit) {
    $Commit = (& git -C (Join-Path $PSScriptRoot '..') rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the commit to validate.' }
}
if ($Commit -notmatch '^[a-fA-F0-9]{40}$') { throw 'Workflow validation needs a full commit SHA.' }
$response = & gh run list --repo $Repository --workflow checks.yml --commit $Commit --limit 100 --json databaseId,headSha,status,conclusion,url
if ($LASTEXITCODE -ne 0) { throw 'Could not read GitHub Checks. Workflow readiness has not been verified.' }
$runs = ($response -join "`n") | ConvertFrom-Json
$latest = $runs | Where-Object { $_.headSha -eq $Commit } | Sort-Object databaseId -Descending | Select-Object -First 1
if (!$latest) { throw "No GitHub Checks run exists for $Commit. Push this commit and let Checks finish before release." }
if ($latest.status -ne 'completed' -or $latest.conclusion -ne 'success') {
    throw "GitHub Checks has not passed for $Commit ($($latest.status), $($latest.conclusion)). $($latest.url)"
}
Write-Output "GitHub Checks passed for $Commit. $($latest.url)"
