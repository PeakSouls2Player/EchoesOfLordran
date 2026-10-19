param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Commands)
# Sends autopilot commands to the running test instance (loader autopilot=1): each argument is one command line,
# e.g.  ds2-ctl.ps1 "press START 200" "wait 3000" "press A 150".  See src\loader\autopilot.c for the command set.
$control = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\control.txt"
foreach ($c in $Commands) { Add-Content -Path $control -Value $c -Encoding ascii }
"sent $($Commands.Count) command(s)"
