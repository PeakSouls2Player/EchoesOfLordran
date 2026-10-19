param([Parameter(Mandatory = $true)][string]$Pack, [string]$Via = "19420000 3700010", [string]$Back = "", [switch]$DryRun, [switch]$Force)
# Hot swap (session 11): puts a freshly built pack into the RUNNING test instance without relaunching DS2.
# The loader checks data\lordran\pack on every file open (vfs.c: GetFileAttributesW per open, no cache), so files a map
# load reads are picked up the next time that map loads. This script:
#   1. diffs $Pack against the installed pack (size, then SHA-256) and lists added / changed / removed files;
#   2. sorts them: "map reload" (map, ezstate, per-map params, models, morpheme, timeact, map sound banks, ...) vs
#      "restart" (title\ = regulation, menu\, the FMOD project frpg2.fevbnd, worldmaplist, loader.ini: read once at boot);
#   3. copies the changes into the installed pack (removed files are deleted; loader.ini is never touched) and records
#      what it did in data\lordran\hot-swap.log;
#   4. with -Back "MAP LOC", warps the player away ($Via, default Firelink) and back so the map reloads from the new files.
# Restart-class changes are refused (exit 2) unless -Force: they would only apply after a relaunch, and a half-applied
# pack is a confusing test. -DryRun prints the plan only. Only for test instances started by run-ds2.ps1.
$ErrorActionPreference = "Stop"
$installed = "C:\EchoesOfLordran\data\lordran\pack"
$logFile = "C:\EchoesOfLordran\data\lordran\hot-swap.log"
$src = (Resolve-Path $Pack).Path.TrimEnd('\')
if ($src -eq $installed) { "the new pack is the installed pack"; exit 1 }
$skip = '^(loader\.ini|variant\.txt|build\.log)$'
$restart = '^(title\\|gamedata\\menu\\|gamedata\\sound\\frpg2\.fevbnd|gamedata\\map\\worldmaplist\\)'

function Files($root) {
    $h = @{}
    Get-ChildItem $root -Recurse -File | ForEach-Object { $rel = $_.FullName.Substring($root.Length + 1); if ($rel -notmatch $skip) { $h[$rel] = $_ } }
    $h
}
$new = Files $src; $old = Files $installed
$added = @(); $changed = @(); $removed = @()
foreach ($rel in $new.Keys) {
    if (-not $old.ContainsKey($rel)) { $added += $rel }
    elseif ($new[$rel].Length -ne $old[$rel].Length -or (Get-FileHash $new[$rel].FullName).Hash -ne (Get-FileHash $old[$rel].FullName).Hash) { $changed += $rel }
}
foreach ($rel in $old.Keys) { if (-not $new.ContainsKey($rel)) { $removed += $rel } }
$all = @($added + $changed + $removed)
$needRestart = @($all | Where-Object { $_ -match $restart })
"hot-swap ${src}: $($added.Count) added, $($changed.Count) changed, $($removed.Count) removed"
foreach ($r in ($added | Sort-Object)) { "  + $r" }
foreach ($r in ($changed | Sort-Object)) { "  ~ $r" }
foreach ($r in ($removed | Sort-Object)) { "  - $r" }
if (-not $all) { "nothing to swap"; exit 0 }
if ($needRestart) {
    "RESTART NEEDED for $($needRestart.Count) file(s) (read once at boot): " + ($needRestart -join ", ")
    if (-not $Force) { "not swapping (use -Force to copy anyway, then relaunch)"; exit 2 }
}
if ($DryRun) { exit 0 }
if (-not (Get-Process DarkSoulsII -ErrorAction SilentlyContinue)) { "note: DS2 is not running; the swap is a plain install" }

$stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Add-Content $logFile "[$stamp] from $src : +$($added.Count) ~$($changed.Count) -$($removed.Count)"
foreach ($rel in @($added + $changed)) {
    $to = Join-Path $installed $rel
    New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
    $ok = $false
    for ($i = 0; $i -lt 10 -and -not $ok; $i++) {
        try { Copy-Item $new[$rel].FullName $to -Force; $ok = $true } catch { Start-Sleep -Milliseconds 500 } # DS2 may hold it open mid-read
    }
    if (-not $ok) { "FAILED to copy $rel (locked?)"; Add-Content $logFile "  FAILED $rel"; exit 1 }
    Add-Content $logFile "  copied $rel"
}
foreach ($rel in $removed) { Remove-Item (Join-Path $installed $rel) -Force; Add-Content $logFile "  removed $rel" }
"swapped."

if ($Back) {
    if (-not (Get-Process DarkSoulsII -ErrorAction SilentlyContinue)) { "DS2 not running: no reload"; exit 0 }
    $v = $Via -split ' '; $b = $Back -split ' '
    "reloading: warp $Via, then $Back"
    $r = & "$PSScriptRoot\ds2-do.ps1" -Commands "warp $($v[0]) $($v[1])", "wait 15000", "log hs-away" -WaitFor "AUTOPILOT hs-away" -TimeoutMs 60000
    if ($LASTEXITCODE -eq 3) { "user active in DS2: stopped before the reload"; exit 3 }
    $r = & "$PSScriptRoot\ds2-do.ps1" -Commands "warp $($b[0]) $($b[1])", "wait 15000", "log hs-back" -WaitFor "AUTOPILOT hs-back" -TimeoutMs 60000
    if ($LASTEXITCODE -eq 3) { "user active in DS2: stopped before the return"; exit 3 }
    "reloaded: $(& "$PSScriptRoot\ds2-pos.ps1")"
}
exit 0
