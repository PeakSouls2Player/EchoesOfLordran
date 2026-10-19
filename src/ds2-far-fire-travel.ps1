param([string]$ShotDir = "C:\EchoesOfLordran\docs\shots", [string]$Prefix = "travel", [int]$LoadSeconds = 25)
# From the test character's load point in Majula (MP: ~(1.2, 5.1, -16.9) facing +x, The Far Fire 8.5 m ahead), walks to
# The Far Fire, rests, and picks Travel -> Majula -> The Far Fire. With build-pack --asylum-travel-test (bonfire 4650
# retargeted) that loads m18_01_00_00. Keyboard map (KeyConfigParam): RETURN = pad A (interact/confirm), BACKSPACE = menu
# back, E = pad X (use item), Shift+Left/Right = menu LB/RB. Stops if the user is giving input to DS2.
$s = $PSScriptRoot
function Guard { & "$s\user-active.ps1" | Out-Null; if ($LASTEXITCODE -ne 0) { "user input seen in DS2: stopping"; exit 1 } }
function Shot($name) { & "$s\shot-ds2.ps1" -Out (Join-Path $ShotDir "$Prefix-$name.png") }
Guard
& "$s\ds2-pos.ps1"
& "$s\ds2-ctl.ps1" "key W 1300" "wait 800" "key W,D 350" "wait 800" "key D 150" "wait 900" | Out-Null
Start-Sleep -Seconds 5
& "$s\ds2-pos.ps1"
Shot "1-at-bonfire"
Guard
& "$s\ds2-ctl.ps1" "key RETURN 150" "wait 5000" "key RETURN 150" "wait 2500" "key RETURN 150" "wait 2000" | Out-Null
Start-Sleep -Seconds 10
Shot "2-travel-menu"
Guard
& "$s\ds2-ctl.ps1" "log travel: confirm The Far Fire" "key RETURN 150" "wait 500" | Out-Null
Start-Sleep -Seconds $LoadSeconds
Shot "3-arrived"
& "$s\ds2-pos.ps1"
