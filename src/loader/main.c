// Lordran loader entry point. Loaded as dinput8.dll from DS2's game folder (see proxy.c).
#include "lordran.h"

uintptr_t lr_base;
LoaderConfig lr_config = { .device_hooks = 1, .archive_redirect = 1, .archive_crypto_fix = 1, .loose_redirect = 1, .log_opens = 0, .dump_regulation = 0, .regulation_check = 1, .autopilot = 0, .travel_pages = 1, .travel_title = 1, .map_cap = 48, .gesture_pages = 1, .seam_stream = 1, .dummy_guard = 1, .nmb_pool_grow = 1 };
wchar_t lr_pack_dir[MAX_PATH];

static void read_config(const wchar_t *pack)
{
    if (!pack[0])
        return;
    wchar_t ini[MAX_PATH];
    lstrcpynW(ini, pack, MAX_PATH - 16);
    size_t n = (size_t)lstrlenW(ini);
    if (n && ini[n - 1] != L'\\')
        lstrcatW(ini, L"\\");
    lstrcatW(ini, L"loader.ini");
    lr_config.device_hooks = (int)GetPrivateProfileIntW(L"loader", L"device_hooks", lr_config.device_hooks, ini);
    lr_config.archive_redirect = (int)GetPrivateProfileIntW(L"loader", L"archive_redirect", lr_config.archive_redirect, ini);
    lr_config.archive_crypto_fix = (int)GetPrivateProfileIntW(L"loader", L"archive_crypto_fix", lr_config.archive_crypto_fix, ini);
    lr_config.loose_redirect = (int)GetPrivateProfileIntW(L"loader", L"loose_redirect", lr_config.loose_redirect, ini);
    lr_config.log_opens = (int)GetPrivateProfileIntW(L"loader", L"log_opens", lr_config.log_opens, ini);
    lr_config.dump_regulation = (int)GetPrivateProfileIntW(L"loader", L"dump_regulation", lr_config.dump_regulation, ini);
    lr_config.regulation_check = (int)GetPrivateProfileIntW(L"loader", L"regulation_check", lr_config.regulation_check, ini);
    lr_config.autopilot = (int)GetPrivateProfileIntW(L"loader", L"autopilot", lr_config.autopilot, ini);
    lr_config.travel_pages = (int)GetPrivateProfileIntW(L"loader", L"travel_pages", lr_config.travel_pages, ini);
    lr_config.travel_title = (int)GetPrivateProfileIntW(L"loader", L"travel_title", lr_config.travel_title, ini);
    lr_config.map_cap = (int)GetPrivateProfileIntW(L"loader", L"map_cap", lr_config.map_cap, ini);
    lr_config.gesture_pages = (int)GetPrivateProfileIntW(L"loader", L"gesture_pages", lr_config.gesture_pages, ini);
    lr_config.seam_stream = (int)GetPrivateProfileIntW(L"loader", L"seam_stream", lr_config.seam_stream, ini);
    lr_config.dummy_guard = (int)GetPrivateProfileIntW(L"loader", L"dummy_guard", lr_config.dummy_guard, ini);
    lr_config.nmb_pool_grow = (int)GetPrivateProfileIntW(L"loader", L"nmb_pool_grow", lr_config.nmb_pool_grow, ini);
    lr_log(L"config (%ls): device_hooks=%d archive_redirect=%d archive_crypto_fix=%d loose_redirect=%d log_opens=%d dump_regulation=%d regulation_check=%d autopilot=%d travel_pages=%d travel_title=%d map_cap=%d gesture_pages=%d seam_stream=%d dummy_guard=%d nmb_pool_grow=%d", ini,
           lr_config.device_hooks, lr_config.archive_redirect, lr_config.archive_crypto_fix, lr_config.loose_redirect, lr_config.log_opens,
           lr_config.dump_regulation, lr_config.regulation_check, lr_config.autopilot, lr_config.travel_pages, lr_config.travel_title, lr_config.map_cap, lr_config.gesture_pages, lr_config.seam_stream, lr_config.dummy_guard, lr_config.nmb_pool_grow);
}

#ifdef LORDRAN_DEV
#define LORDRAN_DEV_PACK L"C:\\EchoesOfLordran\\data\\lordran\\pack"
#else
#define LORDRAN_DEV_PACK L""
#endif

// Content pack location, first match wins:
//   1. "pack=" line in <game>\lordran\lordran.ini
//   2. <game>\lordran\pack\ if it exists
//   3. the developer build's workspace pack (LORDRAN_DEV_PACK, set by build.bat)
static void find_pack(const wchar_t *lordran_dir, wchar_t *out)
{
    out[0] = 0;
    wchar_t ini[MAX_PATH];
    lstrcpyW(ini, lordran_dir);
    lstrcatW(ini, L"lordran.ini");
    GetPrivateProfileStringW(L"lordran", L"pack", L"", out, MAX_PATH, ini);
    if (out[0])
        return;

    lstrcpyW(out, lordran_dir);
    lstrcatW(out, L"pack");
    if (GetFileAttributesW(out) != INVALID_FILE_ATTRIBUTES)
        return;

    lstrcpynW(out, LORDRAN_DEV_PACK, MAX_PATH);
}

static void init(HMODULE self)
{
    lr_base = (uintptr_t)GetModuleHandleW(NULL);

    wchar_t dir[MAX_PATH];
    DWORD n = GetModuleFileNameW(self, dir, MAX_PATH);
    while (n > 0 && dir[n - 1] != L'\\')
        n--;
    dir[n] = 0;

    wchar_t lordran_dir[MAX_PATH];
    lstrcpyW(lordran_dir, dir);
    lstrcatW(lordran_dir, L"lordran\\");
    CreateDirectoryW(lordran_dir, NULL);

    wchar_t log_path[MAX_PATH];
    lstrcpyW(log_path, lordran_dir);
    lstrcatW(log_path, L"lordran.log");
    lr_log_open(log_path);

    wchar_t exe[MAX_PATH];
    GetModuleFileNameW(NULL, exe, MAX_PATH);
    lr_log(L"Lordran loader v2.11-seam13 (archive redirect via pack-backed archive operators + OS-level loose-file redirect + regulation check + autopilot + entry item + travel pages + seam stream + dummy guard + nmb pool growth). exe=%ls base=%p", exe, (void *)lr_base);
    lr_log(L"seamless coop dll loaded yet: %ls", GetModuleHandleW(L"ds2sc.dll") ? L"yes" : L"no");

    wchar_t pack[MAX_PATH];
    find_pack(lordran_dir, pack);
    lstrcpynW(lr_pack_dir, pack, MAX_PATH);
    read_config(pack);
    if (lr_config.device_hooks)
        lr_vfs_install(pack);
    if (lr_config.loose_redirect)
        lr_winfs_install(pack, dir);
    if (lr_config.regulation_check)
        lr_regulation_install(lordran_dir);
    if (lr_config.autopilot)
        lr_autopilot_install(lordran_dir);
    lr_mapcap_set_dir(lordran_dir);
    lr_entry_install();
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(self);
        init(self);
    } else if (reason == DLL_PROCESS_DETACH) {
        lr_vfs_report();
        lr_winfs_report();
        lr_mapcap_report();
        lr_log(L"seamless coop dll loaded: %ls", GetModuleHandleW(L"ds2sc.dll") ? L"yes" : L"no");
        lr_log_close();
    }
    return TRUE;
}
