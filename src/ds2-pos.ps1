param([string]$Chain = "16148F0,D0,90", [int]$Count = 3, [int]$TimeoutMs = 4000)
# Asks the autopilot for a peek (default: the player position, [[exe+0x16148F0]+0xD0]+0x90) and prints the logged
# result line. Needs a running test instance with autopilot=1.
$log = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log"
$before = (Get-Item $log).Length
& "$PSScriptRoot\ds2-ctl.ps1" "peek $Chain $Count" | Out-Null
$t = 0
while ($t -lt $TimeoutMs) {
    Start-Sleep -Milliseconds 200; $t += 200
    $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try {
        if ($fs.Length -gt $before) {
            $fs.Position = $before
            $text = (New-Object System.IO.StreamReader($fs)).ReadToEnd()
            $line = ($text -split "`n") | Where-Object { $_ -match 'PEEK' } | Select-Object -Last 1
            if ($line) { $line.Trim(); exit 0 }
        }
    } finally { $fs.Close() }
}
"no PEEK result within $TimeoutMs ms"
exit 1
