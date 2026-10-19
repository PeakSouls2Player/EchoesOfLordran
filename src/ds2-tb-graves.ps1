param([string]$Prefix = "tb")
# Things Betwixt event check (session 5 bisect): from Fire Keepers' Dwelling (after ds2-mp-to-tb.ps1), walk through the
# old women's hut and out of its back door to Griant's grave (o00_9050, instance 10024100; route from lr.cmd navpath),
# then RETURN = examine. Vanilla shows "[name] Offer souls to grave? Souls needed: N" (mapevent 1211, TB event 111113 ->
# subroutines 2147483643/2147483645). The dialog is closed with BACKSPACE (back = no choice made, no souls spent).
$s = $PSScriptRoot
$shots = "C:\EchoesOfLordran\docs\shots"
function Shot($name) { & "$s\shot-ds2.ps1" -Out (Join-Path $shots "$Prefix-$name.png") }
# navmesh route: bonfire -> front door -> through the hut -> back door -> in front of Griant's grave (approach from the west)
$route = "path -181.73 -144.96 -187.47 -149.70 -196.22 -152.29 -205.97 -162.57 -203.23 -166.75 -196.80 -165.88"
$r = & "$s\ds2-do.ps1" -Commands $route -WaitFor "GOTO (reached|failed|can't|no player|usage)" -TimeoutMs 120000
$r
if ($LASTEXITCODE -eq 3) { exit 3 }
Shot "2-at-grave"
$r = & "$s\ds2-do.ps1" -Commands "key RETURN 150", "wait 2500", "log examined grave" -WaitFor "AUTOPILOT examined grave" -TimeoutMs 10000
if ($LASTEXITCODE -eq 3) { exit 3 }
Shot "3-examined"
$r = & "$s\ds2-do.ps1" -Commands "key BACK 150", "wait 1500", "log closed" -WaitFor "AUTOPILOT closed" -TimeoutMs 10000
Shot "4-closed"
& "$s\ds2-pos.ps1"
