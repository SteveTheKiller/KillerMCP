param([string]$HostPath = '')

# A host inside an agent client's job opens the update notice. When the client tears that job
# down, the notice must stay open so the user can still act on it.
$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { throw 'The client job notice test requires Windows.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$mcpHost = if ($HostPath) { [IO.Path]::GetFullPath($HostPath) } else { Join-Path $root 'bin\Release\net10.0\KillerMCP.exe' }
if (-not (Test-Path -LiteralPath $mcpHost -PathType Leaf)) { throw "The KillerMCP host is missing at $mcpHost. Build the solution first." }
. (Join-Path $PSScriptRoot 'ClientJob.ps1')

$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-client-notice-' + [guid]::NewGuid().ToString('N'))
$marker = Join-Path $scratch 'notice-started.txt'
$notifier = Join-Path $scratch 'notice-standin.exe'
$variables = 'KILLERMCP_UPDATE_CACHE', 'KILLERMCP_UPDATE_API', 'KILLERMCP_NOTIFIER', 'KILLERMCP_TOOL_CALL_LOG', 'KILLERMCP_TEST_NOTICE_MARKER'
$previous = @{}
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$hostProcess = $null
$job = $null
$noticeId = $null

try {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    # Stands in for the update window: records that it started, then waits like an open window.
    Add-Type -OutputType WindowsApplication -OutputAssembly $notifier -TypeDefinition @'
public static class NoticeStandIn {
    public static void Main(string[] args) {
        System.IO.File.WriteAllText(System.Environment.GetEnvironmentVariable("KILLERMCP_TEST_NOTICE_MARKER"), System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
        System.Threading.Thread.Sleep(60000);
    }
}
'@
    $env:KILLERMCP_UPDATE_CACHE = Join-Path $scratch 'update-status.json'
    $env:KILLERMCP_UPDATE_API = 'https://127.0.0.1:9/no-release'
    $env:KILLERMCP_NOTIFIER = $notifier
    $env:KILLERMCP_TOOL_CALL_LOG = Join-Path $scratch 'tool-calls.jsonl'
    $env:KILLERMCP_TEST_NOTICE_MARKER = $marker
    # A fresh cached check that offers a newer release, so the host shows the notice without the network.
    $cache = @{ LatestVersion = '99.0.0'; CheckedAt = [DateTimeOffset]::UtcNow.ToString('o'); Notes = $null } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($env:KILLERMCP_UPDATE_CACHE, $cache, (New-Object Text.UTF8Encoding($false)))

    $job = New-Object KillerMCPTests.ClientJob
    $start = [Diagnostics.ProcessStartInfo]::new($mcpHost)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $hostProcess = [Diagnostics.Process]::Start($start)
    $job.Add($hostProcess.Handle)

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $marker)) { Start-Sleep -Milliseconds 50 }
    if (-not (Test-Path -LiteralPath $marker)) { throw 'The host never opened the update notice.' }
    $noticeId = [int](Get-Content -LiteralPath $marker -Raw)

    # The agent client tears down the job that holds its host.
    $job.Dispose()
    Start-Sleep -Seconds 2
    if (-not $hostProcess.HasExited) { throw 'Closing the client job did not end the host.' }
    if (-not (Get-CimInstance Win32_Process -Filter "ProcessId = $noticeId")) { throw 'The update notice closed when the agent client closed the host that opened it.' }
    Write-Output 'The update notice stayed open after the agent client closed its host.'
}
finally {
    if ($job) { $job.Dispose() }
    if ($hostProcess) { if (-not $hostProcess.HasExited) { $hostProcess.Kill() }; $hostProcess.Dispose() }
    if ($noticeId) { Stop-Process -Id $noticeId -Force -ErrorAction SilentlyContinue }
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Start-Sleep -Milliseconds 300
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}
