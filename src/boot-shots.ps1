param([string]$Label = "boot", [string]$OutDir, [int[]]$At = @(12, 25, 40), [switch]$Seamless)
# Boot test with screenshots: launches DS2 through run-ds2.ps1 and captures the window at the given seconds.
$src = $PSScriptRoot
New-Item -ItemType Directory -Force $OutDir | Out-Null
$total = ($At | Measure-Object -Maximum).Maximum + 5
$job = Start-Job -ArgumentList $src, $Label, $total, $Seamless.IsPresent -ScriptBlock {
    param($src, $label, $secs, $seamless)
    if ($seamless) { & "$src\run-ds2.ps1" -Label $label -Seconds $secs -Seamless } else { & "$src\run-ds2.ps1" -Label $label -Seconds $secs }
}
$t0 = Get-Date
foreach ($s in $At) {
    $wait = $s - ((Get-Date) - $t0).TotalSeconds
    if ($wait -gt 0) { Start-Sleep -Seconds $wait }
    & "$src\shot-ds2.ps1" -Out (Join-Path $OutDir ("{0}-{1:D2}s.png" -f $Label, $s))
}
Receive-Job $job -Wait
