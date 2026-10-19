param([int]$Map = 18010000, [int]$Loc = 3700010, [string]$Prefix = "bf", [switch]$NoEnter, [switch]$NoRest)
# Bonfire test (session 6) inside a run-ds2.ps1 -TestSave session with autopilot=1: enter as MP (unless -NoEnter), wait
# until the character is in the world, warp to MAP/LOC (an EventLocation in front of a bonfire), wait for arrival
# (position changes and stays put), press RETURN to rest (unless -NoRest) and take a screenshot.
# Lordran test points: Asylum 18010000/3700010, Firelink 19420000/3700010 (session 11: rule H1, was 19020000).
# Stops if the user is giving input to DS2.
$s = $PSScriptRoot
$shots = "C:\EchoesOfLordran\docs\shots"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log"
function Shot($name) { & "$s\shot-ds2.ps1" -Out (Join-Path $shots "$Prefix-$name.png") }
function Send([string[]]$c, [string]$marker) { $r = & "$s\ds2-do.ps1" -Commands ($c + "log $marker") -WaitFor "AUTOPILOT $marker" -TimeoutMs 30000; if ($LASTEXITCODE -eq 3) { "user active: stopping"; exit 3 } }
function Pos { $r = & "$s\ds2-pos.ps1"; if ($r -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)') { return @([double]$Matches[1], [double]$Matches[2], [double]$Matches[3]) } return $null }
& "$s\ds2-focus.ps1" | Out-Null
if (-not $NoEnter) {
    $mark = (Get-Item $log).Length
    & "$s\ds2-enter.ps1" -BootSeconds 42 -LoadSeconds 5
    $t0 = Get-Date; $resent = $false
    while (((Get-Date) - $t0).TotalSeconds -lt 240) {
        $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
        try { $fs.Position = $mark; $text = (New-Object System.IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Close() }
        $l = $text -split "`n" | Select-String "entry item: (gave|character has)" | Select-Object -Last 1
        if ($l) { $l.Line.Trim(); break }
        # the Data List sometimes keeps the last RETURN (MP highlighted): one more after a minute without a character
        if (((Get-Date) - $t0).TotalSeconds -gt 60 -and -not $resent) { $resent = $true; & "$s\ds2-ctl.ps1" "key RETURN 200" | Out-Null; "resent RETURN at the Data List" }
        Start-Sleep -Seconds 2
    }
    Start-Sleep -Seconds 2
    if ($l -and $l.Line -match 'gave') { Send @("key RETURN 150", "wait 1000") "popup closed" }
}
$p0 = Pos
"start position: $p0"
Send @("warp $Map $Loc") "warp sent"
$arrived = $false; $last = $null; $still = 0
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    if ($i -eq 15 -or $i -eq 35) { Send @("warp $Map $Loc") "warp resent" }
    $p = Pos
    if (-not $p -or -not $p0) { continue }
    $moved = [Math]::Abs($p[0] - $p0[0]) + [Math]::Abs($p[1] - $p0[1]) + [Math]::Abs($p[2] - $p0[2])
    if ($moved -gt 5 -and $last -and ([Math]::Abs($p[0] - $last[0]) + [Math]::Abs($p[1] - $last[1]) + [Math]::Abs($p[2] - $last[2])) -lt 0.05) { $still++ } else { $still = 0 }
    $last = $p
    if ($still -ge 2) { "ARRIVED: $p"; $arrived = $true; break }
}
if (-not $arrived) { "did not arrive"; Shot "noarrive"; exit 2 }
Start-Sleep -Seconds 3
Shot "arrived"
if (-not $NoRest) {
    Send @("key RETURN 150", "wait 4000") "rested"
    Shot "rest-menu"
}
exit 0
