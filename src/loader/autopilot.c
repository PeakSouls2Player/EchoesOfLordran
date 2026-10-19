// Autopilot for automated in-game tests: virtual input driven by commands that a test script appends to
// <game>\lordran\control.txt (src\ds2-ctl.ps1). Enabled by autopilot=1 in loader.ini.
//  - Keyboard: DS2 reads it with IDirectInputDevice8::GetDeviceState(256, buf) on a device made through our dinput8
//    proxy, so we hook CreateDevice/GetDeviceState and OR our keys into the state. Inputs reach only DS2.
//  - Pad: xinput1_3!XInputGetState is hooked too, but DS2 only polls pads it found through DirectInput enumeration,
//    so a virtual pad alone is never adopted (kept for machines with a real pad attached).
//
// Commands (one per line, executed in order):
//   key KEYS MS          hold keyboard keys (comma-separated DIK names: W A S D E Q RETURN ESCAPE SPACE UP DOWN LEFT RIGHT
//                        LSHIFT LCONTROL TAB BACK F1..F4, letters, digits) for MS ms
//   press BUTTONS MS     hold pad buttons (comma-separated: A B X Y START BACK LB RB LT RT LS RS UP DOWN LEFT RIGHT) for MS ms
//   stick L|R X Y MS     hold a stick at X,Y (-1..1) for MS ms
//   wait MS              do nothing for MS ms
//   log TEXT             write TEXT to lordran.log (a marker for the test script)
//   peek RVA[,off...] [N] follow a pointer chain from the exe base, log N floats
//   scan X Y Z R [MAX]   log float triples within R of (X,Y,Z) in the game's private read-write memory
//   read ADDR [N]        log N floats at an absolute address
//   readx ADDR[,off..] [N] follow a pointer chain from an absolute address, log N u32 words in hex
//   trace RVA [MAX]      log the first MAX calls of exe+RVA with rcx/rdx/r8/r9 and the caller (trace.c)
//   goto X Z [TOL] [TIMEOUT_MS] [WALK_RADIUS]  walk the player to (X, Z) (closed loop on the position; logs GOTO ...)
//   givelot LOT          give an ItemLotParam2_Other lot with the pickup popup (game thread, entry.c)
//   warp MAP LOCATION    warp to an EventLocation of a map (decimal AABBCCDD), via the game thread (entry.c)
//   path X1 Z1 X2 Z2 ...  walk through waypoints (from lr.cmd navpath); logs "GOTO reached" / "GOTO failed at"
// A real pad keeps working: its state is merged with the virtual one.
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#include <math.h>
#include <stdio.h>
#include <stdlib.h>

typedef struct XPad {
    WORD buttons;
    BYTE lt, rt;
    SHORT lx, ly, rx, ry;
} XPad;

typedef struct XState {
    DWORD packet;
    XPad pad;
} XState;

typedef DWORD(WINAPI *XInputGetStateFn)(DWORD, XState *);
static XInputGetStateFn orig_XInputGetState;

static CRITICAL_SECTION pad_lock;
static XPad virtual_pad;
static DWORD packet = 1;
static wchar_t control_path[MAX_PATH];
static volatile LONG installed;
static volatile LONG calls[4], last_result = -1;
static BYTE virtual_keys[256];
static void *keyboard_device;
static volatile LONG key_reads;

static DWORD WINAPI hk_XInputGetState(DWORD index, XState *state)
{
    DWORD r = orig_XInputGetState(index, state);
    if (index < 4)
        InterlockedIncrement(&calls[index]);
    if (index != 0)
        return r;
    last_result = (LONG)r;
    EnterCriticalSection(&pad_lock);
    XPad v = virtual_pad;
    DWORD p = packet;
    LeaveCriticalSection(&pad_lock);
    int active = v.buttons || v.lt || v.rt || v.lx || v.ly || v.rx || v.ry;
    if (!active)
        return r; // no pad command running: the game sees the real pads only
    if (r != ERROR_SUCCESS) {
        memset(state, 0, sizeof *state);
        state->pad = v;
        state->packet = p;
        return ERROR_SUCCESS;
    }
    state->pad.buttons |= v.buttons;
    if (v.lt) state->pad.lt = v.lt;
    if (v.rt) state->pad.rt = v.rt;
    if (v.lx || v.ly) { state->pad.lx = v.lx; state->pad.ly = v.ly; }
    if (v.rx || v.ry) { state->pad.rx = v.rx; state->pad.ry = v.ry; }
    state->packet += p;
    return ERROR_SUCCESS;
}

