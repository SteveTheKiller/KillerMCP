param(
    [Parameter(Mandatory = $true)][string]$SetupPath,
    [Parameter(Mandatory = $true)][string]$ScratchPath,
    [ValidateSet('off','check','download','install')][string]$Behavior = 'off'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ScratchPath)
# The test writes and replaces files in this folder, so it has to be a throwaway folder.
$allowed = @([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), (Join-Path $env:USERPROFILE 'scratch\')) | ForEach-Object { $_.TrimEnd('\') + '\' }
if (-not ($allowed | Where-Object { $root.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) -and $root.Length -gt $_.Length })) { throw 'Use a task folder under the temporary folder or scratch.' }
# The test changes these for the setup it starts; the calling session gets its own values back.
$savedVariables = @{}
foreach ($name in 'TEMP', 'TMP', 'KILLERMCP_TEST_INSTALL_ROOT', 'KILLERMCP_TEST_UPDATE_API', 'KILLERMCP_TEST_CAPTURE') { $savedVariables[$name] = [Environment]::GetEnvironmentVariable($name) }
$destination = Join-Path $root 'install'
[IO.Directory]::CreateDirectory($destination) | Out-Null
[IO.File]::Copy((Resolve-Path $SetupPath), (Join-Path $destination 'KillerMCP.exe'), $true)
[IO.File]::WriteAllText((Join-Path $root 'update-preferences.json'), ('{"Behavior":"' + $Behavior + '"}'))
$capture = Join-Path $root 'notifier-arguments.json'
if ($Behavior -ne 'off') {
    $fakeNotifier = Join-Path $destination 'KillerMCP.Notify.exe'
    if (-not [IO.File]::Exists($fakeNotifier)) {
        Add-Type -TypeDefinition 'public static class CaptureNotifier { public static void Main(string[] args) { System.IO.File.WriteAllLines(System.Environment.GetEnvironmentVariable("KILLERMCP_TEST_CAPTURE"), args); } }' -OutputAssembly $fakeNotifier -OutputType WindowsApplication
    }
    $env:KILLERMCP_TEST_CAPTURE = $capture
}
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$env:TEMP = $root
$env:TMP = $root
$env:KILLERMCP_TEST_INSTALL_ROOT = $destination
$env:KILLERMCP_TEST_UPDATE_API = "http://127.0.0.1:$port/latest"
$start = [Diagnostics.ProcessStartInfo]::new((Resolve-Path $SetupPath), '/check-update')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $requested = $false
    for ($i = 0; $i -lt 100 -and -not $process.HasExited; $i++) {
        if ($listener.Pending()) {
            $requested = $true
            $client = $listener.AcceptTcpClient()
            $body = '{"tag_name":"v99.0.0"}'
            $bytes = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n$body")
            $client.GetStream().Write($bytes, 0, $bytes.Length)
            $client.Dispose()
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $process.HasExited) { throw 'Startup checker did not finish.' }
    if ($Behavior -eq 'off' -and $requested) { throw 'Off mode still requested the release API.' }
    if ($process.ExitCode -ne 0) { throw "Startup checker exited $($process.ExitCode)." }
    if ($Behavior -eq 'off') { Write-Output 'PASS: off mode exits without contacting the release API.' }
    else {
        for ($i = 0; $i -lt 30 -and -not [IO.File]::Exists($capture); $i++) { Start-Sleep -Milliseconds 100 }
        if (-not [IO.File]::Exists($capture)) { throw 'The notifier was not launched.' }
        $arguments = [IO.File]::ReadAllLines($capture)
        $index = [Array]::IndexOf($arguments, '--behavior')
        if ($index -lt 0 -or $arguments[$index + 1] -ne $Behavior) { throw 'Startup did not forward the chosen behavior.' }
        $index = [Array]::IndexOf($arguments, '--latest')
        if ($index -lt 0 -or $arguments[$index + 1] -ne '99.0.0') { throw 'Startup did not forward the discovered version.' }
        Write-Output "PASS: $Behavior launches the notifier with the saved behavior and discovered update."
    }
} finally {
    $listener.Stop(); if (-not $process.HasExited) { $process.Kill() }; $process.Dispose()
    foreach ($name in $savedVariables.Keys) { [Environment]::SetEnvironmentVariable($name, $savedVariables[$name]) }
}
