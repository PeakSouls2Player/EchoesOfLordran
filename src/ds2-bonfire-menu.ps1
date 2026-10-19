param([string]$Prefix = "menu", [int]$Rounds = 1, [int]$Tiles = 4, [string]$Route = "", [switch]$NoShots,
      [ValidateSet("auto", "on", "off")][string]$Pages = "auto",
      [string]$LogPath = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game\lordran\lordran.log",
      [switch]$SelfTest)
# Bonfire menu test (session 6, the bonfire-menu crash), screen-verified: every step reads the screen with
# ds2-menu-state.ps1 before and after pressing, so dropped key presses are retried instead of desyncing the run.
# Needs MP resting at (or standing at and facing) a bonfire. Per round, at the current bonfire: Travel (cursor over all
# area tiles and back, the last area's bonfire list open/close), Attune spells (OK dialog for MP: no spells),
# Burn (OK dialog: no ascetic), Item box, Leave (Back: MP stands up) and rest again.
# -Route "2,0,3" then travels to those area tiles in turn and runs $Rounds rounds at each arrival bonfire. Tile numbers
# are positions in the full (vanilla) area grid: MP's grid is 0 Majula, 1 Forest of Fallen Giants, 2 Firelink Shrine,
# 3 Northern Undead Asylum. Checks the arrival map by height (Majula ~6, Firelink ~-60, Asylum ~196) and that DS2 is
# alive after every step.
# Travel pages (loader travel.c, session 8; loader.ini travel_pages=1, the default): the grid shows one realm, Lordran
# (DS1 bonfires 40000..49999) or Drangleic, opening on the realm of the bonfire you rest at; Shift+Left/Right (menu
# LB/RB) switch. -Pages auto (default) reads the loader's "travel pages: on/off/DISABLED" line in lordran.log. With
# pages on, every Travel open reads the loader's "Travel opened ... -> page P, N shown" line, which lists the full grid
# in order with a realm tag per area ("10040000D 18010000L"): a round walks the N tiles of the opening page, opens the
# last one's bonfire list, switches page (checks the "LB/RB -> page" line), walks that page and switches back; a -Route
# tile becomes (page, index on that page), with a Shift+Right first when it is on the other page. MP's pages: Lordran
# 0 Firelink, 1 Asylum; Drangleic 0 Majula, 1 FoFG. So the vanilla routes ("3,2,0", "2,0,3") keep working unchanged.
# -SelfTest checks the log parsing and the route translation offline (no game needed) and exits.
# Exit: 0 all done, 1 DS2 gone (crash?), 2 stuck (screen not as expected), 3 user active, 4 done but a page check failed.
$s = $PSScriptRoot
if (-not (Test-Path "$s\ds2-do.ps1")) { $s = "C:\EchoesOfLordran\src" } # a sandbox copy uses the main tree's helpers
$shots = "C:\EchoesOfLordran\docs\shots"
$log = New-Object System.Collections.Generic.List[string]
$script:pageFails = 0
function Note($t) { $line = "{0:HH:mm:ss} {1}" -f (Get-Date), $t; $line; $log.Add($line); Add-Content (Join-Path $shots "$Prefix-log.txt") $line }
function Alive { if (-not (Get-Process DarkSoulsII -ErrorAction SilentlyContinue)) { Note "DS2 GONE (crash?) during: $args"; exit 1 } }
function Keys([string[]]$c) {
    & "$s\ds2-focus.ps1" | Out-Null
    $m = "k$(Get-Random)"
    $r = & "$s\ds2-do.ps1" -Commands ($c + "log $m") -WaitFor "AUTOPILOT $m" -TimeoutMs 60000
    if ($LASTEXITCODE -eq 3) { Note "user active: stopping"; exit 3 }
    Alive ($c -join ' ')
}
function State { Alive "state"; $st = & "$s\ds2-menu-state.ps1"; return "$st".Trim() }
$script:here = ""
function Shot($name) { if (-not $NoShots) { & "$s\shot-ds2.ps1" -Out (Join-Path $shots "$Prefix-$script:here$name.png") | Out-Null } }
function WaitState([string]$re, [int]$sec = 12) {
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $sec) { $st = State; if ($st -match "^($re)$") { return $st }; Start-Sleep -Milliseconds 700 }
    return (State)
}
# --- lordran.log: travel pages (session 8) ---
function LogSize { if (Test-Path $LogPath) { return (Get-Item $LogPath).Length } else { return 0 } }
function LogSince([long]$pos) {
    if (-not (Test-Path $LogPath)) { return @() }
    $fs = [System.IO.File]::Open($LogPath, 'Open', 'Read', 'ReadWrite')
    try {
        if ($fs.Length -le $pos) { return @() }   # nothing new (or the game restarted and the log is shorter)
        $fs.Position = $pos
        return @((New-Object System.IO.StreamReader($fs)).ReadToEnd() -split "`r?`n")
    } finally { $fs.Close() }
}
function PagesOn {
    if ($Pages -ne "auto") { return ($Pages -eq "on") }
    $l = LogSince 0 | Where-Object { $_ -match 'travel pages: (on \(|off|DISABLED)' } | Select-Object -Last 1
    return [bool]($l -and $l -match 'travel pages: on \(')
}
# The loader's open line -> @{ Page = 0 Drangleic / 1 Lordran / -1 unpaged; Shown; Realms = full grid, 0 D / 1 L }
function ParseOpen([string]$l) {
    $info = @{ Line = $l; Page = -1; Shown = 0; Realms = @() }
    if ($l -match '-> page (Lordran|Drangleic), (\d+) shown') { $info.Page = [int]($Matches[1] -eq 'Lordran'); $info.Shown = [int]$Matches[2] }
    $info.Realms = @([regex]::Matches($l, ' \d+([DL])(?= )') | ForEach-Object { [int]($_.Groups[1].Value -eq 'L') })
    return $info
}
function OpenInfo([long]$pos) {
    for ($i = 0; $i -lt 10; $i++) {
        $l = LogSince $pos | Where-Object { $_ -match 'travel pages: Travel opened at bonfire' } | Select-Object -Last 1
        if ($l) { return (ParseOpen $l) }
        Start-Sleep -Milliseconds 300
    }
    return $null
}
function PageName([int]$p) { if ($p -eq 1) { "Lordran" } else { "Drangleic" } }
# Full-grid tile -> @{ Flip = the tile is on the other page; Index = its position on its page }
function PageRoute($info, [int]$tile) {
    if (-not $info -or $info.Page -lt 0 -or $tile -ge $info.Realms.Count) { return @{ Flip = $false; Index = $tile } }
    $realm = $info.Realms[$tile]
    $index = @($info.Realms[0..$tile] | Where-Object { $_ -eq $realm }).Count - 1
    return @{ Flip = ($realm -ne $info.Page); Index = $index }
}
function CheckSwitch([long]$pos, [int]$to) {
    $l = LogSince $pos | Where-Object { $_ -match 'travel pages: LB/RB' } | Select-Object -Last 1
    if ($l -match "LB/RB -> page $(PageName $to)") { Note "  page switch -> $(PageName $to) (log ok)"; return }
    Note "  PAGE CHECK FAILED: expected 'LB/RB -> page $(PageName $to)', log: $(if ($l) { $l.Trim() } else { 'no LB/RB line' })"; $script:pageFails++
}
function ExpectedPage {
    $m = Map
    if ($m -eq "asylum" -or $m -eq "firelink") { return 1 } elseif ($m -eq "majula") { return 0 } else { return -1 }
}
if ($SelfTest) {
    $ok = 0; $bad = 0
    function T($what, $got, $want) { if ("$got" -eq "$want") { $script:ok++ } else { $script:bad++; "FAIL $what : got '$got', want '$want'" } }
    $i = ParseOpen "19:40:01.123 [ 4242] travel pages: Travel opened at bonfire 48150: 4 areas (2 Drangleic, 2 Lordran): 10040000D 10100000D 40100000L 18010000L -> page Lordran, 2 shown"
    T "page" $i.Page 1; T "shown" $i.Shown 2; T "realms" ($i.Realms -join ',') "0,0,1,1"
    $r = PageRoute $i 3; T "tile 3 (Asylum) from Lordran" "$($r.Flip),$($r.Index)" "False,1"
    $r = PageRoute $i 2; T "tile 2 (Firelink) from Lordran" "$($r.Flip),$($r.Index)" "False,0"
    $r = PageRoute $i 0; T "tile 0 (Majula) from Lordran" "$($r.Flip),$($r.Index)" "True,0"
    $r = PageRoute $i 1; T "tile 1 (FoFG) from Lordran" "$($r.Flip),$($r.Index)" "True,1"
    $j = ParseOpen "x travel pages: Travel opened at bonfire 4650: 4 areas (2 Drangleic, 2 Lordran): 10040000D 40100000L 10100000D 18010000L -> page Drangleic, 2 shown"
    $r = PageRoute $j 2; T "interleaved tile 2 from Drangleic" "$($r.Flip),$($r.Index)" "False,1"
    $r = PageRoute $j 3; T "interleaved tile 3 from Drangleic" "$($r.Flip),$($r.Index)" "True,1"
    $k = ParseOpen "x travel pages: Travel opened at bonfire 4650 with 0 areas; not paged"
    T "unpaged" $k.Page -1; $r = PageRoute $k 3; T "unpaged route" "$($r.Flip),$($r.Index)" "False,3"
    $r = PageRoute $null 2; T "no open line" "$($r.Flip),$($r.Index)" "False,2"
    $tmp = Join-Path $env:TEMP "ds2-bonfire-menu-selftest.log"
    Set-Content $tmp @("a Lordran loader v2.9", "b travel pages: on (exe base 00007FF7..., 23 code signatures and 3 vtable slots match)", "c travel pages: Travel opened at bonfire 40250: 3 areas (2 Drangleic, 1 Lordran): 10040000D 10100000D 40100000L -> page Lordran, 1 shown", "d travel pages: LB/RB -> page Drangleic, 2 areas") -Encoding UTF8
    $script:LogPath = $tmp
    $Pages = "auto"; T "auto on" (PagesOn) True
    $o = OpenInfo 0; T "OpenInfo from file" "$($o.Page),$($o.Shown),$($o.Realms -join '')" "1,1,001"
    T "LogSince past end" (@(LogSince 100000).Count) 0
    Set-Content $tmp @("travel pages: on (exe base 1)", "travel pages: Top input differs: ...", "travel pages: DISABLED (not the exe)") -Encoding UTF8
    T "auto, later DISABLED" (PagesOn) False
    Set-Content $tmp @("travel pages: off (loader.ini travel_pages=0)") -Encoding UTF8
    T "auto off" (PagesOn) False
    Set-Content $tmp @("Lordran loader v2.8 (no travel line)") -Encoding UTF8
    T "old loader" (PagesOn) False
    $Pages = "on"; T "forced on" (PagesOn) True
    Remove-Item $tmp
    "SELFTEST: $ok passed, $bad failed"
    exit ([int]($bad -gt 0))
}
function ToRest {
    for ($i = 0; $i -lt 10; $i++) {
        $st = State
        if ($st -like "rest:*") { return $st }
        switch -Regex ($st) {
            '^dialog$' { Keys @("key RETURN 160", "wait 1500") }
            '^(travel|itembox)$' { Keys @("key BACK 160", "wait 1500") }
            '^world$' { Keys @("key RETURN 160", "wait 4500") }   # rest (MP stands at the bonfire facing it)
            default { Start-Sleep -Seconds 2 }                    # loading / transition
        }
    }
    Note "STUCK: can't reach the rest menu (screen: $(State))"; Shot "stuck"; exit 2
}
function Cursor([int]$row) {
    for ($i = 0; $i -lt 8; $i++) {
        $st = ToRest
        $cur = [int]($st.Substring(5))
        if ($cur -eq $row) { return }
        $k = if ($cur -lt $row) { "DOWN" } else { "UP" }
        Keys @("key $k 160", "wait 700")
    }
    Note "STUCK: cursor won't reach row $row"; Shot "stuck"; exit 2
}
function Entry([int]$row, [string]$name, [string]$expect, [string]$close, [int]$r) {
    for ($try = 0; $try -lt 3; $try++) {
        Cursor $row
        Keys @("key RETURN 160", "wait 1500")
        $st = WaitState $expect
        if ($st -match "^($expect)$") {
            if ($r -eq 1) { Shot "$name" }
            Keys @("key $close 160", "wait 1500")
            $back = WaitState "rest:\d"
            Note "  $name -> $st, closed -> $back"
            return
        }
        Note "  ${name}: expected $expect, screen $st (retry)"
    }
    Note "STUCK: $name never opened"; Shot "stuck-$name"; exit 2
}
function Walk([string]$dir, [int]$n) {
    if ($n -le 1) { return }
    $c = @(); for ($i = 1; $i -lt $n; $i++) { $c += "key $dir 160"; $c += "wait 1100" }
    Keys $c
}
# Opens Travel from the rest menu; returns the page info (paged) or $null (vanilla), or "closed" if Travel didn't open.
function OpenTravel {
    Cursor 0
    $pos = LogSize
    Keys @("key RETURN 160", "wait 1500")
    if ((WaitState "travel") -ne "travel") { return "closed" }
    if (-not $script:paged) { return $null }
    $info = OpenInfo $pos
    if (-not $info -or $info.Page -lt 0) {
        Note "  PAGE CHECK FAILED: travel pages are on but no paged 'Travel opened' line$(if ($info) { ': ' + $info.Line.Trim() })" | Out-Host; $script:pageFails++
        return $null
    }
    $want = ExpectedPage
    $other = @($info.Realms | Where-Object { $_ -ne $info.Page }).Count
    $verdict = if ($want -lt 0 -or $want -eq $info.Page) { "ok" } else { $script:pageFails++; "PAGE CHECK FAILED: expected $(PageName $want) here" }
    Note "  travel opened on page $(PageName $info.Page): $($info.Shown) tiles, $other on the other page ($verdict)" | Out-Host # Note also returns its line: keep it out of the result
    return $info
}
function Round([int]$r) {
    $script:here = "$(Map)-"
    # Travel: grid, every tile, the last one's bonfire list; with pages also the other page
    $info = OpenTravel
    if ($info -is [string]) { Note "  travel didn't open"; return }
    $n = if ($info) { $info.Shown } else { $Tiles }
    Walk "RIGHT" $n
    if ($r -eq 1) { Shot "travel" }
    Keys @("key RETURN 160", "wait 1800")
    if ($r -eq 1) { Shot "travel-list" }
    Keys @("key BACK 160", "wait 1300")
    if ($info) {
        $other = @($info.Realms | Where-Object { $_ -ne $info.Page }).Count
        if ($other -gt 0) {
            $pos = LogSize
            Keys @("key LSHIFT,RIGHT 160", "wait 1300")      # RB: the other page, cursor on its cell 0
            CheckSwitch $pos (1 - $info.Page)
            Walk "RIGHT" $other
            if ($r -eq 1) { Shot "travel-page2" }
            Walk "LEFT" $other
            $pos = LogSize
            Keys @("key LSHIFT,LEFT 160", "wait 1300")       # LB: back to the opening page, cursor on cell 0
            CheckSwitch $pos $info.Page
        } else {
            Walk "LEFT" $n
        }
    } else {
        Walk "LEFT" $n
    }
    Keys @("key BACK 160", "wait 1500")
    Note "  travel grid + list -> $(WaitState 'rest:\d')"
    Entry 1 "attune" "dialog" "RETURN" $r
    Entry 2 "burn" "dialog" "RETURN" $r
    Entry 3 "itembox" "itembox" "BACK" $r
    # Leave (Back from the rest menu): MP stands up; then rest again
    ToRest | Out-Null
    Keys @("key BACK 160", "wait 2500")
    $st = WaitState "world"
    Note "  leave -> $st"
    if ($r -eq 1) { Shot "left" }
    ToRest | Out-Null
}
function Map {
    $p = & "$s\ds2-pos.ps1"
    if ($p -match '-> \S+: (-?[\d.]+) (-?[\d.]+) (-?[\d.]+)') { $y = [double]$Matches[2]; if ($y -gt 150) { return "asylum" } elseif ($y -lt -30) { return "firelink" } else { return "majula" } }
    return "?"
}
function TravelTo([int]$tile) {
    $want = @{ 0 = "majula"; 2 = "firelink"; 3 = "asylum" }[$tile]
    for ($try = 0; $try -lt 2; $try++) {
        $info = OpenTravel
        if ($info -is [string]) { continue }
        $route = PageRoute $info $tile
        $c = @()
        if ($route.Flip) { $c += "key LSHIFT,RIGHT 160"; $c += "wait 1300" }   # the tile is on the other page
        for ($i = 0; $i -lt $route.Index; $i++) { $c += "key RIGHT 160"; $c += "wait 1100" }
        if ($info) { Note "  tile $tile = $(PageName ($info.Page -bxor [int]$route.Flip)) page, cell $($route.Index)$(if ($route.Flip) { ' (switch first)' })" }
        Keys ($c + @("key RETURN 160", "wait 1800", "key RETURN 160", "wait 4000"))
        # loading, then the world again
        $t0 = Get-Date
        while (((Get-Date) - $t0).TotalSeconds -lt 120) { Start-Sleep -Seconds 3; if ((State) -eq "world") { break } }
        Start-Sleep -Seconds 4
        $m = Map
        Note "travel to tile $tile -> $m"
        Shot "arrived-$m"
        ToRest | Out-Null
        if (-not $want -or $m -eq $want) { return }
    }
    Note "travel to tile $tile ended at $(Map)"
}
$script:paged = PagesOn
Note "start at $(Map), screen $(State), travel pages $(if ($script:paged) { 'on' } else { 'off' }) (-Pages $Pages)"
ToRest | Out-Null
for ($r = 1; $r -le $Rounds; $r++) { Note "round $r at $(Map)"; Round $r }
if ($Route) {
    $hop = 0
    foreach ($t in ($Route -split ',')) {
        $hop++
        TravelTo ([int]$t)
        for ($r = 1; $r -le $Rounds; $r++) { Note "round $r at $(Map) (hop $hop)"; Round ($(if ($hop -le 3) { $r } else { 2 })) }
    }
}
if ($script:pageFails -gt 0) { Note "DONE, DS2 alive, but $script:pageFails page check(s) FAILED"; exit 4 }
Note "ALL DONE, DS2 alive"
exit 0
