param([Parameter(Mandatory = $true)][string[]]$Commands, [string]$WaitFor = "", [int]$TimeoutMs = 60000, [switch]$NoGuard)
# Sends autopilot commands to the test instance (like ds2-ctl.ps1) and, with -WaitFor REGEX, waits until a new lordran.log
# line matches (e.g. "GOTO (reached|stuck|timeout|can't|no player|lost)"), then prints the new autopilot result lines.
# Checks src\user-active.ps1 first (exit 3 if the user is playing the instance) unless -NoGuard.
$log = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log"
if (-not $NoGuard) {
    & "$PSScriptRoot\user-active.ps1" | Out-Null
    if ($LASTEXITCODE -ne 0) { "USER ACTIVE in DS2: not sending input"; exit 3 }
}
$before = (Get-Item $log).Length
& "$PSScriptRoot\ds2-ctl.ps1" @Commands | Out-Null
if (-not $WaitFor) { exit 0 }
$t = 0
while ($t -lt $TimeoutMs) {
    Start-Sleep -Milliseconds 250; $t += 250
    $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try {
        if ($fs.Length -gt $before) {
            $fs.Position = $before
            $text = (New-Object System.IO.StreamReader($fs)).ReadToEnd()
            # "log TEXT" commands appear as "AUTOPILOT TEXT"; key presses ("... keyboard reads so far") are left out
            $lines = $text -split "`r?`n" | Where-Object { $_ -match 'GOTO|PEEK|READX?|SCAN|AUTOPILOT|TRACE' -and $_ -notmatch 'keyboard reads|XInputGetState calls' }
            if ($lines | Where-Object { $_ -match $WaitFor }) { $lines | ForEach-Object { $_.Trim() }; exit 0 }
        }
    } finally { $fs.Close() }
}
"timed out after $TimeoutMs ms waiting for /$WaitFor/"
exit 1
