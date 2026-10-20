param([Parameter(Mandatory = $true)][string]$AssemblyPath, [Parameter(Mandatory = $true)][string]$ScratchPath)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchPath)
# The test writes and replaces files in this folder, so it has to be a throwaway folder.
$allowed = @([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), (Join-Path $env:USERPROFILE 'scratch\')) | ForEach-Object { $_.TrimEnd('\') + '\' }
if (-not ($allowed | Where-Object { $root.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) -and $root.Length -gt $_.Length })) { throw 'Use a task folder under the temporary folder or scratch.' }
[IO.Directory]::CreateDirectory($root) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$type = $assembly.GetType('KillerMCP.Setup.UpdatePreferences', $true)
$read = $type.GetMethod('Read', [Reflection.BindingFlags]'Static,NonPublic')
$save = $type.GetMethod('Save', [Reflection.BindingFlags]'Static,NonPublic')
[string]$path = Join-Path $root 'update-preferences.json'
foreach ($behavior in 'off','check','download','install') {
    [IO.File]::WriteAllText($path, '{"Behavior":"check","OtherSetting":42}')
    $save.Invoke($null, @($path, [string]$behavior, 'check')) | Out-Null
    if ($read.Invoke($null, @($path)) -ne $behavior) { throw "Could not persist $behavior." }
    $values = [IO.File]::ReadAllText($path) | ConvertFrom-Json
    if ($values.OtherSetting -ne 42) { throw 'Saving behavior discarded another setting.' }
}
[IO.File]::WriteAllText($path, '{"Behavior":"off","OtherSetting":43}')
$before = [IO.File]::ReadAllText($path)
$save.Invoke($null, @($path, 'check', 'check')) | Out-Null
if ([IO.File]::ReadAllText($path) -cne $before) { throw 'An untouched dropdown overwrote a concurrent choice.' }
[IO.File]::WriteAllText($path, '{"Behavior":"unknown"}')
if ($read.Invoke($null, @($path)) -ne 'check') { throw 'Invalid behavior did not default to check.' }
[IO.File]::WriteAllText($path, '{broken')
if ($read.Invoke($null, @($path)) -ne 'check') { throw 'Malformed preferences did not default to check.' }
Write-Output 'PASS: four choices round-trip, unrelated fields survive, unchanged choice preserves concurrent edits, invalid settings default to check.'
