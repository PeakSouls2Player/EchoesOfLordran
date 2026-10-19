// The Lordran entry item (session 5, user's choice): "Echo of Lordran", an infinite Homeward Bone whose special effect
// warps to the DS1 cell (built by build-pack: Build/Maps/EntryItem.cs). Its warp is pure data; the loader only makes sure
// every character carries one: once a character has been in the world for a few seconds, it counts the item in the
// inventory and, if there is none, gives its item lot through the game's own pickup path (the "item received" popup).
// Runs on the game's input poll (keyboard GetDeviceState, see autopilot.c), the thread that runs the game loop.
#include "lordran.h"

#define RVA_GAME_MANAGER  0x16148F0
#define RVA_ITEM_COUNT    0x1ABF00  // int (inventory = [[gm+0xA8]+0x10], int itemId, uint8_t *where): ESD function 131401
#define RVA_GIVE_ITEM_LOT 0x198E50  // int *(mgr = [[gm+0x70]+0x50], int *result, ItemLot *, int popup): ESD command 131402(lot, 1)
#define ENTRY_ITEM        60357000  // ItemParam (Build/Maps/EntryItem.cs)
#define ENTRY_LOT         60357000  // ItemLotParam2_Other
#define SETTLE_MS         8000      // in the world this long before checking (not during the load or the area banner)

typedef struct ItemLot { int lot; float unk; } ItemLot;
typedef int (*ItemCountFn)(void *inventory, int item, uint8_t *where);
typedef int *(*GiveItemLotFn)(void *mgr, int *result, ItemLot *lot, int popup);

#define RVA_WARP_TO_LOCATION 0x4521B0 // int (packed map AA<<24|BB<<16|CC<<8|DD, EventLocation id): queues a map warp
typedef int (*WarpToLocationFn)(uint32_t map, int location);

static DWORD main_thread;
static DWORD tick_thread;
static volatile LONG warp_map, warp_location; // a warp asked for by the autopilot ("warp"), run on the game thread
static volatile LONG give_lot;                 // an item lot asked for by the autopilot ("givelot")

void lr_entry_request_lot(int lot) { InterlockedExchange(&give_lot, lot); }

// Autopilot "warp DECIMALMAP LOCATION" (tests): e.g. warp 18010000 3700010.
void lr_entry_request_warp(int decimal_map, int location)
{
    uint32_t aa = decimal_map / 1000000, bb = decimal_map / 10000 % 100, cc = decimal_map / 100 % 100, dd = decimal_map % 100;
    InterlockedExchange(&warp_location, location);
    InterlockedExchange(&warp_map, (LONG)(aa << 24 | bb << 16 | cc << 8 | dd));
}

void lr_entry_install(void)
{
    main_thread = GetCurrentThreadId(); // DllMain runs on the thread that loads the exe's static imports
}

static uintptr_t read_ptr(uintptr_t at)
{
    if (!at || IsBadReadPtr((void *)at, 8))
        return 0;
    return *(uintptr_t *)at;
}

void lr_entry_tick(void)
{
    static uintptr_t last_player;
    static DWORD seen_at;
    static int checked;
    if (!tick_thread) {
        tick_thread = GetCurrentThreadId();
        lr_log(L"entry item: input poll runs on thread %lu (loader thread %lu)", tick_thread, main_thread);
    }
    uintptr_t gm = read_ptr(lr_base + RVA_GAME_MANAGER);
    uintptr_t player = gm ? read_ptr(gm + 0xD0) : 0;
    LONG wm = InterlockedExchange(&warp_map, 0);
    if (wm && player && read_ptr(gm + 0x70)) {
        int r = ((WarpToLocationFn)(lr_base + RVA_WARP_TO_LOCATION))((uint32_t)wm, warp_location);
        lr_log(L"WARP map %08lX location %ld requested (result %d)", wm, warp_location, r);
    }
    LONG gl = InterlockedExchange(&give_lot, 0);
    if (gl && player) {
        uintptr_t mgr = read_ptr(read_ptr(gm + 0x70) + 0x50);
        ItemLot lot = { gl, 0.0f };
        int result = -1;
        if (mgr) ((GiveItemLotFn)(lr_base + RVA_GIVE_ITEM_LOT))((void *)mgr, &result, &lot, 1);
        lr_log(L"GIVELOT %ld result %d", gl, result);
    }
    if (!player) { last_player = 0; checked = 0; return; } // title screen or loading
    if (player != last_player) { last_player = player; seen_at = GetTickCount(); checked = 0; return; }
    if (checked || GetTickCount() - seen_at < SETTLE_MS)
        return;
    checked = 1;

    uintptr_t inventory = read_ptr(read_ptr(gm + 0xA8) + 0x10);
    uintptr_t items = read_ptr(read_ptr(gm + 0x70) + 0x50);
    if (!inventory || !items) { lr_log(L"entry item: no inventory (%p) or item manager (%p)", (void *)inventory, (void *)items); return; }
    uint8_t where = 7; // inventory, equipped and item box (ESD 131401's flags; it defaults to 3)
    int count = ((ItemCountFn)(lr_base + RVA_ITEM_COUNT))((void *)inventory, ENTRY_ITEM, &where);
    if (count > 0) { lr_log(L"entry item: character has %d", count); return; }
    ItemLot lot = { ENTRY_LOT, 0.0f };
    int result = -1;
    ((GiveItemLotFn)(lr_base + RVA_GIVE_ITEM_LOT))((void *)items, &result, &lot, 1);
    lr_log(L"entry item: gave lot %d (result %d: 1 = added, 2 = sent to the item box?, 0 = failed)", ENTRY_LOT, result);
}
