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
# Setup reports its own assembly version in Installed Apps and compares it with the installed host,
# so every shipped project has to carry the same release number.
$projects = @{
    'installer\KillerMCP.Setup.csproj' = @{ Version = $appVersion; AssemblyVersion = "$appVersion.0"; FileVersion = "$appVersion.0" }
    'src\KillerMCP.Configure\KillerMCP.Configure.csproj' = @{ Version = $appVersion }
    'src\KillerMCP.Notify\KillerMCP.Notify.csproj' = @{ Version = $appVersion }
}
foreach ($entry in $projects.GetEnumerator()) {
    [xml]$other = Get-Content -LiteralPath (Join-Path $Root $entry.Key) -Raw
    foreach ($property in $entry.Value.GetEnumerator()) {
        $actual = @($other.Project.PropertyGroup | ForEach-Object { $_.($property.Key) } | Where-Object { $_ }) | Select-Object -First 1
        if ([string]$actual -cne $property.Value) {
            throw "$($entry.Key) $($property.Key) '$actual' must be '$($property.Value)' to match KillerMCP.csproj."
        }
    }
}
Write-Output "KillerMCP app, setup, helper and plugin versions match: $appVersion. Publisher: Steve the Killer."

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
