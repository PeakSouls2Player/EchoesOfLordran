// The Lordran Dev Kit (session 11, track devkit11; TEST BUILDS ONLY: build-pack --dev-kit writes <pack>\lordran\devkit.ini,
// no ini = this module does nothing). The kit is a consumable goods item (a Lifegem clone, Build/DevKit.cs); DS2's item
// scripts have no item-lot opcode, so the loader does the giving through the game's own pickup path, the same two functions
// the Echo of Lordran uses (entry.c: ESD 131401 item count, ESD 131402 give item lot with the "item received" popup):
//  - once a character has been in the world for SETTLE_MS, a character without a kit gets the kit lot (once per load);
//  - then the kit count is polled every POLL_MS; when it drops from >= 1 to 0 (the kit was used) every content lot of the
//    ini is given, one every interval_ms (the inventory overflows to the item box, DS2's rule), then a new kit.
// Runs on the game's input poll (autopilot.c), the game thread, like lr_entry_tick.
#include "lordran.h"

#define RVA_GAME_MANAGER  0x16148F0
#define RVA_ITEM_COUNT    0x1ABF00  // int (inventory = [[gm+0xA8]+0x10], int itemId, uint8_t *where): ESD function 131401
#define RVA_GIVE_ITEM_LOT 0x198E50  // int *(mgr = [[gm+0x70]+0x50], int *result, ItemLot *, int popup): ESD command 131402(lot, 1)
#define SETTLE_MS         10000     // in the world this long before the first check (after the Echo's 8 s)
#define POLL_MS           1000
#define MAX_LOTS          1000

typedef struct DevKitLot { int lot; float unk; } DevKitLot;
typedef int (*ItemCountFn)(void *inventory, int item, uint8_t *where);
typedef int *(*GiveItemLotFn)(void *mgr, int *result, DevKitLot *lot, int popup);

static int loaded, enabled, kit_item, kit_lot, interval_ms = 250, lot_count;
static int lots[MAX_LOTS];

static uintptr_t rd(uintptr_t at)
{
    if (!at || IsBadReadPtr((void *)at, 8))
        return 0;
    return *(uintptr_t *)at;
}

static void load(void)
{
    loaded = 1;
    if (!lr_pack_dir[0]) return;
    wchar_t ini[MAX_PATH];
    swprintf(ini, MAX_PATH, L"%ls%lslordran\\devkit.ini", lr_pack_dir, lr_pack_dir[lstrlenW(lr_pack_dir) - 1] == L'\\' ? L"" : L"\\");
    if (GetFileAttributesW(ini) == INVALID_FILE_ATTRIBUTES) return; // not a --dev-kit pack: nothing to do, no log noise
    if (GetPrivateProfileIntW(L"devkit", L"version", 0, ini) != 1) { lr_log(L"dev kit: %ls has an unknown version", ini); return; }
    kit_item = (int)GetPrivateProfileIntW(L"devkit", L"item", 0, ini);
    kit_lot = (int)GetPrivateProfileIntW(L"devkit", L"lot", 0, ini);
    interval_ms = (int)GetPrivateProfileIntW(L"devkit", L"interval_ms", 250, ini);
    int n = (int)GetPrivateProfileIntW(L"devkit", L"count", 0, ini);
    if (n > MAX_LOTS) n = MAX_LOTS;
    for (int i = 0; i < n; i++) {
        wchar_t key[16];
        swprintf(key, 16, L"lot%d", i);
        int v = (int)GetPrivateProfileIntW(L"devkit", key, 0, ini);
        if (v > 0) lots[lot_count++] = v;
    }
    enabled = kit_item > 0 && kit_lot > 0;
    lr_log(L"dev kit: %ls: item %d, kit lot %d, %d content lots, every %d ms (TEST BUILD)", ini, kit_item, kit_lot, lot_count, interval_ms);
}

static int give(uintptr_t gm, int lot)
{
    uintptr_t mgr = rd(rd(gm + 0x70) + 0x50);
    if (!mgr) return -1;
    DevKitLot l = { lot, 0.0f };
    int result = -1;
    ((GiveItemLotFn)(lr_base + RVA_GIVE_ITEM_LOT))((void *)mgr, &result, &l, 1);
    return result;
}

void lr_devkit_tick(void)
{
    static uintptr_t last_player;
    static DWORD seen_at, polled_at, gave_at;
    static int checked, last_count = -1, queue = -1; // queue: next content lot to give (-1 = idle; lot_count = the new kit)
    if (!loaded) load();
    if (!enabled) return;
    uintptr_t gm = rd(lr_base + RVA_GAME_MANAGER);
    uintptr_t player = gm ? rd(gm + 0xD0) : 0;
    if (!player) { last_player = 0; checked = 0; last_count = -1; return; } // title screen or loading (a queue resumes after)
    DWORD now = GetTickCount();
    if (player != last_player) { last_player = player; seen_at = now; checked = 0; last_count = -1; return; }
    if (now - seen_at < SETTLE_MS) return;

    if (queue >= 0) { // giving the content: one lot per interval
        if (now - gave_at < (DWORD)interval_ms) return;
        gave_at = now;
        int lot = queue < lot_count ? lots[queue] : kit_lot;
        int r = give(gm, lot);
        if (queue == 0 || queue >= lot_count - 1) lr_log(L"dev kit: lot %d (%d/%d) result %d", lot, queue + 1, lot_count + 1, r);
        queue = queue < lot_count ? queue + 1 : -1;
        if (queue < 0) last_count = -1; // re-read the new kit
        return;
    }
    if (now - polled_at < POLL_MS && checked) return;
    polled_at = now;
    uintptr_t inventory = rd(rd(gm + 0xA8) + 0x10);
    if (!inventory) return;
    uint8_t where = 7; // inventory, equipped and item box (as entry.c)
    int count = ((ItemCountFn)(lr_base + RVA_ITEM_COUNT))((void *)inventory, kit_item, &where);
    if (!checked) {
        checked = 1;
        if (count <= 0) { lr_log(L"dev kit: character has none: gave kit lot %d (result %d)", kit_lot, give(gm, kit_lot)); last_count = -1; return; }
    } else if (last_count >= 1 && count == 0) {
        lr_log(L"dev kit: kit used: giving %d content lots", lot_count);
        queue = 0;
        gave_at = now - (DWORD)interval_ms;
    }
    last_count = count;
}
