param([Parameter(Mandatory = $true)][string]$Pack, [string]$Lr = "C:\EchoesOfLordran\src\lr.cmd", [string[]]$Maps = @("m18_01_00_00", "m19_42_00_00"), [string]$Only = "", [string]$Baseline = "", [string]$Refs = "")
# Offline gate (session 11): every pack check the tracks wrote, in one command, before any in-game run.
# Runs each check with the given lr.cmd (main's by default, or a sandbox's), writes PACK\verify.log, prints one line per
# check (PASS / FAIL / SKIP = this build of the tools has no such command) and exits 1 if anything failed.
# -Baseline OLD\verify.log marks a FAIL as KNOWN when the same check failed with the same last line there (e.g. main's
# pack), so only regressions count as failures. -Only REGEX runs just the matching checks (e.g. -Only 'esd|talk').
# Usage: powershell -File src\verify-pack.ps1 -Pack data\sandbox\merge11\pack -Lr data\sandbox\merge11\lr.cmd
$root = "C:\EchoesOfLordran"
Set-Location $root
$pack = (Resolve-Path $Pack).Path
$log = Join-Path $pack "verify.log"
# session 11 (leap12): -Refs DIR = where the ds1boss-ref-* / ds1rig-ref-* reference builds live (default data\sandbox; a sandbox whose rules change
# the boss files rebuilds its own: lr ds1boss-build DIR\ds1boss-ref-cXXXX --boss cXXXX)
$refRoot = if ($Refs) { (Resolve-Path $Refs).Path } else { "$root\data\sandbox" }
"verify-pack $pack ($(Get-Date -Format s)) with $Lr" | Set-Content $log