static void set_pad(const XPad *pad)
{
    EnterCriticalSection(&pad_lock);
    virtual_pad = *pad;
    packet++;
    LeaveCriticalSection(&pad_lock);
}

static WORD button_bit(const char *name)
{
    static const struct { const char *name; WORD bit; } map[] = {
        {"UP", 0x0001}, {"DOWN", 0x0002}, {"LEFT", 0x0004}, {"RIGHT", 0x0008}, {"START", 0x0010}, {"BACK", 0x0020},
        {"LS", 0x0040}, {"RS", 0x0080}, {"LB", 0x0100}, {"RB", 0x0200}, {"A", 0x1000}, {"B", 0x2000}, {"X", 0x4000}, {"Y", 0x8000},
    };
    for (size_t i = 0; i < sizeof map / sizeof map[0]; i++)
        if (!_stricmp(name, map[i].name))
            return map[i].bit;
    return 0;
}

static int dik(const char *name)
{
    static const struct { const char *name; int code; } map[] = {
        {"ESCAPE", 0x01}, {"1", 0x02}, {"2", 0x03}, {"3", 0x04}, {"4", 0x05}, {"5", 0x06}, {"6", 0x07}, {"7", 0x08}, {"8", 0x09},
        {"9", 0x0A}, {"0", 0x0B}, {"BACK", 0x0E}, {"TAB", 0x0F}, {"Q", 0x10}, {"W", 0x11}, {"E", 0x12}, {"R", 0x13}, {"T", 0x14},
        {"Y", 0x15}, {"U", 0x16}, {"I", 0x17}, {"O", 0x18}, {"P", 0x19}, {"RETURN", 0x1C}, {"LCONTROL", 0x1D}, {"A", 0x1E},
        {"S", 0x1F}, {"D", 0x20}, {"F", 0x21}, {"G", 0x22}, {"H", 0x23}, {"J", 0x24}, {"K", 0x25}, {"L", 0x26}, {"LSHIFT", 0x2A},
        {"Z", 0x2C}, {"X", 0x2D}, {"C", 0x2E}, {"V", 0x2F}, {"B", 0x30}, {"N", 0x31}, {"M", 0x32}, {"LALT", 0x38}, {"SPACE", 0x39},
        {"F1", 0x3B}, {"F2", 0x3C}, {"F3", 0x3D}, {"F4", 0x3E}, {"UP", 0xC8}, {"LEFT", 0xCB}, {"RIGHT", 0xCD}, {"DOWN", 0xD0},
    };
    for (size_t i = 0; i < sizeof map / sizeof map[0]; i++)
        if (!_stricmp(name, map[i].name))
            return map[i].code;
    return -1;
}

static void set_keys(const BYTE *keys)
{
    EnterCriticalSection(&pad_lock);
    memcpy(virtual_keys, keys, sizeof virtual_keys);
    LeaveCriticalSection(&pad_lock);
}

// ---- DirectInput keyboard: IDirectInput8W::CreateDevice (slot 3), IDirectInputDevice8W::GetDeviceState (slot 9)
typedef HRESULT(WINAPI *CreateDeviceFn)(void *, const GUID *, void **, void *);
typedef HRESULT(WINAPI *GetDeviceStateFn)(void *, DWORD, void *);
static CreateDeviceFn orig_CreateDevice;
static GetDeviceStateFn orig_GetDeviceState;
static const GUID guid_sys_keyboard = {0x6F1D2B61, 0xD5A0, 0x11CF, {0xBF, 0xC7, 0x44, 0x45, 0x53, 0x54, 0x00, 0x00}};

// The input manager (DAT_141875278) keeps "window active" at +0x16d, set only from WM_ACTIVATE. While DS2 runs windowed
// behind another app, it is 0 and DirectInput can't acquire the keyboard, so the game drops the device every poll and
// re-creates it once a second (test keys get lost). While a virtual key is held we report success and mark the window
// active, so the keys reach DS2 without taking focus from the user's app; real keys still can't reach an unfocused DS2.
#define RVA_INPUT_MANAGER 0x1875278

static void mark_window_active(void)
{
    uintptr_t *slot = (uintptr_t *)(lr_base + RVA_INPUT_MANAGER);
    uintptr_t mgr = *slot;
    if (mgr && !IsBadWritePtr((void *)(mgr + 0x16d), 1))
        *(volatile BYTE *)(mgr + 0x16d) = 1;
}

