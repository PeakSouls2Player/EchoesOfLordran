param([int]$IdleMs = 4000)
# Guard for autopilot tests: reports how long the user's real keyboard/mouse has been idle (GetLastInputInfo; our
# autopilot injects through DirectInput hooks, so it never shows up here) and which process owns the foreground window.
# Exit 1 = the user gave input within $IdleMs while DS2 was in front (they may be playing the test instance): stop
# sending inputs and ask. Exit 0 otherwise.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UserIdle {
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public static uint IdleMs() { var i = new LASTINPUTINFO(); i.cbSize = 8; GetLastInputInfo(ref i); return (uint)Environment.TickCount - i.dwTime; }
}
"@
$idle = [UserIdle]::IdleMs()
$fgPid = [uint32]0
[UserIdle]::GetWindowThreadProcessId([UserIdle]::GetForegroundWindow(), [ref]$fgPid) | Out-Null
$fg = (Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName
"user idle {0} ms, foreground: {1}" -f $idle, $fg
if ($fg -eq 'DarkSoulsII' -and $idle -lt $IdleMs) { "USER ACTIVE IN DS2"; exit 1 }
exit 0
