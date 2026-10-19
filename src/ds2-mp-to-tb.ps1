param([string]$Prefix = "tb", [int]$WaitSeconds = 90)
# Test route for MP (test save data\test-saves\mp-tb-lit.sl2: MP at Crestfallen's Retreat with Fire Keepers' Dwelling lit):
# enter the game, rest, Travel -> Things Betwixt -> Fire Keepers' Dwelling, wait until the player stands in Things Betwixt
# (position check, up to $WaitSeconds), then report how often m10_02 was loaded and take screenshots.
# Run inside a run-ds2.ps1 -TestSave session. Stops if the user is giving input to DS2.
$s = $PSScriptRoot
$shots = "C:\EchoesOfLordran\docs\shots"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log"
function Guard { & "$s\user-active.ps1" | Out-Null; if ($LASTEXITCODE -ne 0) { "user input seen in DS2: stopping"; exit 1 } }
& "$s\ds2-focus.ps1" | Out-Null
& "$s\ds2-enter.ps1" -BootSeconds 42 -LoadSeconds 5 -Shot (Join-Path $shots "$Prefix-0-entered.png")
# "Loading character data" can take a while: wait until the player position is readable twice in a row
$ok = 0
for ($t = 0; $t -lt 90 -and $ok -lt 2; $t += 2) {
    $r = & "$s\ds2-pos.ps1"
    if ($r -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)') { $ok++ } else { $ok = 0 }
    Start-Sleep -Seconds 2
}
Start-Sleep -Seconds 4
$r
Guard
# A one step left puts MP in reach of Crestfallen's Retreat; rest; Travel; Things Betwixt (one to the right of Majula);
# its only bonfire, Fire Keepers' Dwelling.
& "$s\ds2-ctl.ps1" "key A 150" "wait 900" "key RETURN 150" "wait 5000" "key RETURN 150" "wait 2500" "key RIGHT 150" "wait 700" "key RETURN 150" "wait 1500" "log TRAVEL to Fire Keepers' Dwelling" "key RETURN 150" | Out-Null
Start-Sleep -Seconds 12
$arrived = $false
for ($t = 12; $t -lt $WaitSeconds; $t += 3) {
    $r = & "$s\ds2-pos.ps1"
    if ($r -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)' -and [double]$Matches[2] -gt 5 -and [double]$Matches[1] -lt -150) { $arrived = $true; "arrived: $r"; break }
    Start-Sleep -Seconds 3
}
if (-not $arrived) { "did not arrive in Things Betwixt within $WaitSeconds s" }
Start-Sleep -Seconds 5
& "$s\shot-ds2.ps1" -Out (Join-Path $shots "$Prefix-1-arrived.png")
$lines = Get-Content $log
$i = ($lines | Select-String "TRAVEL to Fire Keepers" | Select-Object -Last 1).LineNumber
$loads = $lines | Select-Object -Skip $i | Where-Object { $_ -match 'm10_02_00_00\.msb"' -and $_ -notmatch 'REDIRECT' }
"m10_02 loads after travel (only counted with log_opens=1): $(@($loads).Count)"
$lines | Where-Object { $_ -match 'REDIRECT archive "gamedata:/(param|EzState|map)/' } | ForEach-Object { $_.Substring(0, [Math]::Min(220, $_.Length)) }
if ($arrived) { exit 0 } else { exit 2 }