static HRESULT WINAPI hk_GetDeviceState(void *self, DWORD size, void *data)
{
    HRESULT r = orig_GetDeviceState(self, size, data);
    if (self != keyboard_device || size != 256 || !data)
        return r;
    lr_entry_tick(); // the game's input poll: a per-frame hook on the game thread (entry.c), autopilot or not
    lr_devkit_tick(); // the Lordran Dev Kit (devkit.c, session 11; only with a --dev-kit pack's lordran/devkit.ini)
    lr_mapcap_tick(); // raises the 42-map WorldMapList limit once, on the game thread, before any map loads (mapcap.c, session 11)
    lr_travel_tick(); // installs the Travel page hooks once, on the game thread (travel.c, session 8)
    lr_gesture_tick(); // installs the gesture page hooks once, on the game thread (gesture.c, session 11)
    if (!lr_config.autopilot)
        return r;
    if (FAILED(r)) {
        // We hide failures below, so the game never drops and re-creates the device; re-acquire here instead
        // (IDirectInputDevice8::Acquire, slot 7: succeeds once the window is in front again).
        typedef HRESULT(WINAPI *AcquireFn)(void *);
        if (SUCCEEDED(((AcquireFn)(*(void ***)self)[7])(self)))
            r = orig_GetDeviceState(self, size, data);
    }
    InterlockedIncrement(&key_reads);
    BYTE keys[256];
    EnterCriticalSection(&pad_lock);
    memcpy(keys, virtual_keys, sizeof keys);
    LeaveCriticalSection(&pad_lock);
    int any = 0;
    for (int i = 0; i < 256; i++)
        any |= keys[i];
    if (FAILED(r))
        memset(data, 0, 256); // not acquired (window inactive): an empty state keeps the device alive for our next key
    // Session 11 (run s11-b1): marking the window active only while a key is held was too late for DS2's menu dialogs (the
    // game re-created the keyboard every second and the dialog never saw RETURN behind the user's apps). A test instance
    // (autopilot=1) is always treated as active; a real key can still only reach a focused window.
    mark_window_active();
    if (!any)
        return S_OK;
    BYTE *out = (BYTE *)data;
    for (int i = 0; i < 256; i++)
        out[i] |= keys[i];
    return S_OK;
}

static HRESULT WINAPI hk_CreateDevice(void *self, const GUID *guid, void **device, void *outer)
{
    HRESULT r = orig_CreateDevice(self, guid, device, outer);
    if (SUCCEEDED(r) && guid && device && *device && !memcmp(guid, &guid_sys_keyboard, sizeof(GUID))) {
        keyboard_device = *device;
        if (!orig_GetDeviceState) {
            void *target = (*(void ***)*device)[9];
            if (MH_CreateHook(target, (void *)hk_GetDeviceState, (void **)&orig_GetDeviceState) == MH_OK && MH_EnableHook(target) == MH_OK)
                lr_log(L"autopilot: keyboard device %p, GetDeviceState hooked", *device);
            else
                lr_log(L"autopilot: hooking GetDeviceState failed");
        } else if (lr_config.autopilot)
            lr_log(L"autopilot: keyboard device re-created: %p", *device); // without autopilot DS2 re-creates it every second while unfocused
    }
    return r;
}

// Called from the DirectInput8Create proxy with the game's new IDirectInput8W.
void lr_autopilot_dinput(void *dinput)
{
    if (orig_CreateDevice || !dinput)
        return;
    MH_STATUS s = MH_Initialize();
    if (s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED)
        return;
    void *target = (*(void ***)dinput)[3];
    if (MH_CreateHook(target, (void *)hk_CreateDevice, (void **)&orig_CreateDevice) == MH_OK && MH_EnableHook(target) == MH_OK)
        lr_log(L"autopilot: IDirectInput8::CreateDevice hooked");
    else
        lr_log(L"autopilot: hooking CreateDevice failed");
}

static SHORT axis(double v)
{
    if (v > 1) v = 1;
    if (v < -1) v = -1;
    return (SHORT)(v * 32767);
}

