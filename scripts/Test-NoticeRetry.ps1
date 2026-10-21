param([string]$HostPath = '')

# A notice that never opens must not count as shown, or the next host stays quiet for a day.
# A notice that did open must count, so the next host does not open another.
$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { throw 'The notice retry test requires Windows.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$mcpHost = if ($HostPath) { [IO.Path]::GetFullPath($HostPath) } else { Join-Path $root 'bin\Release\net10.0\KillerMCP.exe' }
if (-not (Test-Path -LiteralPath $mcpHost -PathType Leaf)) { throw "The KillerMCP host is missing at $mcpHost. Build the solution first." }

$scratch = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('KillerMCP-notice-retry-' + [guid]::NewGuid().ToString('N'))
$marker = Join-Path $scratch 'notice-starts.txt'
$variables = 'KILLERMCP_UPDATE_CACHE', 'KILLERMCP_UPDATE_API', 'KILLERMCP_NOTIFIER', 'KILLERMCP_TOOL_CALL_LOG', 'KILLERMCP_TEST_NOTICE_MARKER', 'KILLERMCP_NOTICE_WAIT_MS'
$previous = @{}
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }

function New-StandIn([string]$Path, [bool]$Opens) {
    # Records each start. One that opens signals the host the way the update window does and stays up.
    $body = if ($Opens) { 'using (var opened = System.Threading.EventWaitHandle.OpenExisting(@"Local\KillerMCP-UpdateNotice-Opened")) opened.Set(); System.Threading.Thread.Sleep(30000);' } else { '' }
    Add-Type -OutputType WindowsApplication -OutputAssembly $Path -TypeDefinition @"
public static class NoticeStandIn {
    public static void Main(string[] args) {
        System.IO.File.AppendAllText(System.Environment.GetEnvironmentVariable("KILLERMCP_TEST_NOTICE_MARKER"), System.Diagnostics.Process.GetCurrentProcess().Id + System.Environment.NewLine);
        $body
    }
}
"@
}

function Get-Starts { if (Test-Path -LiteralPath $marker) { @(Get-Content -LiteralPath $marker | Where-Object { $_ }).Count } else { 0 } }

# Runs one host until it has handled its startup notice, then closes it like a finished chat.
function Invoke-Host([int]$ExpectedStarts) {
    $start = [Diagnostics.ProcessStartInfo]::new($mcpHost)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline -and (Get-Starts) -lt $ExpectedStarts) { Start-Sleep -Milliseconds 50 }
        # Covers the host's wait for the notice to open.
        Start-Sleep -Milliseconds 2500
    }
    finally { if (-not $process.HasExited) { $process.Kill() }; $process.Dispose() }
}

try {
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $quits = Join-Path $scratch 'notice-quits.exe'
    $opens = Join-Path $scratch 'notice-opens.exe'
    New-StandIn $quits $false
    New-StandIn $opens $true
    $env:KILLERMCP_UPDATE_CACHE = Join-Path $scratch 'update-status.json'
    $env:KILLERMCP_UPDATE_API = 'https://127.0.0.1:9/no-release'
    $env:KILLERMCP_TOOL_CALL_LOG = Join-Path $scratch 'tool-calls.jsonl'
    $env:KILLERMCP_TEST_NOTICE_MARKER = $marker
    $env:KILLERMCP_NOTICE_WAIT_MS = '1500'
    $cache = @{ LatestVersion = '99.0.0'; CheckedAt = [DateTimeOffset]::UtcNow.ToString('o'); Notes = $null } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($env:KILLERMCP_UPDATE_CACHE, $cache, (New-Object Text.UTF8Encoding($false)))

    $env:KILLERMCP_NOTIFIER = $quits
    Invoke-Host 1
    if ((Get-Starts) -ne 1) { throw "The first host started the notice $(Get-Starts) times, expected once." }
    Invoke-Host 2
    if ((Get-Starts) -ne 2) { throw 'A notice that never opened was recorded as shown, so the next host did not try again.' }

    Remove-Item -LiteralPath (Join-Path $scratch 'update-notification.json') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $marker
    $env:KILLERMCP_NOTIFIER = $opens
    Invoke-Host 1
    Invoke-Host 1
    if ((Get-Starts) -ne 1) { throw "A notice that opened was shown again by the next host ($(Get-Starts) starts)." }
    Write-Output 'Notices that never open are retried, and notices that opened are not repeated.'
}
finally {
    Get-CimInstance Win32_Process -Filter "Name = 'notice-opens.exe' OR Name = 'notice-quits.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    Start-Sleep -Milliseconds 300
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}
