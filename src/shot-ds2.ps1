param([string]$Out)
# Saves a screenshot of DarkSoulsII's main window only (not the rest of the desktop) to $Out (PNG).
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@
[Win]::SetProcessDPIAware() | Out-Null
$p = Get-Process DarkSoulsII -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p -or $p.MainWindowHandle -eq 0) { "no DS2 window"; exit 1 }
$r = New-Object Win+RECT
[Win]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
if ($w -le 0 -or $h -le 0) { "window has no area ($w x $h)"; exit 1 }
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
# Session 11: DS2 behind other windows (run-ds2.ps1 -Background) can't be copied from the screen, so ask the window to
# render itself (PW_RENDERFULLCONTENT = 2, works for D3D windows through DWM); CopyFromScreen only when DS2 is in front.
$how = "PrintWindow"
if ([Win]::GetForegroundWindow() -eq $p.MainWindowHandle) { $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $how = "screen" }
else { $hdc = $g.GetHdc(); [Win]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc) }
$small = New-Object System.Drawing.Bitmap $bmp, ([int]($w / 2)), ([int]($h / 2))
$small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
# Mean brightness (0-255) over a 32x32 grid, so callers can tell a black frame from a rendered one without looking.
$sum = 0; $n = 0
for ($y = 0; $y -lt $small.Height; $y += [Math]::Max(1, [int]($small.Height / 32))) {
    for ($x = 0; $x -lt $small.Width; $x += [Math]::Max(1, [int]($small.Width / 32))) {
        $c = $small.GetPixel($x, $y); $sum += ($c.R + $c.G + $c.B) / 3; $n++
    }
}
"saved $Out ($how, $w x $h, title '$($p.MainWindowTitle)', brightness {0:N1})" -f ($sum / [Math]::Max(1, $n))