// ---- goto / path: closed-loop walking (session 5)
// The player position is PlayerCtrl+0x90 with PlayerCtrl = [[exe+0x16148F0]+0xD0] (docs/ds2-re-notes.md, "Player").
// Movement keys are camera-relative and the camera yaw isn't read from memory, so it is estimated from how the player
// actually moves: a short probe press (W, else S, A or D when blocked) gives the camera's forward direction; afterwards
// every full-speed step whose keys were held for a while refines it (the camera turns as the player turns). D is always
// 90 degrees clockwise of W seen from above (measured in the open in session 5: with W at atan2(z,x) = -45 deg, D moved
// at -135 deg), so the handedness is fixed. Waypoints for "path" come from the navmesh: lr.cmd navpath MAP X1 Y1 Z1 X2 Y2 Z2.
#define RVA_GAME_MANAGER 0x16148F0
#define GOTO_TICK_MS 40
#define SECTOR_RAD 0.785398
#define SIDE (-1.0) // positive world angle (atan2(z, x)) is towards A

static int player_pos(float out[3])
{
    uintptr_t gm = *(uintptr_t *)(lr_base + RVA_GAME_MANAGER);
    if (!gm || IsBadReadPtr((void *)(gm + 0xD0), 8))
        return 0;
    uintptr_t pc = *(uintptr_t *)(gm + 0xD0);
    if (!pc || IsBadReadPtr((void *)(pc + 0x90), 12))
        return 0;
    memcpy(out, (void *)(pc + 0x90), 12);
    return out[0] != 0 || out[1] != 0 || out[2] != 0;
}

static double wrap_angle(double a)
{
    while (a > 3.14159265358979) a -= 2 * 3.14159265358979;
    while (a < -3.14159265358979) a += 2 * 3.14159265358979;
    return a;
}

// Holds the movement keys of sector k (0 = W, then 45-degree steps towards D: W+D, D, S+D, S, S+A, A, W+A); walk adds LALT.
static void hold_sector(int k, int walk)
{
    static const char *const combos[8][2] = {
        {"W", NULL}, {"W", "D"}, {"D", NULL}, {"S", "D"}, {"S", NULL}, {"S", "A"}, {"A", NULL}, {"W", "A"},
    };
    BYTE keys[256] = {0};
    if (k >= 0) {
        for (int i = 0; i < 2; i++)
            if (combos[k][i])
                keys[dik(combos[k][i])] = 0x80;
        if (walk)
            keys[dik("LALT")] = 0x80;
    }
    set_keys(keys);
}

// Holds sector k for ms; returns the xz displacement in *mx/*mz (0 if the position is unreadable).
static int probe_move(int k, DWORD ms, float *mx, float *mz)
{
    float a[3], b[3];
    if (!player_pos(a))
        return 0;
    hold_sector(k, 0);
    Sleep(ms);
    hold_sector(-1, 0);
    Sleep(300); // let the run stop
    if (!player_pos(b))
        return 0;
    *mx = b[0] - a[0];
    *mz = b[2] - a[2];
    return 1;
}

typedef struct Nav {
    double cam;      // camera forward, world angle atan2(z, x)
    double max_tick; // fastest displacement seen in one tick (full running speed)
} Nav;

// Camera forward from the first probe that moves freely (W, then S, A, D).
static int nav_calibrate(Nav *n)
{
    static const int order[4] = {0, 4, 6, 2};
    for (int i = 0; i < 4; i++) {
        float mx = 0, mz = 0;
        if (!probe_move(order[i], 230, &mx, &mz))
            return 0;
        if (hypot(mx, mz) >= 0.35) {
            n->cam = wrap_angle(atan2(mz, mx) - SIDE * order[i] * SECTOR_RAD);
            lr_log(L"GOTO calibrated with sector %d (%.2f m): camera forward %.0f deg", order[i], hypot(mx, mz), n->cam * 57.2958);
            return 1;
        }
        lr_log(L"GOTO probe sector %d blocked (%.2f m)", order[i], hypot(mx, mz));
    }
    return 0;
}

