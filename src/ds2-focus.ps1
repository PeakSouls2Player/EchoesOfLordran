param([int]$IdleMs = 4500, [int]$MaxWaitSeconds = 120)
# Brings the DS2 test window to the front (DS2 ignores input while it isn't the focused window). Allowed by the user in
# session 4. Waits until the user's real keyboard/mouse has been idle for $IdleMs so focus is never pulled away mid-typing.
# Uses AttachThreadInput with the current foreground thread, the usual way to pass the foreground lock.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Focus {
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static uint IdleMs() { var i = new LASTINPUTINFO(); i.cbSize = 8; GetLastInputInfo(ref i); return (uint)Environment.TickCount - i.dwTime; }
    public static bool Bring(IntPtr h) {
        IntPtr fg = GetForegroundWindow();
        if (fg == h) return true;
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
        AttachThreadInput(me, fgThread, true);
        if (IsIconic(h)) ShowWindow(h, 9);
        BringWindowToTop(h);
        bool ok = SetForegroundWindow(h);
        AttachThreadInput(me, fgThread, false);
        if (ok && GetForegroundWindow() == h && !IsIconic(h)) return true;
        // Windows' foreground lock (another app, e.g. Claude, took focus and DS2 minimized itself): a synthetic Alt tap
        // lets SetForegroundWindow through; then restore. Only runs after the user has been idle (see below).
        keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
        ok = SetForegroundWindow(h);
        ShowWindow(h, 9);
        System.Threading.Thread.Sleep(1500);
        return ok && !IsIconic(h);
    }
}
"@
$p = Get-Process DarkSoulsII -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p -or $p.MainWindowHandle -eq 0) { "no DS2 window"; exit 1 }
if ([Focus]::GetForegroundWindow() -eq $p.MainWindowHandle -and -not [Focus]::IsIconic($p.MainWindowHandle)) { "DS2 already in front"; exit 0 }
$t0 = Get-Date
while ([Focus]::IdleMs() -lt $IdleMs) {
    if (((Get-Date) - $t0).TotalSeconds -gt $MaxWaitSeconds) { "user still active after $MaxWaitSeconds s; not taking focus"; exit 2 }
    Start-Sleep -Milliseconds 250
}
if ([Focus]::Bring($p.MainWindowHandle)) {
    # The synthetic Alt tap in Bring counts as input for GetLastInputInfo, so user-active.ps1 (4 s window) would report the
    # user as active right after focusing (session 9: every focus-then-input stopped the runner). Wait it out.
    Start-Sleep -Milliseconds 4500
    "DS2 brought to front"; exit 0
}
"could not bring DS2 to front"
exit 1
