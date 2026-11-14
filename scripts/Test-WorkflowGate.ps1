$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'Test-WorkflowReadiness.ps1'
$commit = 'a' * 40
$fixture = [pscustomobject]@{ Response = ''; ExitCode = 0 }
$previousGh = Get-Command gh -CommandType Function -ErrorAction SilentlyContinue
$fakeGh = { $global:LASTEXITCODE = $fixture.ExitCode; $fixture.Response }.GetNewClosure()
Set-Item -Path Function:global:gh -Value $fakeGh
function Check([string]$Name, [object[]]$Runs, [bool]$Accept) {
    $fixture.Response = ConvertTo-Json -InputObject $Runs -Depth 5 -Compress
    $accepted = $true
    try { & $gate -Commit $commit | Out-Null } catch { $accepted = $false }
    if ($accepted -ne $Accept) { throw "Workflow readiness verdict is wrong: $Name" }
    Write-Output "PASS $Name"
}
try {
    $success = @{ databaseId = 1; headSha = $commit; status = 'completed'; conclusion = 'success'; url = 'https://example.invalid/checks' }
    Check 'successful exact commit' @($success) $true
    foreach ($state in 'failure','cancelled','skipped','timed_out','neutral') {
        Check $state @(@{databaseId=2;headSha=$commit;status='completed';conclusion=$state}) $false
    }
    Check 'running checks' @(@{databaseId=2;headSha=$commit;status='in_progress';conclusion=''}) $false
    Check 'no run' @() $false
    Check 'different commit' @(@{databaseId=2;headSha=('b'*40);status='completed';conclusion='success'}) $false
    Check 'newer failed run overrides older success' @($success,@{databaseId=2;headSha=$commit;status='completed';conclusion='failure'}) $false
    $fixture.ExitCode = 1
    Check 'GitHub lookup failure' @($success) $false
    $fixture.ExitCode = 0
    $fixture.Response = 'not-json'
    $refused = $false
    try { & $gate -Commit $commit | Out-Null } catch { $refused = $true }
    if (!$refused) { throw 'Invalid GitHub response was accepted.' }
    Write-Output 'PASS malformed response'
    Write-Output 'All 12 workflow readiness checks passed.'
} finally {
    if ($previousGh) { Set-Item -Path Function:global:gh -Value $previousGh.ScriptBlock }
    else { Remove-Item -LiteralPath Function:\gh -ErrorAction SilentlyContinue }
}
