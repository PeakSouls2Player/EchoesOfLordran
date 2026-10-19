param([string]$Prefix = "steps", [string[]]$Steps = @(), [string]$Map = "18010000", [switch]$NoEnter, [switch]$Background)
# Step-driven in-game test (session 7) inside a run-ds2.ps1 -TestSave session with autopilot=1. Enters as MP (unless
# -NoEnter), waits for the character, closes the entry-item popup, then runs the steps in order:
#   "warp LOC X Z"   warp to EventLocation LOC of -Map; waits until the player stands within 2.5 m of (X, Z) (resends twice)
#   "goto X Z"       closed-loop walk (autopilot goto), waits for its result line
#   "key K [MS]"     a key press/hold (autopilot key names: RETURN, W, L, BACK, ...)
#   "sleep S"        wait S seconds
#   "shot NAME"      screenshot docs\shots\PREFIX-NAME.png (+ position line)
#   "pos"            log the position
# Stops (exit 3) if the user is giving input to DS2. Write: powershell -Command "& .\src\ds2-steps.ps1 -Steps 'a','b'".
$s = $PSScriptRoot
$shots = "C:\EchoesOfLordran\docs\shots"
$log = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log"
function Shot($name) { & "$s\shot-ds2.ps1" -Out (Join-Path $shots "$Prefix-$name.png") | Out-Null; "shot ${name}: $(Pos)" }
function Send([string[]]$c, [string]$marker, [int]$timeout = 30000) {
    $r = & "$s\ds2-do.ps1" -Commands ($c + "log $marker") -WaitFor "AUTOPILOT $marker|GOTO (reached|stuck|timeout|can't|no player|lost)" -TimeoutMs $timeout
    if ($LASTEXITCODE -eq 3) { "user active: stopping"; exit 3 }
    $r | Where-Object { $_ -match 'GOTO' }
}
function Pos { $r = & "$s\ds2-pos.ps1"; if ($r -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)') { "{0} {1} {2}" -f $Matches[1], $Matches[2], $Matches[3] } else { "?" } }
function PosXZ { $r = & "$s\ds2-pos.ps1"; if ($r -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)') { , @([double]$Matches[1], [double]$Matches[3]) } else { $null } }

if (-not $Background) { & "$s\ds2-focus.ps1" | Out-Null } # -Background: the instance stays behind the user's apps (run-ds2.ps1 -Background)
if (-not $NoEnter) {
    & "$s\ds2-enter.ps1" -BootSeconds 42 -LoadSeconds 5
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt 240) {
        $l = Get-Content $log | Select-String "entry item: (gave|character has)" | Select-Object -Last 1
        if ($l) { $l.Line; break }
        Start-Sleep -Seconds 2
    }
    Start-Sleep -Seconds 2
    Send @("key RETURN 150", "wait 1000") "popup closed" | Out-Null
}
$n = 0
foreach ($step in $Steps) {
    $n++
    $w = $step -split ' '
    "[$n] $step"
    switch ($w[0]) {
        "warp" {
            $tx = [double]$w[2]; $tz = [double]$w[3]; $ok = $false
            Send @("warp $Map $($w[1])") "warp $n" | Out-Null
            for ($i = 0; $i -lt 45; $i++) {
                Start-Sleep -Seconds 2
                if ($i -eq 12 -or $i -eq 28) { Send @("warp $Map $($w[1])") "rewarp $n" | Out-Null }
                $p = PosXZ
                if ($p -and [math]::Abs($p[0] - $tx) -lt 2.5 -and [math]::Abs($p[1] - $tz) -lt 2.5) { $ok = $true; break }
            }
            Start-Sleep -Seconds 3
            "  arrived=$ok at $(Pos)"
        }
        "goto" { Send @("goto $($w[1]) $($w[2])") "goto $n" 40000; "  at $(Pos)" }
        "key" { $ms = if ($w.Count -gt 2) { $w[2] } else { 150 }; Send @("key $($w[1]) $ms") "key $n" | Out-Null }
        "sleep" { Start-Sleep -Milliseconds ([int]([double]$w[1] * 1000)) }
        "shot" { Shot $w[1] }
        "pos" { "  at $(Pos)" }
        default { "unknown step $step" }
    }
}
exit 0