$checks = @()
foreach ($m in $Maps) {
    $checks += , @("esd-check $m", "esd-check", $pack, $m)
    $checks += , @("talk-sim $m", "talk-sim", $pack, $m, "--scenario", "all")
    $checks += , @("event-sim lint $m", "event-sim", "lint", $pack, $m)
    $checks += , @("msg-check $m", "msg-check", $pack, $m)
    # sound-check takes the DS1 map. Session 11 (burg11): the hosts of Mappings\ds1-maps.tsv (rule H1: Firelink m19_42, was
    # m19_02; the Burg m19_41 = DS1 m10_01)
    $ds1Of = @{ "m19_02_00_00" = "m10_02_00_00"; "m19_42_00_00" = "m10_02_00_00"; "m19_41_00_00" = "m10_01_00_00"; "m19_40_00_00" = "m10_00_00_00" }
    $ds1 = if ($ds1Of.ContainsKey($m)) { $ds1Of[$m] } else { $m }
    $checks += , @("sound-check $m", "sound-check", $ds1, "--pack", $pack)
    # session 11 (walk11): walk-check = void, invisible walls, draw groups, step-ramp slopes, object coverage, textures
    $checks += , @("walk-check $m", "walk-check", $pack, $m, "--seams")
    $checks += , @("obj-check $m", "obj-check", $pack, $m) # session 11 (fx11): DS1 object behaviours (break trigger/sound/effect/drops, chests)
    # session 12 (merge12, burg11 round 9): rule UV1, converted map-piece UVs vs DS1. Residual (burg11 NOTES): single triangles wider than
    # +-16, clamped: m18_01 1 mesh / 33 tris, m19_41 7 / 34 (KNOWN via the baseline). Not on m19_40: the merged Depths + Blighttown map
    # (DS1 m10_00 has two m2080 flvers: uv-check's model dictionary throws)
    if ($m -ne "m19_40_00_00") { $checks += , @("uv-check $m", "uv-check", $pack, $m, $ds1) }
}
$checks += , @("flag-check", "flag-check", $pack)
$checks += , @("spot-check", "spot-check", "all", "--pack", $pack)
$checks += , @("tex-collide", "tex-collide", "$pack\gamedata", "data\ds2")
$checks += , @("parts-check", "parts-check", $pack)
$checks += , @("nmb-ids", "nmb-ids", $pack)
$checks += , @("nmb-runtime-check", "nmb-runtime-check", "$pack\gamedata\morpheme4\chr")
$checks += , @("world-events", "world-events", $pack)
$checks += , @("event-sim world", "event-sim", "world", $pack)
$checks += , @("event-sim nests", "event-sim", "nests", $pack)
$checks += , @("event-sim doors", "event-sim", "doors", $pack)
$checks += , @("origin-check", "origin-check", $pack)
$checks += , @("ring-check", "ring-check", $pack)
$checks += , @("ds1boss-check", "ds1boss-check", "$refRoot\ds1boss-ref", "--pack", $pack) # DS1 boss reference (lr ds1boss-build data\sandbox\ds1boss-ref; rebuilt session 11 after enemy11)
$checks += , @("ai-check", "ai-check", $pack)
$checks += , @("ds1boss-check c2230", "ds1boss-check", "$refRoot\ds1boss-ref-c2230", "--pack", $pack) # session 11 (ret11): the Stray Demon (lr ds1boss-build DIR --boss c2230)
# session 11 (burgchr11): the Burg's 1:1 bosses (build-pack --burg-chrs; lr ds1boss-build DIR --boss cXXXX), checked when the pack holds
# their anim set (DS1 chr -> anibnd)
$burgBosses = @{ "c2250" = "c2180"; "c2240" = "c2150"; "c2300" = "c2300"; "c3340" = "c2070"; "c1201" = "c1201"; "c3300" = "c2080"; "c3460" = "c3460"; "c5350" = "c5350"; "c3430" = "c3430" }
foreach ($b in $burgBosses.Keys | Sort-Object) { if (Test-Path "$pack\gamedata\morpheme4\chr\$($burgBosses[$b]).anibnd.dcx") { $checks += , @("ds1boss-check $b", "ds1boss-check", "$refRoot\ds1boss-ref-$b", "--pack", $pack) } }
# session 11 (dchr11): the Depths' 1:1 boss-pipeline characters (lr ds1boss-build data\sandbox\ds1boss-ref-cXXXX --boss cXXXX) and the Butcher on
# the 1:1 rig pipeline (lr ds1rig-build data\sandbox\ds1rig-ref-c2660 --chr c2660; checked on the reference: ds1rig-check --pack's melee-row
# check predates E9c and fails for every rig), when the pack holds the anim set
$depthsBosses = @{ "c1200" = "c1190"; "c1202" = "c1220"; "c3200" = "c3200"; "c3270" = "c3280"; "c5260" = "c5260" }
foreach ($b in $depthsBosses.Keys | Sort-Object) { if (Test-Path "$pack\gamedata\morpheme4\chr\$($depthsBosses[$b]).anibnd.dcx") { $checks += , @("ds1boss-check $b", "ds1boss-check", "$refRoot\ds1boss-ref-$b", "--pack", $pack) } }
if (Test-Path "$pack\gamedata\morpheme4\chr\c2660.anibnd.dcx") { $checks += , @("ds1rig-check c2660", "ds1rig-check", "$refRoot\ds1rig-ref-c2660") }
$checks += , @("burg-chr-check", "burg-chr-check", $pack) # session 11 (burgchr11): every Burg character the pack builds is complete (files, params, AI)
$checks += , @("balance-check", "balance-check", $pack) # session 11 (bal11): rules BAL1-BAL3, checks BAL-C0..C4 (Mappings\balance.tsv)
$checks += , @("gesture-check", "gesture-check", $pack) # session 11 (gest11): DS1 gestures: c0001/c000100 only additive vs vanilla (rule GS1), table, texts, icons
$checks += , @("ik-chain-check", "ik-chain-check", $pack)
$checks += , @("special-check", "special-check", $pack) # session 11 (fx11): DS1 enemy specials (bullets, buffs, event AI requests)
$checks += , @("hit-chain-check", "hit-chain-check", $pack) # session 12 (ehit12): every enemy attack -> DS2 hit chain (rule E-ID1 enemy id range, rows, hit shapes, model dummies, TAE 2200 slots)
$checks += , @("bullet-check", "bullet-check", $pack) # session 12 (bomb12): player + enemy projectile chains vs vanilla (effects < 30000, children, damage categories, rules E10f/E10g/GD4b)
# session 12 (merge12, burg11 round 9): rule DG1, seam pieces both DS1 maps carry are drawn together no more often than in DS1 (both directions)
$zfight = @(@("m19_42_00_00", "m19_41_00_00"), @("m19_41_00_00", "m19_40_00_00"))
foreach ($z in $zfight) { if (($Maps -contains $z[0]) -and ($Maps -contains $z[1])) {
    $checks += , @("seam-zfight $($z[0])>$($z[1])", "seam-zfight", $pack, $z[0], $z[1])
    $checks += , @("seam-zfight $($z[1])>$($z[0])", "seam-zfight", $pack, $z[1], $z[0]) } }
