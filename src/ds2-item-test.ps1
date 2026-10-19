param([string]$Prefix = "item", [int]$Slot = 3, [switch]$Trace)
# Lordran entry-item test (session 5) inside a run-ds2.ps1 -TestSave session with autopilot=1: enter as MP, wait for
# the loader to give the "Echo of Lordran" (entry.c: popup), close it, Inventory -> consumables -> item $Slot (MP: 3rd,
# after Lifegem and Homeward Bone) -> Use, then wait until the player stands in the Asylum (y > 150) and take
# screenshots of the cell (camera turned in steps). Stops if the user is giving input to DS2.
$s = $PSScriptRoot
$shots = "C:\EchoesOfLordran\docs\shots"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log"
function Shot($name) { & "$s\shot-ds2.ps1" -Out (Join-Path $shots "$Prefix-$name.png") }
function Send([string[]]$c, [string]$marker) { $r = & "$s\ds2-do.ps1" -Commands ($c + "log $marker") -WaitFor "AUTOPILOT $marker" -TimeoutMs 30000; if ($LASTEXITCODE -eq 3) { "user active: stopping"; exit 3 } }
& "$s\ds2-focus.ps1" | Out-Null
& "$s\ds2-enter.ps1" -BootSeconds 42 -LoadSeconds 5
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) { # DS2 sometimes boots slowly (runs s5-31, s5-42)
    $l = Get-Content $log | Select-String "entry item: (gave|character has)" | Select-Object -Last 1
    if ($l) { $l.Line; break }
    Start-Sleep -Seconds 2
}
Start-Sleep -Seconds 2
Shot "1-given"
& "$s\ds2-pos.ps1"
$right = @(); for ($i = 1; $i -lt $Slot; $i++) { $right += "key RIGHT 150"; $right += "wait 450" }
Send (@("key RETURN 150", "wait 1200", "key ESCAPE 150", "wait 1500", "key RIGHT 150", "wait 700", "key RETURN 150", "wait 1800", "key RETURN 150", "wait 1000") + $right + @("wait 400")) "on item"
Shot "2-on-item"
Send @("key RETURN 150", "wait 1000") "submenu"
Shot "3-use"
if ($Trace) { Send @("trace 461F20 40") "traced" }
Send @("key RETURN 150", "wait 300") "USE Echo of Lordran"
$arrived = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 2
    $r = & "$s\ds2-pos.ps1"
    if ($r -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)' -and [double]$Matches[2] -gt 150) { "ARRIVED: $r"; $arrived = $true; Shot "4a-arrival"; Start-Sleep -Seconds 1; Shot "4b-arrival"; break }
}
Start-Sleep -Seconds 4
Shot "4-cell"
& "$s\ds2-pos.ps1" -Chain "16148F0,D0,60" -Count 12
foreach ($k in 1..3) { Send @("key L 600", "wait 700") "turn $k"; Shot "5-turn$k" }
if ($Trace) { "event commands dispatched (trace 461F20): " + @(Get-Content $log | Select-String "TRACE 461F20 #").Count }
if ($arrived) { exit 0 } else { exit 2 }