// Walks to (tx, tz); 1 = reached. Logs GOTO lines.
static int nav_walk(Nav *n, double tx, double tz, double tol, int timeout, double walk_radius)
{
    float p[3], prev[3];
    if (!player_pos(p)) { lr_log(L"GOTO no player"); return 0; }
    lr_log(L"GOTO leg (%.2f, %.2f, %.2f) -> (%.2f, %.2f), %.2f m", p[0], p[1], p[2], tx, tz, hypot(tx - p[0], tz - p[2]));
    DWORD t0 = GetTickCount(), best_at = t0;
    int sector = -1, held = 0, slow = 0, recoveries = 0;
    double best = 1e9;
    memcpy(prev, p, sizeof prev);
    for (;;) {
        if (!player_pos(p)) { hold_sector(-1, 0); lr_log(L"GOTO lost the player"); return 0; }
        double dx = tx - p[0], dz = tz - p[2], dist = hypot(dx, dz);
        if (dist < tol) {
            lr_log(L"GOTO at (%.3f, %.3f, %.3f) in %lu ms", p[0], p[1], p[2], GetTickCount() - t0);
            return 1;
        }
        if (dist < best - 0.25) { best = dist; best_at = GetTickCount(); }
        if (GetTickCount() - best_at > 2000) {
            if (++recoveries > 3) {
                hold_sector(-1, 0);
                lr_log(L"GOTO stuck at (%.3f, %.3f, %.3f), %.2f m to go", p[0], p[1], p[2], dist);
                return 0;
            }
            // side-step (alternating), then re-measure the camera
            float mx, mz;
            lr_log(L"GOTO no progress at (%.3f, %.3f, %.3f): side-step %d", p[0], p[1], p[2], recoveries);
            probe_move(recoveries % 2 ? 2 : 6, 400, &mx, &mz);
            if (!nav_calibrate(n)) { lr_log(L"GOTO can't move"); return 0; }
            best = 1e9; best_at = GetTickCount(); sector = -1;
            player_pos(prev);
            continue;
        }
        if ((int)(GetTickCount() - t0) > timeout) {
            hold_sector(-1, 0);
            lr_log(L"GOTO timeout at (%.3f, %.3f, %.3f), %.2f m to go", p[0], p[1], p[2], dist);
            return 0;
        }
        // Refine the camera estimate from the last tick: same keys held for 3+ ticks and moving at (near) full speed.
        double mxx = p[0] - prev[0], mzz = p[2] - prev[2], step = hypot(mxx, mzz);
        if (step > n->max_tick && step < 1.0) n->max_tick = step;
        if (sector >= 0 && held >= 3 && !slow && step > 0.7 * n->max_tick && step > 0.05) {
            double est = wrap_angle(atan2(mzz, mxx) - SIDE * sector * SECTOR_RAD);
            n->cam = wrap_angle(n->cam + 0.5 * wrap_angle(est - n->cam));
        }
        memcpy(prev, p, sizeof prev);
        double rel = wrap_angle(atan2(dz, dx) - n->cam) * SIDE; // positive = towards D
        int k = (int)floor(rel / SECTOR_RAD + 0.5);
        k = ((k % 8) + 8) % 8;
        int walk = dist < walk_radius;
        if (k != sector || walk != slow) { sector = k; slow = walk; held = 0; hold_sector(k, walk); }
        else held++;
        Sleep(GOTO_TICK_MS);
    }
}

static void nav_stop_log(const wchar_t *what, DWORD t0)
{
    float p[3] = {0};
    hold_sector(-1, 0);
    Sleep(350);
    player_pos(p);
    lr_log(L"GOTO %ls (%.3f, %.3f, %.3f) after %lu ms", what, p[0], p[1], p[2], GetTickCount() - t0);
}

// goto X Z [TOL] [TIMEOUT_MS] [WALK_RADIUS]: walks the player to (X, Z); logs "GOTO reached"/"stuck"/"timeout".
static void run_goto(const char *line)
{
    double tx = 0, tz = 0, tol = 0.5, walk_radius = 1.5;
    int timeout = 30000;
    if (sscanf(line, "%*s %lf %lf %lf %d %lf", &tx, &tz, &tol, &timeout, &walk_radius) < 2) {
        lr_log(L"GOTO usage: goto X Z [TOL] [TIMEOUT_MS] [WALK_RADIUS]");
        return;
    }
    Nav n = {0};
    DWORD t0 = GetTickCount();
    float p[3];
    if (!player_pos(p)) { lr_log(L"GOTO no player"); return; }
    if (hypot(tx - p[0], tz - p[2]) < tol) { lr_log(L"GOTO reached (%.3f, %.3f, %.3f), already there", p[0], p[1], p[2]); return; }
    if (!nav_calibrate(&n)) { hold_sector(-1, 0); lr_log(L"GOTO can't move"); return; }
    nav_stop_log(nav_walk(&n, tx, tz, tol, timeout, walk_radius) ? L"reached" : L"failed at", t0);
}

