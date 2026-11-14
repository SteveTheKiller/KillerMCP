function Invoke-SetupCheck {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WorkingDirectory = '', [switch]$Wait, [switch]$PassThru, [string]$WindowStyle = 'Hidden', [string]$RedirectStandardError = '')
    # Start-Process -Wait creates a job object on Windows PowerShell. Functional setup checks
    # wait directly; deliberate client-job behavior is exercised separately by ClientJob.ps1.
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = [IO.Path]::GetFullPath($FilePath)
    $start.Arguments = ($ArgumentList | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    if ($WorkingDirectory) { $start.WorkingDirectory = $WorkingDirectory }
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(300000)) { $process.Kill(); throw 'The setup check exceeded five minutes.' }
        $code = $process.ExitCode
        $output = $stdout.Result
        $errorText = $stderr.Result
        if ($RedirectStandardError) { [IO.File]::WriteAllText($RedirectStandardError, $errorText) }
        [PSCustomObject]@{ ExitCode = $code; Output = $output; Error = $errorText }
    }
    finally { $process.Dispose() }
}
