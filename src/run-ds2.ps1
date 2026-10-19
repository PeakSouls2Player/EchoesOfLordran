param([string]$Label = "run", [int]$Seconds = 20, [switch]$Seamless, [string]$StopFile = "", [string]$TestSave = "", [switch]$Background)
# -Background (session 11): the test instance runs windowed (1280x720) behind the user's apps. GraphicsConfig_SOFS.xml is
# switched to FullScreen OFF after the save snapshot (Restore-Saves puts the user's config back), and focus is handed back
# to whatever window was in front at launch. The autopilot's keys reach an unfocused DS2 (autopilot.c marks the window
# active while a virtual key is held) and shot-ds2.ps1 captures the window with PrintWindow, so nothing needs the screen.
# Launches DS2 (offline) for a boot test, waits $Seconds, then closes only the instance it started.
# -Seamless starts it through Seamless Co-op's ds2sc_launcher.exe instead of Steam.
# -StopFile PATH ends the run early as soon as PATH exists (for long interactive autopilot sessions).
# Saves are protected: the save folder is snapshotted before launch, and anything the test instance changed is put back
# afterwards (a keypress reaching the window can start the game, and closing it mid-save could corrupt the save).
# -TestSave SL2 plays a test copy of the save (e.g. made with lr.cmd save-bonfire): it replaces the live
# DS2SOFS0000.sl2 only after the snapshot, so the user's real save is put back when the run ends.
$game = "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin\Game"
$saves = "$env:APPDATA\DarkSoulsII"
$backups = Join-Path (Split-Path $PSScriptRoot -Parent) "saves-backup"
if (Get-Process DarkSoulsII -ErrorAction SilentlyContinue) { "[$Label] SKIPPED: DS2 is already running (not ours)"; exit 2 }

$snapshot = Join-Path $backups "run-ds2-snapshot"
if (Test-Path $snapshot) { Remove-Item -Recurse -Force $snapshot }
Copy-Item $saves $snapshot -Recurse
$before = @{}; Get-ChildItem $snapshot -Recurse -File | ForEach-Object { $before[$_.FullName.Substring($snapshot.Length)] = (Get-FileHash $_.FullName).Hash }

function Restore-Saves {
    $changed = @()
    Get-ChildItem $saves -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($saves.Length)
        if (-not $before.ContainsKey($rel)) {
            $aside = Join-Path $backups ("run-ds2-new-files-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
            New-Item -ItemType Directory -Force (Split-Path (Join-Path $aside $rel) -Parent) | Out-Null
            Move-Item $_.FullName (Join-Path $aside $rel) -Force; $changed += "$rel (new; moved to $aside)"
        } elseif ((Get-FileHash $_.FullName).Hash -ne $before[$rel]) {
            Copy-Item (Join-Path $snapshot $rel) $_.FullName -Force; $changed += "$rel (restored)"
        }
    }
    if ($changed) { "[$Label] WARNING: the test instance touched saves; put back: " + ($changed -join ", ") }
}

if ($TestSave) {
    $live = Get-ChildItem $saves -Recurse -Filter DS2SOFS0000.sl2 | Select-Object -First 1
    if (-not $live -or -not (Test-Path $TestSave)) { "[$Label] test save or live save missing"; exit 4 }
    Copy-Item $TestSave $live.FullName -Force
    "[$Label] playing test save $TestSave (the real save is restored afterwards)"
}
$userWindow = [IntPtr]::Zero
if ($Background) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Bg {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
"@
    $cfg = Join-Path $saves "GraphicsConfig_SOFS.xml"
    if (Test-Path $cfg) {
        $xml = [IO.File]::ReadAllText($cfg, [Text.Encoding]::Unicode)
        $xml = $xml -replace '<FullScreen>ON</FullScreen>', '<FullScreen>OFF</FullScreen>'
        $xml = $xml -replace '<Resolution-WindowScreenWidth>\d+<', '<Resolution-WindowScreenWidth>1280<' -replace '<Resolution-WindowScreenHeight>\d+<', '<Resolution-WindowScreenHeight>720<'
        [IO.File]::WriteAllText($cfg, $xml, [Text.Encoding]::Unicode)
        "[$Label] background: windowed 1280x720 for this run (the user's GraphicsConfig is restored afterwards)"
    }
    $userWindow = [Bg]::GetForegroundWindow()
}
$t0 = Get-Date
$launcher = $null
if ($Seamless) { $launcher = Start-Process "$game\ds2sc_launcher.exe" -WorkingDirectory $game -PassThru }
else { Start-Process "steam://rungameid/335300" }
$proc = $null
for ($i = 0; $i -lt 60 -and -not $proc; $i++) { Start-Sleep -Milliseconds 500; $proc = Get-Process DarkSoulsII -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $t0.AddSeconds(-1) } }
if (-not $proc) { "[$Label] DS2 did not start"; if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }; Restore-Saves; exit 3 }
if ($Background) {
    # hand focus back once the window exists, and keep DS2 at the bottom of the z-order (not minimized: it must render)
    for ($i = 0; $i -lt 60 -and $proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 500; $proc.Refresh() }
    Start-Sleep -Seconds 2
    if ($proc.MainWindowHandle -ne 0) { [Bg]::SetWindowPos($proc.MainWindowHandle, [IntPtr]1, 0, 0, 0, 0, 0x13) | Out-Null } # HWND_BOTTOM, NOSIZE|NOMOVE|NOACTIVATE
    if ($userWindow -ne [IntPtr]::Zero) { [Bg]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [Bg]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero); [Bg]::SetForegroundWindow($userWindow) | Out-Null }
}
$alive = $true
if ($StopFile -and (Test-Path $StopFile)) { Remove-Item $StopFile -Force }
for ($i = 0; $i -lt ($Seconds * 2); $i++) {
    Start-Sleep -Milliseconds 500
    if ($proc.HasExited) { $alive = $false; break }
    if ($StopFile -and (Test-Path $StopFile)) { Remove-Item $StopFile -Force; "[$Label] stop file seen after {0:N0}s" -f ((Get-Date) - $t0).TotalSeconds; break }
}
$seamlessLoaded = if (-not $proc.HasExited) { [bool]($proc.Modules | Where-Object { $_.ModuleName -eq 'ds2sc.dll' }) } else { $null }
$crash = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000; StartTime=$t0} -ErrorAction SilentlyContinue | Where-Object { $_.Message -match 'DarkSoulsII' } | Select-Object -First 1
$off = if ($crash -and $crash.Message -match 'Fault offset: (0x[0-9a-fA-F]+)') { $Matches[1] } else { '-' }
if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force }
if ($alive) { Stop-Process -Id $proc.Id -Force }
$proc.WaitForExit(15000) | Out-Null
Restore-Saves
if ($alive) { "[$Label] OK: still running after {0:N0}s (ds2sc.dll loaded: $seamlessLoaded; closed our instance)" -f ((Get-Date) - $t0).TotalSeconds; exit 0 }
"[$Label] CRASH after {0:N1}s, offset {1}" -f ((Get-Date) - $t0).TotalSeconds, $off; exit 1
