param(
    [string]$ArtifactRoot = '',
    [string]$SignedSetupPath = '',
    [string]$LabMachine = '',
    [string]$PreparedRoot = '',
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Installer review checks require Windows.' }
if (-not $BuildOnly -and $env:CI -ne 'true' -and (!$LabMachine -or $LabMachine -ine $env:COMPUTERNAME)) {
    throw 'Run executable installer reviews only on the leased disposable VM with its explicit -LabMachine name, or use -BuildOnly.'
}
$sourceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ArtifactRoot = & (Join-Path $PSScriptRoot 'Get-ArtifactRoot.ps1') -ArtifactRoot $ArtifactRoot
$runRoot = Join-Path $ArtifactRoot ('installer-review\run-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runRoot) | Out-Null
$artifacts = Join-Path $runRoot 'artifacts'

function Write-NewText([string]$Path, [string]$Text) {
    if (Test-Path -LiteralPath $Path) { throw "Preserve existing review file: $Path" }
    $stream = New-Object IO.FileStream($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $bytes = (New-Object Text.UTF8Encoding -ArgumentList $false).GetBytes($Text); $stream.Write($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }
}

function New-ReviewProject([string]$Name, [string]$Framework, [string[]]$Files, [string]$Entry, [switch]$Wpf) {
    $folder = Join-Path $runRoot $Name
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    Write-NewText (Join-Path $folder 'Entry.cs') $Entry
    $items = @('<Compile Include="Entry.cs" />')
    foreach ($file in $Files) { $items += '<Compile Include="' + [Security.SecurityElement]::Escape((Join-Path $sourceRoot $file)) + '" />' }
    if ($Wpf) { $items += '<ProjectReference Include="' + [Security.SecurityElement]::Escape((Join-Path $sourceRoot 'Shell\KillerMCP.Notify.csproj')) + '" />' }
    if ($Framework -eq 'net48') { $items += '<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />' }
    $properties = '<TargetFramework>' + $Framework + '</TargetFramework><OutputType>Exe</OutputType><LangVersion>latest</LangVersion><Nullable>enable</Nullable><ImplicitUsings>disable</ImplicitUsings><EnableDefaultItems>false</EnableDefaultItems><PlatformTarget>x64</PlatformTarget>'
    if ($Wpf) { $properties += '<UseWPF>true</UseWPF>' }
    $project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>' + $properties + '</PropertyGroup><ItemGroup>' + ($items -join '') + '</ItemGroup></Project>'
    $path = Join-Path $folder ($Name + '.csproj')
    Write-NewText $path $project
    $path
}

if (!$PreparedRoot) {
# Public identity fixture exported from the verified installed signed setup. This is certificate
# data only, with no private key. Identity checks deliberately do not depend on certificate expiry.
$certificateSha256 = '213AB774B384FC0ACE996A8F365B4B667DD503670C3B9CB91B14345A1BD4D1F0'
$publicKeySha256 = 'E09F43EEE1DF45D4FDE291303F671C978DFC992E59FAD9DF29367B7223F0A83B'
$certificateBase64 = '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'
$certificatePath = Join-Path $runRoot 'publisher-identity.cer'
[IO.File]::WriteAllBytes($certificatePath, [Convert]::FromBase64String($certificateBase64))
if ((Get-FileHash -LiteralPath $certificatePath -Algorithm SHA256).Hash -ne $certificateSha256) { throw 'Public publisher fixture hash differs.' }
$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($certificatePath)
$sha = [Security.Cryptography.SHA256]::Create()
try { $actualKey = [BitConverter]::ToString($sha.ComputeHash($certificate.GetPublicKey())).Replace('-', '') }
finally { $sha.Dispose(); $certificate.Dispose() }
if ($actualKey -cne $publicKeySha256) { throw 'Public publisher fixture key differs from the trusted signing identity.' }
if (!$SignedSetupPath) { $SignedSetupPath = $certificatePath }
if (!(Test-Path -LiteralPath $SignedSetupPath -PathType Leaf)) { throw 'Supply an existing signed setup or public signer certificate fixture.' }

$signerEntry = @'
using System;
internal static class Entry {
  private static void Main(string[] args) {
    try { InstallerSignerReviewChecks.Run(args[0]); InstallerPathReviewChecks.Run(args[1]); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
  }
}
'@
$windowEntry = @'
using System;
internal static class Entry {
  [STAThread] private static void Main(string[] args) {
    try { UpdateWindowReviewChecks.Run(args[0]); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
  }
}
'@
$lifecycleEntry = @'
using System;
internal static class Entry {
  private static void Main(string[] args) {
    try { InstallerLifecycleReviewChecks.Run(args[0], args[1]); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
  }
}
'@
$processTreeEntry = @'
using System;
internal static class Entry {
  private static void Main(string[] args) {
    try { if (!InstallerProcessTreeReviewChecks.Serve(args)) InstallerProcessTreeReviewChecks.Run(args[0], args[1]); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
  }
}
'@
$signerProject = New-ReviewProject 'SignerReview' 'net10.0' @('installer\ReleaseSignature.cs', 'installer\ManifestPaths.cs', 'tests\KillerMCP.Setup.Checks\InstallerSignerReviewChecks.cs', 'tests\KillerMCP.Setup.Checks\InstallerPathReviewChecks.cs') $signerEntry
$windowProject = New-ReviewProject 'WindowReview' 'net48' @('tests\UpdateWindowReviewChecks.cs') $windowEntry -Wpf
$lifecycleProject = New-ReviewProject 'LifecycleReview' 'net48' @('tests\InstallerLifecycleReviewChecks.cs') $lifecycleEntry
$processTreeProject = New-ReviewProject 'ProcessTreeReview' 'net48' @('tests\InstallerProcessTreeReviewChecks.cs') $processTreeEntry
$payload = Join-Path $runRoot 'compile-only-payload.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadSource = Join-Path $runRoot 'empty-payload'
[IO.Directory]::CreateDirectory($payloadSource) | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($payloadSource, $payload)
dotnet build (Join-Path $sourceRoot 'installer\KillerMCP.Setup.csproj') -c Release --artifacts-path $artifacts "/p:PayloadZip=$payload"
if ($LASTEXITCODE -ne 0) { throw 'Installer review assembly compilation failed.' }
dotnet publish $signerProject -c Release -r win-x64 --self-contained true --artifacts-path $artifacts -o (Join-Path $runRoot 'signer-publish')
if ($LASTEXITCODE -ne 0) { throw 'Signer/path review publication failed.' }
dotnet build $windowProject -c Release --artifacts-path $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Update-window review compilation failed.' }
dotnet build $lifecycleProject -c Release --artifacts-path $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Installer lifecycle review compilation failed.' }
dotnet build $processTreeProject -c Release --artifacts-path $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Installer process-tree review compilation failed.' }
$manifest = [PSCustomObject]@{
    runRoot = $runRoot
    signerExe = Join-Path $runRoot 'signer-publish\SignerReview.exe'
    windowExe = Join-Path $artifacts 'bin\WindowReview\release\WindowReview.exe'
    lifecycleExe = Join-Path $artifacts 'bin\LifecycleReview\release\LifecycleReview.exe'
    processTreeExe = Join-Path $artifacts 'bin\ProcessTreeReview\release\ProcessTreeReview.exe'
    setupAssembly = Join-Path $artifacts 'bin\KillerMCP.Setup\release\KillerMCP-Setup.exe'
    signerFixture = [IO.Path]::GetFullPath($SignedSetupPath)
    minimumSelected = 21
}
foreach ($file in @($manifest.signerExe, $manifest.windowExe, $manifest.lifecycleExe, $manifest.processTreeExe, $manifest.setupAssembly)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Compiled review artifact is missing: $file" }
}
$hashes = [ordered]@{}
foreach ($name in @('signerExe', 'windowExe', 'lifecycleExe', 'processTreeExe', 'setupAssembly', 'signerFixture')) { $hashes[$name] = (Get-FileHash -LiteralPath $manifest.$name -Algorithm SHA256).Hash }
$manifest | Add-Member -NotePropertyName sha256 -NotePropertyValue $hashes
Write-NewText (Join-Path $runRoot 'review-manifest.json') ($manifest | ConvertTo-Json -Depth 5)
Write-Output ('Installer review manifest: ' + (Join-Path $runRoot 'review-manifest.json'))
if ($BuildOnly) { Write-Output 'Compiled installer review projects; no executable checks were run.'; return }
}
else {
    if ($BuildOnly) { throw '-PreparedRoot and -BuildOnly are separate operations.' }
    $PreparedRoot = (Resolve-Path -LiteralPath $PreparedRoot).Path
    $manifest = Get-Content -LiteralPath (Join-Path $PreparedRoot 'review-manifest.json') -Raw | ConvertFrom-Json
    $originalRoot = $manifest.runRoot.TrimEnd('\') + '\'
    foreach ($name in @('signerExe', 'windowExe', 'lifecycleExe', 'processTreeExe', 'setupAssembly', 'signerFixture')) {
        if (!$manifest.$name.StartsWith($originalRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Prepared path escapes its source artifact root: $name" }
        $mapped = Join-Path $PreparedRoot $manifest.$name.Substring($originalRoot.Length)
        if ((Get-FileHash -LiteralPath $mapped -Algorithm SHA256).Hash -ne $manifest.sha256.$name) { throw "Prepared artifact hash differs: $name" }
        $manifest.$name = $mapped
    }
}

function Invoke-Review([string]$Name, [string]$Exe, [string[]]$Arguments, [int]$Minimum) {
    $fixtures = Join-Path $runRoot ($Name + '-fixtures')
    [IO.Directory]::CreateDirectory($fixtures) | Out-Null
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Exe
    $start.Arguments = (($Arguments + @($fixtures)) | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['TEMP'] = $fixtures
    $start.EnvironmentVariables['TMP'] = $fixtures
    $child = [Diagnostics.Process]::Start($start)
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    if (!$child.WaitForExit(120000)) { $child.Kill(); throw "$Name review timed out." }
    $code = $child.ExitCode
    $output = $stdout.Result
    $errorText = $stderr.Result
    $child.Dispose()
    Write-NewText (Join-Path $runRoot ($Name + '.stdout.txt')) $output
    Write-NewText (Join-Path $runRoot ($Name + '.stderr.txt')) $errorText
    if ($code -ne 0) { throw "$Name review failed ($code): $errorText" }
    $selected = 0
    foreach ($match in [regex]::Matches($output, '(\d+) selected regression')) { $selected += [int]$match.Groups[1].Value }
    if ($selected -lt $Minimum) { throw "$Name selected only $selected checks; expected at least $Minimum." }
    Write-Output $output
}
Invoke-Review 'signer-path' $manifest.signerExe @($manifest.signerFixture) 5
Invoke-Review 'window' $manifest.windowExe @() 5
Invoke-Review 'lifecycle' $manifest.lifecycleExe @($manifest.setupAssembly) 9
Invoke-Review 'process-tree' $manifest.processTreeExe @($manifest.setupAssembly) 2
Write-Output 'All 21 selected installer reviews passed.'
