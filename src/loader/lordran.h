#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>
#include <wctype.h>

// DS2 SOTFS DarkSoulsII.exe v1.0.3.0 (28,200,992 bytes). RVAs come from the Ghidra decompile
// (preferred base 0x140000000); see docs/ds2-re-notes.md.
#define RVA_EBL_VFTABLE  0x012928e8u  // DLEBL::DLEncryptedBinderLightFileDevice (encrypted archives)
#define RVA_EBL_OPEN     0x00fef510u
#define RVA_DISK_VFTABLE 0x0114fdd8u  // DLIOD::msvc90_windows::MicrosoftDiskFileDevice (loose files)
#define RVA_DISK_OPEN    0x008e80f0u
#define VT_SLOT_OPEN     1
// Regulation resource (enc_regulation.bnd.dcx): vtable slot 16 = FUN_1402df8f0(self, file data, file size), which calls
// FUN_1402e0470 to CWC-decrypt, DCX-decompress and parse it. Afterwards the BND4 is at self+0xd8, its size at self+0xd4.
#define RVA_REGULATION_VFTABLE 0x010d9a08u
#define RVA_REGULATION_LOAD    0x002df8f0u
#define VT_SLOT_REGULATION_LOAD 16

// The game's wide string (MSVC std::wstring with a stateful allocator): 8-wchar small buffer or a
// heap pointer, then size and capacity. Capacity >= 8 means the pointer is in use (see FUN_140fef510).
typedef struct DLString {
    union {
        wchar_t small[8];
        wchar_t *heap;
    };
    uint64_t size;
    uint64_t capacity;
    void *allocator;
} DLString;

static inline const wchar_t *dl_str(const DLString *s)
{
    return s->capacity >= 8 ? s->heap : s->small;
}

// DLFileDevice::Open (vtable slot 1). Arguments after the path are passed through untouched.
typedef void *(*DeviceOpenFn)(void *device, DLString *path, void *a3, void *a4, void *allocator, uint64_t flag);

extern uintptr_t lr_base;

// Switches read from <pack>\loader.ini [loader] at startup (defaults in main.c). They let experiments be run
// by editing a file in the workspace instead of reinstalling the DLL.
typedef struct LoaderConfig {
    int device_hooks;      // swap the file devices' Open slots at all
    int archive_redirect;  // serve gamedata:/... etc. from the pack
    int archive_crypto_fix; // drop the archive's AES/SHA step for pack-served files (vfs.c, session 11 tae12); 0 = old behaviour
    int loose_redirect;    // serve <game>\... (title:) from the pack via CreateFileW & co.
    int log_opens;         // log every Open (slow; for diagnosis only)
    int dump_regulation;   // write the BND4 the game decoded to <game>/lordran/regulation-loaded.bnd
    int regulation_check;  // wrap the regulation load to log whether the game accepted it (needed for dump_regulation)
    int autopilot;         // virtual XInput pad driven by <game>\lordran\control.txt (automated in-game tests)
    int travel_pages;      // Travel menu: Lordran / Drangleic pages switched with LB/RB (travel.c, session 8)
    int travel_title;      // Travel window title: 0 vanilla (bonfire name), 1 page name, 2 page name - bonfire name
    int map_cap;           // WorldMapList limit raised to this many maps (mapcap.c, session 11); <= 42 = vanilla, max 48
    int gesture_pages;     // gesture menu: Drangleic / Lordran (DS1 gestures) pages switched with LB/RB (gesture.c, session 11)
    int seam_stream;       // DS1 maps (areas 18/19) count as available to DS2's neighbour streaming (stream.c, session 11 seam12)
    int nmb_pool_grow;     // DS2's fixed 12,288-entry Morpheme asset pool grows instead of crashing at exe+5f6634 (nmbpool.c, session 13 seam13)
    int dummy_guard;       // rigid-model Flag1 + attach-bone dummies cleared instead of crashing at exe+b35dae (dummyguard.c, session 13 seam13)
} LoaderConfig;
extern LoaderConfig lr_config;
extern wchar_t lr_pack_dir[MAX_PATH]; // the content pack in use (main.c find_pack), "" = none

void lr_log_open(const wchar_t *path);
void lr_log(const wchar_t *fmt, ...);
void lr_log_close(void);

int lr_patch_vtable(const wchar_t *name, uint32_t vt_rva, int index, uint32_t expected_rva, void *hook, void **orig);

void lr_vfs_install(const wchar_t *pack);
void lr_vfs_report(void);

void lr_winfs_install(const wchar_t *pack, const wchar_t *game_dir);
void lr_winfs_report(void);

void lr_regulation_install(const wchar_t *lordran_dir);

void lr_autopilot_install(const wchar_t *lordran_dir);
void lr_autopilot_hook(void);
void lr_autopilot_dinput(void *dinput);
int lr_trace(uintptr_t rva, int max);

void lr_entry_install(void);
void lr_entry_tick(void);
void lr_devkit_tick(void);   // devkit.c (session 11, devkit11): the Lordran Dev Kit, only with a --dev-kit pack (lordran/devkit.ini)
void lr_entry_request_warp(int decimal_map, int location);
void lr_entry_request_lot(int lot);

void lr_travel_tick(void);
void lr_gesture_tick(void);

void lr_mapcap_tick(void);
int lr_mapcap_active(void); // the active map cap, 0 = off
void lr_mapcap_set_dir(const wchar_t *lordran_dir); // side files go to <game>/lordran/mapcap/
void lr_mapcap_report(void);

void lr_stream_tick(void);   // stream.c (session 11, seam12): seam streaming gate for DS1 maps
int lr_stream_active(void);
void lr_nmbpool_install(void);    // nmbpool.c (session 13, seam13): rule I1p-L, Morpheme asset pool growth
void lr_dummyguard_install(void); // dummyguard.c (session 13, seam13): rule DM1 safety net for exe+b35dae