// path X1 Z1 X2 Z2 ...: walks through the waypoints (1 m tolerance on the way, 0.45 m and walking near the last one);
// logs "GOTO reached" or "GOTO failed at".
static void run_path(const char *line)
{
    double pts[64];
    int n = 0, used = 0;
    const char *s = line;
    while (*s && *s != ' ') s++;
    while (n < 64 && sscanf(s, " %lf%n", &pts[n], &used) == 1) { n++; s += used; }
    if (n < 2 || n % 2) { lr_log(L"GOTO usage: path X1 Z1 [X2 Z2 ...]"); return; }
    Nav nav = {0};
    DWORD t0 = GetTickCount();
    if (!nav_calibrate(&nav)) { hold_sector(-1, 0); lr_log(L"GOTO can't move"); nav_stop_log(L"failed at", t0); return; }
    for (int i = 0; i < n; i += 2) {
        int last = i == n - 2;
        if (!nav_walk(&nav, pts[i], pts[i + 1], last ? 0.45 : 1.0, 30000, last ? 1.5 : 0)) {
            nav_stop_log(L"failed at", t0);
            return;
        }
    }
    nav_stop_log(L"reached", t0);
}

static void run_command(char *line)
{
    char *nl = strpbrk(line, "\r\n");
    if (nl)
        *nl = 0;
    char verb[16] = {0};
    if (sscanf(line, "%15s", verb) != 1)
        return;
    XPad pad = {0};
    BYTE keys[256] = {0};
    int ms = 0;
    if (!_stricmp(verb, "key")) {
        char names[160] = {0};
        if (sscanf(line, "%*s %159s %d", names, &ms) < 1)
            return;
        for (char *tok = strtok(names, ","); tok; tok = strtok(NULL, ",")) {
            int code = dik(tok);
            if (code < 0) { lr_log(L"AUTOPILOT unknown key %hs", tok); return; }
            keys[code] = 0x80;
        }
        if (ms <= 0) ms = 120;
        lr_log(L"AUTOPILOT %hs (keyboard reads so far: %ld)", line, key_reads);
        set_keys(keys);
        Sleep((DWORD)ms);
        BYTE none[256] = {0};
        set_keys(none);
        Sleep(80);
        return;
    } else if (!_stricmp(verb, "press")) {
        char buttons[128] = {0};
        if (sscanf(line, "%*s %127s %d", buttons, &ms) < 1)
            return;
        for (char *tok = strtok(buttons, ","); tok; tok = strtok(NULL, ",")) {
            if (!_stricmp(tok, "LT")) pad.lt = 255;
            else if (!_stricmp(tok, "RT")) pad.rt = 255;
            else pad.buttons |= button_bit(tok);
        }
        if (ms <= 0) ms = 120;
    } else if (!_stricmp(verb, "stick")) {
        char which[4] = {0};
        double x = 0, y = 0;
        if (sscanf(line, "%*s %3s %lf %lf %d", which, &x, &y, &ms) < 4)
            return;
        if (which[0] == 'R' || which[0] == 'r') { pad.rx = axis(x); pad.ry = axis(y); }
        else { pad.lx = axis(x); pad.ly = axis(y); }
    } else if (!_stricmp(verb, "wait")) {
        sscanf(line, "%*s %d", &ms);
        Sleep((DWORD)(ms > 0 ? ms : 0));
        return;
    } else if (!_stricmp(verb, "peek")) {
        // peek RVA[,off,off...] [N]: follows a pointer chain from the exe base and logs N floats (and the pointers).
        char chain[256] = {0};
        int count = 4;
        if (sscanf(line, "%*s %255s %d", chain, &count) < 1)
            return;
        uintptr_t addr = 0;
        int first = 1;
        for (char *tok = strtok(chain, ","); tok; tok = strtok(NULL, ",")) {
            uintptr_t off = (uintptr_t)_strtoui64(tok, NULL, 16);
            if (first) { addr = lr_base + off; first = 0; continue; }
            uintptr_t next = 0;
            if (IsBadReadPtr((void *)addr, 8)) { lr_log(L"PEEK %hs: unreadable %p", line, (void *)addr); return; }
            next = *(uintptr_t *)addr;
            addr = next + off;
        }
        if (count < 1 || count > 64) count = 4;
        if (IsBadReadPtr((void *)addr, (UINT_PTR)count * 4)) { lr_log(L"PEEK %hs: unreadable %p", line, (void *)addr); return; }
        wchar_t out[2048];
        int n = swprintf(out, 2048, L"PEEK %hs -> %p:", line + 5, (void *)addr);
        for (int i = 0; i < count && n < 1900; i++)
            n += swprintf(out + n, 2048 - n, L" %.3f", ((float *)addr)[i]);
        lr_log(L"%ls", out);
        return;
    } else if (!_stricmp(verb, "scan")) {
        // scan X Y Z R [MAX]: finds float triples within R of (X,Y,Z) in committed private read-write memory.
        float x, y, z, r;
        int max = 40;
        if (sscanf(line, "%*s %f %f %f %f %d", &x, &y, &z, &r, &max) < 4)
            return;
        int found = 0;
        MEMORY_BASIC_INFORMATION mbi;
        for (uint8_t *p = 0; VirtualQuery(p, &mbi, sizeof mbi) == sizeof mbi && found < max; p = (uint8_t *)mbi.BaseAddress + mbi.RegionSize) {
            if (mbi.State != MEM_COMMIT || mbi.Type != MEM_PRIVATE || !(mbi.Protect & (PAGE_READWRITE)) || (mbi.Protect & PAGE_GUARD))
                continue;
            float *f = (float *)mbi.BaseAddress;
            size_t n = mbi.RegionSize / 4;
            for (size_t i = 0; i + 2 < n && found < max; i += 1) {
                float dx = f[i] - x, dy = f[i + 1] - y, dz = f[i + 2] - z;
                if (dx * dx + dy * dy + dz * dz <= r * r && f[i] != 0) {
                    lr_log(L"SCAN %p: %.3f %.3f %.3f %.3f", (void *)&f[i], f[i], f[i + 1], f[i + 2], f[i + 3]);
                    found++;
                    i += 3;
                }
            }
        }
        lr_log(L"SCAN done: %d match(es) within %.2f of (%.2f, %.2f, %.2f)", found, r, x, y, z);
        return;
    } else if (!_stricmp(verb, "read")) {
        // read ADDR [N]: logs N floats at an absolute address (e.g. one found by scan).
        char a[64] = {0};
        int count = 4;
        if (sscanf(line, "%*s %63s %d", a, &count) < 1)
            return;
        float *p = (float *)(uintptr_t)_strtoui64(a, NULL, 16);
        if (count < 1 || count > 64) count = 4;
        if (IsBadReadPtr(p, (UINT_PTR)count * 4)) { lr_log(L"READ %hs: unreadable", a); return; }
        wchar_t out[2048];
        int n = swprintf(out, 2048, L"READ %p:", (void *)p);
        for (int i = 0; i < count && n < 1900; i++)
            n += swprintf(out + n, 2048 - n, L" %.3f", p[i]);
        lr_log(L"%ls", out);
        return;
    } else if (!_stricmp(verb, "readx")) {
        // readx ADDR[,off,off...] [N]: follows a pointer chain from an absolute address (each ",off" dereferences and
        // adds), then logs N u32 words in hex. Example: readx 7FF4AE0F5140,8,30 4 = 4 words at [0x7FF4AE0F5140+8]+0x30.
        char chain[256] = {0};
        int count = 4;
        if (sscanf(line, "%*s %255s %d", chain, &count) < 1)
            return;
        uintptr_t addr = 0;
        int first = 1;
        for (char *tok = strtok(chain, ","); tok; tok = strtok(NULL, ",")) {
            uintptr_t v = (uintptr_t)_strtoui64(tok, NULL, 16);
            if (first) { addr = v; first = 0; continue; }
            if (IsBadReadPtr((void *)addr, 8)) { lr_log(L"READX: unreadable %p", (void *)addr); return; }
            addr = *(uintptr_t *)addr + v;
        }
        if (count < 1 || count > 64) count = 4;
        if (IsBadReadPtr((void *)addr, (UINT_PTR)count * 4)) { lr_log(L"READX: unreadable %p", (void *)addr); return; }
        wchar_t out[2048];
        int n = swprintf(out, 2048, L"READX %p:", (void *)addr);
        for (int i = 0; i < count && n < 1900; i++)
            n += swprintf(out + n, 2048 - n, L" %08X", ((uint32_t *)addr)[i]);
        lr_log(L"%ls", out);
        return;
    } else if (!_stricmp(verb, "goto")) {
        run_goto(line);
        return;
    } else if (!_stricmp(verb, "warp")) {
        int map = 0, location = 0;
        if (sscanf(line, "%*s %d %d", &map, &location) == 2) { lr_entry_request_warp(map, location); lr_log(L"WARP queued: map %d location %d", map, location); }
        return;
    } else if (!_stricmp(verb, "givelot")) {
        int lot = 0;
        if (sscanf(line, "%*s %d", &lot) == 1) { lr_entry_request_lot(lot); lr_log(L"GIVELOT queued: %d", lot); }
        return;
    } else if (!_stricmp(verb, "path")) {
        run_path(line);
        return;
    } else if (!_stricmp(verb, "trace")) {
        // trace RVA [MAX]: log the first MAX calls of exe+RVA (args + caller), see trace.c
        char rva[32] = {0};
        int max = 30;
        if (sscanf(line, "%*s %31s %d", rva, &max) < 1)
            return;
        lr_trace((uintptr_t)_strtoui64(rva, NULL, 16), max);
        return;
    } else if (!_stricmp(verb, "log")) {
        wchar_t w[512];
        MultiByteToWideChar(CP_UTF8, 0, line + 4, -1, w, 512);
        lr_log(L"AUTOPILOT %ls", w);
        return;
    } else {
        lr_log(L"AUTOPILOT unknown command: %hs", line);
        return;
    }
    lr_log(L"AUTOPILOT %hs (XInputGetState calls so far: %ld/%ld/%ld/%ld, pad 0 real result %ld)", line, calls[0], calls[1], calls[2], calls[3], last_result);
    set_pad(&pad);
    Sleep((DWORD)ms);
    XPad idle = {0};
    set_pad(&idle);
    Sleep(60); // let the game see the release before the next command
}

