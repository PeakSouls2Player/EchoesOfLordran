param([string]$Image = "", [switch]$Debug)
# Classifies the DS2 screen for the bonfire-menu tests (session 6) from a screenshot (default: a fresh one of the test
# window, 1280x720 as saved by shot-ds2.ps1). Prints one of:
#   rest:N    the bonfire rest menu, row N highlighted (0 Travel, 1 Attune spells, 2 Burn, 3 Item box)
#   dialog    an OK message box ("No spells", "You have no items to burn")
#   travel    the Travel area grid or a bonfire list   itembox   the Item box
#   world     in the world (HUD health bar)            other     anything else (loading screen, other menus)
# Regions and colours measured on session-6 screenshots (docs\shots\s6-2-burn, s6-3-*); the menus are opaque UI.
Add-Type -AssemblyName System.Drawing
if (-not $Image) {
    $Image = Join-Path $env:TEMP "ds2-menu-state.png"
    & "$PSScriptRoot\shot-ds2.ps1" -Out $Image | Out-Null
}
$bmp = [System.Drawing.Bitmap]::FromFile($Image)
try {
    if ($bmp.Width -ne 1280) { "other"; exit 0 }
    function Mean($x0, $x1, $y0, $y1) {
        $r = 0; $g = 0; $b = 0; $n = 0
        for ($y = $y0; $y -lt $y1; $y += 2) { for ($x = $x0; $x -lt $x1; $x += 3) { $c = $bmp.GetPixel($x, $y); $r += $c.R; $g += $c.G; $b += $c.B; $n++ } }
        return @(($r / $n), ($g / $n), ($b / $n))
    }
    function Near($m, $r, $g, $b, $tol) { [Math]::Abs($m[0] - $r) -le $tol -and [Math]::Abs($m[1] - $g) -le $tol -and [Math]::Abs($m[2] - $b) -le $tol }
    $ok = Mean 560 730 576 590      # blue OK button: (44, 80, 102)
    $box = Mean 420 880 470 500     # the dialog box behind the text: (17, 11, 8)
    $panel = Mean 130 440 340 500   # the rest menu's empty parchment rows: (35, 31, 24)
    $hdr = Mean 230 600 118 132     # Travel / Item box header bar, blue-grey: travel (54, 63, 68), item box (27, 41, 49)
    $hdrR = Mean 1060 1150 118 132  # right end of the header: Item box only (23, 34, 40)
    $hp = Mean 200 380 72 82        # HUD health bar: (101, 57, 57)
    if ($Debug) { "ok $ok | box $box | panel $panel | hdr $hdr | hdrR $hdrR | hp $hp" }
    if ((Near $ok 44 80 102 14) -and (Near $box 17 11 8 10)) { "dialog"; exit 0 }
    if ($hdr[2] -gt $hdr[0] + 8) {
        if ($hdrR[2] -gt $hdrR[0] + 8 -and $hdrR[2] -lt 60) { "itembox" } else { "travel" }
        exit 0
    }
    if (Near $panel 35 31 24 6) {
        $best = -1; $bestV = -1e9; $i = 0
        foreach ($y in 228, 256, 283, 311) {   # the highlighted row has an orange left edge: (162, 93, 37) vs (51, 42, 30)
            $m = Mean 122 128 ($y - 6) ($y + 6)
            if ($Debug) { "  row $i edge $m" }
            if ($m[0] - $m[2] -gt $bestV) { $bestV = $m[0] - $m[2]; $best = $i }
            $i++
        }
        if ($bestV -gt 45) { "rest:$best"; exit 0 }
    }
    if ($hp[0] -gt 85 -and $hp[0] -gt $hp[1] + 30) { "world"; exit 0 }
    "other"
} finally { $bmp.Dispose() }
