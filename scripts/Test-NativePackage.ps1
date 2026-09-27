$ErrorActionPreference = 'Stop'
$architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$runtime = [System.Runtime.InteropServices.RuntimeInformation]
$platform = if ($runtime::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { 'win' } elseif ($runtime::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) { 'linux' } elseif ($runtime::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) { 'osx' } else { throw 'This operating system is not supported.' }
$runtimeIdentifier = "$platform-$architecture"

& (Join-Path $PSScriptRoot 'Build-NativePackage.ps1') -RuntimeIdentifier $runtimeIdentifier
if ($LASTEXITCODE -ne 0) { throw 'Native KillerMCP package verification failed.' }