// Follows control.txt like `tail -f`: commands appended after startup are run once each.
static DWORD WINAPI control_thread(LPVOID unused)
{
    (void)unused;
    long long offset = 0;
    char pending[4096];
    size_t have = 0;
    for (;;) {
        HANDLE f = CreateFileW(control_path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, 0, NULL);
        if (f != INVALID_HANDLE_VALUE) {
            LARGE_INTEGER size;
            if (GetFileSizeEx(f, &size)) {
                if (size.QuadPart < offset) // file was truncated: start over
                    offset = 0, have = 0;
                if (size.QuadPart > offset) {
                    LARGE_INTEGER pos = {.QuadPart = offset};
                    SetFilePointerEx(f, pos, NULL, FILE_BEGIN);
                    DWORD got = 0;
                    if (ReadFile(f, pending + have, (DWORD)(sizeof pending - 1 - have), &got, NULL) && got) {
                        offset += got;
                        have += got;
                        pending[have] = 0;
                    }
                }
            }
            CloseHandle(f);
        }
        char *nl;
        while ((nl = memchr(pending, '\n', have)) != NULL) {
            size_t len = (size_t)(nl - pending) + 1;
            char line[512];
            size_t copy = len < sizeof line - 1 ? len : sizeof line - 1;
            memcpy(line, pending, copy);
            line[copy] = 0;
            memmove(pending, pending + len, have - len);
            have -= len;
            run_command(line);
        }
        Sleep(50);
    }
    return 0;
}

