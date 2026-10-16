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
