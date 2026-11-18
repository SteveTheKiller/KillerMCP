# KillerMCP installs for one Windows account, and its setup refuses an administrator token because an elevated
# install could write agent client settings through links a standard user planted. Chocolatey runs elevated by
# default, so an elevated run hands the verified setup copy to a one-time scheduled task that runs as the same
# signed-in account with its standard (Limited) token, then waits for it and returns setup's exit code.
function Invoke-KillerMcpSetup {
    param(
        [Parameter(Mandatory)] [string] $Setup,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.IsSystem) {
        throw 'KillerMCP installs for a signed-in Windows account, so it cannot be installed or removed by Chocolatey running as SYSTEM. Run choco from that account instead.'
    }
    $elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $elevated) {
        $process = Start-Process -FilePath $Setup -ArgumentList $Arguments -Wait -PassThru
        return $process.ExitCode
    }

    $name = 'KillerMCP Chocolatey ' + [guid]::NewGuid().ToString('N')
    $action = New-ScheduledTaskAction -Execute $Setup -Argument ($Arguments -join ' ')
    $principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
    Register-ScheduledTask -TaskName $name -Action $action -Principal $principal -Settings $settings | Out-Null
    try {
        Start-ScheduledTask -TaskName $name
        # 267009 means the task is running and 267011 means it has not started yet.
        $deadline = (Get-Date).AddMinutes(16)
        do {
            Start-Sleep -Seconds 1
            $state = (Get-ScheduledTask -TaskName $name).State
            $result = (Get-ScheduledTaskInfo -TaskName $name).LastTaskResult
        } while (($state -in 'Running', 'Queued' -or $result -in 267009, 267011) -and (Get-Date) -lt $deadline)
        if ($result -in 267009, 267011) {
            throw 'KillerMCP setup did not finish as your standard account. Chocolatey needs to run from a desktop session you are signed in to, not over SSH or remoting.'
        }
        return [int]$result
    }
    finally {
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    }
}
