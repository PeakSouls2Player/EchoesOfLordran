param([string]$Shot = "", [int]$BootSeconds = 32, [int]$LoadSeconds = 28, [int]$Up = 0)
# -Up N moves the Data List cursor up N rows before the last RETURN (the first character is one row above the test character).
# Drives a freshly started test instance (loader autopilot=1) from the logos into the game: waits until the process has
# run $BootSeconds (title screen up), then RETURN through the title, both offline-mode notices, Continue and the
# highlighted character; waits $LoadSeconds for the load and optionally saves a screenshot.
$p = Get-Process DarkSoulsII -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "DS2 is not running"; exit 1 }
$wait = $BootSeconds - ((Get-Date) - $p.StartTime).TotalSeconds
if ($wait -gt 0) { Start-Sleep -Seconds $wait }
$pick = @(); for ($i = 0; $i -lt $Up; $i++) { $pick += "key UP 150"; $pick += "wait 600" }
$cmds = @("key RETURN 200", "wait 3500", "key RETURN 200", "wait 3500", "key RETURN 200", "wait 4500", "key RETURN 200", "wait 9000") + $pick + @("key RETURN 200", "log entered game")
& "$PSScriptRoot\ds2-ctl.ps1" @cmds | Out-Null
Start-Sleep -Seconds (17 + $LoadSeconds)
if ($Shot) { & "$PSScriptRoot\shot-ds2.ps1" -Out $Shot }