$checks += , @("warp-check", "warp-check", $pack)   # session 11 (fix11): WT1-WT3 warp targets / spawn boxes / shared ids, BF1-BF4 bonfires
$checks += , @("travel-check", "travel-check", $pack)   # session 11 (travel12): TC1 every DS1 bonfire has a row, TC2 vanilla rows, TC3 Travel pictures (not blank, not a template copy, unique)
$checks += , (@("event-sim idle", "event-sim", "idle", $pack) + $Maps)   # session 11 (fix11): IDLE1 60 s idle at every entry / Echo / bonfire
$checks += , @("devkit-check", "devkit-check", $pack)   # session 11 (devkit11): every ported item in a dev-kit lot (skips packs without --dev-kit)
$checks += , @("world-check", "world-check", $pack)       # session 11 (ret11): W1 world switch (first visit / return trip)
$checks += , @("event-sim return", "event-sim", "return", $pack) # session 11 (ret11): return-trip awards and fights
if ($Maps -contains "m19_41_00_00") { $checks += , @("event-sim burg-bosses", "event-sim", "burg-bosses", $pack) } # session 11 (burg11, M4): the Burg boss fights
if ($Maps -contains "m19_40_00_00") { $checks += , @("event-sim depths-bosses", "event-sim", "depths-bosses", $pack) } # session 11 (dchr11): the Gaping Dragon fight, the Slime wake (WK2)
if ($Maps -contains "m19_40_00_00") { $checks += , @("bgm-check m10_00", "bgm-check", "m10_00_00_00", "--pack", $pack) } # session 11 (dchr11): every Depths BGM reachable (0 pending)
$checks += , @("ds1net-check", "ds1net-check", $pack) # session 11 (enemy11): DS1 network rules G1-G3 (blends, layers, arrows)
$checks += , @("attack-travel-check", "attack-travel-check", $pack) # session 11 (leap12): DS1 attack travel = DS1 (T1/T2), DS1 JT 51 leaps covered by 280000 (rule LP1)
$checks += , @("armor-check", "armor-check", $pack)       # session 11 (equip11): DS1 armour items, rules AR1-AR7
$checks += , @("spell-check", "spell-check", $pack)       # session 11 (equip11): DS1 spells, rules SP1-SP8
$checks += , @("goods-check", "goods-check", $pack)       # session 11 (equip11): DS1 goods, rules GD1-GD6
$checks += , @("regulation-check", "regulation-check", $pack) # session 11 (equip11): enc_regulation decrypts (= Smithbox), parses, ids sorted
$checks += , @("loading-check", "loading-check", $pack)     # session 12 (load12): loading screens: DS1 items LD1/LD2, "EOL" logo line LD3

$known = @{}
if ($Baseline -and (Test-Path $Baseline)) {
    $cur = $null
    foreach ($l in Get-Content $Baseline) {
        if ($l -match '^=== (.+?) -> FAIL \(exit') { $cur = $Matches[1]; $known[$cur] = @() } elseif ($l -match '^=== .+ -> \w+ \(exit') { $cur = $null } elseif ($cur -and $l -match '\S') { $known[$cur] += $l }
    }
}
$failed = 0; $t0 = Get-Date
foreach ($c in $checks) {
    $name = $c[0]; $args2 = $c[1..($c.Count - 1)]
    if ($Only -and $name -notmatch $Only) { continue }
    $s = Get-Date
    $out = & $Lr @args2 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $secs = ((Get-Date) - $s).TotalSeconds
    $last = ($out | Where-Object { $_ -match '\S' } | Select-Object -Last 1)
    $status = if ($out -match '^unknown (command|event-sim mode)') { "SKIP" } elseif ($code -eq 0) { "PASS" }
              elseif ($known.ContainsKey($name) -and ($known[$name] | Select-Object -Last 1) -eq $last) { "KNOWN" } else { $failed++; "FAIL" }
    Add-Content $log "=== $name -> $status (exit $code, $([int]$secs) s)"
    Add-Content $log $out
    if ($last.Length -gt 110) { $last = $last.Substring(0, 110) }
    "{0,-4} {1,-26} {2}" -f $status, $name, $(if ($status -eq "PASS") { "" } else { $last })
}
"{0} failed; {1:N0} s; full output in {2}" -f $failed, ((Get-Date) - $t0).TotalSeconds, $log
exit ([int]($failed -gt 0))
