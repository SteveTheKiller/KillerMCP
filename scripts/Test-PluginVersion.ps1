param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $Root 'src\KillerMCP\KillerMCP.csproj') -Raw
$appVersion = [string]$project.Project.PropertyGroup.Version
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'KillerMCP.csproj must contain an x.y.z version.'
}
$plugin = Get-Content -LiteralPath (Join-Path $Root 'claude-plugin\.claude-plugin\plugin.json') -Raw | ConvertFrom-Json
if ([string]$plugin.version -cne $appVersion) {
    throw "KillerMCP plugin version '$($plugin.version)' must match app version '$appVersion'."
}
if ([string]$plugin.author.name -cne 'Steve the Killer') {
    throw 'KillerMCP plugin author must be Steve the Killer.'
}
Write-Output "KillerMCP app and plugin versions match: $appVersion. Publisher: Steve the Killer."

if ($env:OS -eq 'Windows_NT') {
    $manifest = Get-Content -LiteralPath (Join-Path $Root 'claude-plugin\.mcp.json') -Raw | ConvertFrom-Json
    $server = $manifest.mcpServers.KillerMCP
    $start = [Diagnostics.ProcessStartInfo]::new($server.command)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardOutput = $true
    $start.EnvironmentVariables['PSExecutionPolicyPreference'] = 'Restricted'
    $start.EnvironmentVariables['LOCALAPPDATA'] = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
    $start.Arguments = ($server.args | ForEach-Object {
        '"' + $_.Replace('${CLAUDE_PLUGIN_ROOT}', (Join-Path $Root 'claude-plugin')) + '"'
    }) -join ' '
    $process = [Diagnostics.Process]::Start($start)
    try {
        $errorOutput = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 1 -or $errorOutput -notlike '*KillerMCP is not installed for this Windows user*') {
            throw "Claude plugin launcher did not run under a restricted inherited policy: $errorOutput"
        }
    } finally { $process.Dispose() }
    Write-Output 'Claude plugin launcher runs under a restricted inherited execution policy.'
}
