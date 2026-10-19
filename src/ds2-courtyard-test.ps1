param([string]$Prefix = "yard", [int[]]$ShotsAt = @(0, 3, 6, 10, 15, 22))
# Asylum courtyard test (session 5) inside a run-ds2.ps1 -TestSave session with autopilot=1: enter as MP, wait until the
# character is in the world (loader "entry item" line), warp to the Asylum bonfire (EventLocation 3700010, facing the
# courtyard), then screenshots at the given seconds after arrival (e.g. to watch an --enemy-test enemy spawn and walk).
# Stops if the user is giving input to DS2.
$s = $PSScriptRoot
$shots = "C:\EchoesOfLordran\docs\shots"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log"
function Shot($name) { & "$s\shot-ds2.ps1" -Out (Join-Path $shots "$Prefix-$name.png") }
function Send([string[]]$c, [string]$marker) { $r = & "$s\ds2-do.ps1" -Commands ($c + "log $marker") -WaitFor "AUTOPILOT $marker" -TimeoutMs 30000; if ($LASTEXITCODE -eq 3) { "user active: stopping"; exit 3 } }
& "$s\ds2-focus.ps1" | Out-Null
& "$s\ds2-enter.ps1" -BootSeconds 42 -LoadSeconds 5
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt 240) { # DS2 sometimes boots slowly (run s5-31)
    $l = Get-Content $log | Select-String "entry item: (gave|character has)" | Select-Object -Last 1
    if ($l) { $l.Line; break }
    Start-Sleep -Seconds 2
}
Start-Sleep -Seconds 2
Send @("key RETURN 150", "wait 1000") "popup closed"
Send @("warp 18010000 3700010") "warp sent"
$arrived = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    if ($i -eq 15 -or $i -eq 35) { Send @("warp 18010000 3700010") "warp resent" } # a warp sent while still loading is lost
    $r = & "$s\ds2-pos.ps1"
    if ($r -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)' -and [double]$Matches[2] -gt 150) { "ARRIVED: $r"; $arrived = $true; break }
}
$a = Get-Date
foreach ($t in $ShotsAt) {
    $left = $t - ((Get-Date) - $a).TotalSeconds
    if ($left -gt 0) { Start-Sleep -Milliseconds ([int]($left * 1000)) }
    Shot ("t{0:D2}" -f $t)
    & "$s\ds2-pos.ps1"
}
if ($arrived) { exit 0 } else { exit 2 }
