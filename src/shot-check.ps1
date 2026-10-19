param([string]$Out, [double]$Min = 10)
# Takes a DS2 window screenshot; exit 0 if its brightness is at least $Min (e.g. title screen ~13.7, black 0).
$r = & "$PSScriptRoot\shot-ds2.ps1" -Out $Out
$r
if ($r -match 'brightness ([0-9.]+)' -and [double]$Matches[1] -ge $Min) { exit 0 }
exit 1