// Hooks XInputGetState once xinput1_3.dll is loaded (it is a static import of the exe, so it is mapped by the time
// the game calls DirectInput8Create; DllMain may run before it).
void lr_autopilot_hook(void)
{
    if (InterlockedCompareExchange(&installed, 1, 0) != 0)
        return;
    HMODULE x = GetModuleHandleW(L"xinput1_3.dll");
    if (!x) {
        InterlockedExchange(&installed, 0);
        return;
    }
    void *target = (void *)GetProcAddress(x, "XInputGetState");
    if (!target) {
        lr_log(L"autopilot: XInputGetState not found");
        return;
    }
    MH_STATUS s = MH_Initialize();
    if (s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) {
        lr_log(L"autopilot: MH_Initialize failed (%d)", (int)s);
        return;
    }
    if (MH_CreateHook(target, (void *)hk_XInputGetState, (void **)&orig_XInputGetState) != MH_OK || MH_EnableHook(target) != MH_OK) {
        lr_log(L"autopilot: hooking XInputGetState failed");
        return;
    }
    lr_log(L"autopilot: XInputGetState hooked; reading %ls", control_path);
    CreateThread(NULL, 0, control_thread, NULL, 0, NULL);
}

void lr_autopilot_install(const wchar_t *lordran_dir)
{
    InitializeCriticalSection(&pad_lock);
    lstrcpyW(control_path, lordran_dir);
    lstrcatW(control_path, L"control.txt");
    DeleteFileW(control_path); // commands from an earlier session must not replay
    lr_autopilot_hook();
}
