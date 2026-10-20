param([string]$WorkflowFolder = '')

# Release workflows hold signing and publishing tokens, so everything they download must be pinned:
# PowerShell modules by exact version, and tools from other projects by exact release and SHA-256.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$folder = if ($WorkflowFolder) { $WorkflowFolder } else { Join-Path $root '.github\workflows' }
$problems = New-Object System.Collections.Generic.List[string]

foreach ($workflow in Get-ChildItem -LiteralPath $folder -Filter '*.yml') {
    $text = Get-Content -LiteralPath $workflow.FullName -Raw
    $steps = [regex]::Split($text, '(?m)^\s*- (?=name:|uses:)')
    foreach ($step in $steps) {
        $name = ([regex]::Match($step, '^name:\s*(.+)$', 'Multiline')).Groups[1].Value.Trim()
        $label = if ($name) { "$($workflow.Name): $name" } else { $workflow.Name }
        foreach ($install in [regex]::Matches($step, '(?m)^.*\bInstall-Module\b.*$')) {
            if ($install.Value -notmatch '-RequiredVersion\s+\S+') { $problems.Add("$label installs a module without -RequiredVersion.") }
        }
        if ($step -match 'releases/latest') { $problems.Add("$label looks up a latest release instead of a pinned one.") }
        foreach ($download in [regex]::Matches($step, 'https://github\.com/(?<owner>[^/\s"'']+)/[^\s"'']+/releases/download/[^\s"'']+')) {
            if ($download.Groups['owner'].Value -eq 'SteveTheKiller') { continue }
            if ($step -notmatch 'Get-FileHash' -or $step -notmatch "(?i)'[0-9a-f]{64}'") { $problems.Add("$label downloads $($download.Value) without checking a pinned SHA-256.") }
            if ($download.Value -match '\$tag\b|latest') { $problems.Add("$label downloads a release that is not pinned.") }
        }
    }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host $_ }
    throw "Release workflows have $($problems.Count) unpinned download(s)."
}
Write-Output 'Release workflows pin every download.'
