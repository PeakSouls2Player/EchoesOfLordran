// Lordran / Drangleic pages in the bonfire Travel menu (session 8; user: "I want the DS1 bonfires to have their own
// separate Travel page, so it's clear whether a teleport is Lordran or Drangleic").
//
// The Travel area grid is FeGroupTestBonfireTransitionTop ("Top"). Static RE in docs/travel-page-re.md (session 7),
// re-checked in session 8 against the decompile and the unpacked exe with data\research\travel\trace.py:
//  - Top open (primary vtable slot 25, 0x1400d5480 -> chunk 0x141b78e86) builds the lit-area list with 0x14017dff0 and
//    copies it into the Top with 0x1400d3d40(Top+0x6d0, list) (its only caller: 0x1400d54d7). Entries are 12 bytes
//    {u32 menuAreaID; u8 lit; u8 sortKey; u16; u32 newMark} at Top+0x6d0 (+(-addr & 3) alignment), count u64 at
//    Top+0xb58 (= vector+0x488). Then the open sets the window title to bonfirename([Top+0x2d8]+0x2f60) = the bonfire
//    you rest at, through the secondary vtable (Top+0x58) slot 3 (0x1400d4120, any wchar_t*), sets the prompt bar
//    (bofire.fmg 89001 + 89901), rebuilds the grid (0x1400d4870: item count from Top+0xb58, cells, and the bonfire
//    list of the selected cell), puts the cursor on cell 0 (0x140021b00) and fills the bonfire list.
//  - The Top's per-frame input (primary slot 2, 0x1400d4360) is (Top, float dt, uint32_t ev[7]): the second argument
//    is a float in xmm1 (movaps xmm6, xmm1; session 7's notes said "int type"). ev[0] = pressed this frame, ev[1] =
//    pressed or auto-repeat, ev[2] = held (pad-action table 0x141567b40 filled by 0x14010b9b0/0x14010bc50: menu bit
//    0x40000 = action 0x25 = LB, 0x80000 = action 0x26 = RB; Shift+Left/Right on the keyboard). The bonfire list and
//    popups are pushed as children on the Top's group stack (secondary slot 0 -> 0x140023e90 on Top+0x70; count u64
//    Top+0xC0, read by 0x140023f40 at +0x50): 0 = the grid itself has focus.
// What this module does (loader.ini travel_pages=1, the default):
//  - wraps the copy (MinHook on 0x1400d3d40): keeps the full area list, classifies each area (Lordran = some bonfire
//    record [[[gm+0x70]+0x58]+0x20] with that menuAreaID has a DS1 bonfire id 40000..49999; DS2's go up to 37685) and
//    leaves only one page's areas in the Top: the page of the bonfire you rest at (the other page if that one is empty);
//  - wraps the Top's input (vtable swap of primary slot 2: xref scan shows the slot is 0x1400d4360's only reference):
//    while the grid has focus, LB (Shift+Left) shows the Drangleic page and RB (Shift+Right) the Lordran page (refilter,
//    cursor to cell 0, rebuild grid + list, area caption, title); the bumpers are not passed on, nor the direction bits
//    while a bumper is down, so a Shift+arrow chord can't also move the cursor (session 11: was a toggle on either);
//  - wraps the title setter (vtable swap of secondary slot 3, its only reference): the window title names the page,
//    bofire.fmg 89010 "Drangleic" / 89011 "Lordran" (written by Build/Maps/TravelPages.cs; built-in names if absent).
//    loader.ini travel_title=2 appends the bonfire name ("Lordran - Firelink Shrine"), 0 keeps the vanilla title.
// Safety: every address is rebased on lr_base (DarkSoulsII.exe has DYNAMIC_BASE; the session-7 sketch assumed
// 0x140000000) and checked against the bytes of DarkSoulsII.exe v1.0.3.0 (the SIG/SLOT table below; check script
// data\sandbox\travel\tools\travel_sigcheck.py) before anything is hooked; one mismatch disables the feature and is
// logged. Only menu code is touched (no network/session code, nothing Seamless Co-op hooks). The hooks go in on the
// first keyboard poll (the game thread, after SteamStub's start-up), like the session-5 trace hooks, not in DllMain.
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#include <stdio.h>

#define EXE_BASE 0x140000000ull
#define AT(va) (lr_base + (uintptr_t)((va) - EXE_BASE)) // a VA of the decompile, rebased on the loaded exe

#define VA_GAME_MANAGER 0x1416148f0ull
#define VA_TOP_VFT      0x1410baa18ull // FeGroupTestBonfireTransitionTop, primary vtable
#define VA_TOP_VFT2     0x1410bab68ull // its secondary vtable (object at Top+0x58)
#define VA_COPY_AREAS   0x1400d3d40ull // void *(vector *dst = Top+0x6d0, vector *src)
#define VA_TOP_INPUT    0x1400d4360ull // primary slot 2: void (Top *, float dt, uint32_t ev[7])
#define VA_TOP_CAPTION  0x1400d5990ull // primary slot 12: void (Top *): caption of the selected area
#define VA_TOP_TITLE    0x1400d4120ull // secondary slot 3: void (Top+0x58, const wchar_t *)
#define VA_TOP_REBUILD  0x1400d4870ull // void (Top *): grid cells + bonfire list of the selected cell
#define VA_SET_CURSOR   0x140021b00ull // char (group *, int index)
#define VA_GET_TEXT     0x140503620ull // const wchar_t *(int kind, int id)
#define SLOT_INPUT   2
#define SLOT_CAPTION 12
#define SLOT2_TITLE  3

#define TOP_SUB       0x58   // secondary object (title, prompt bar, child stack at +0x18)
#define TOP_CHILDREN  0xc0   // u64: children on the Top's group stack (bonfire list, popups)
#define TOP_OWNER     0x2d8  // the bonfire root menu
#define OWNER_BONFIRE 0x2f60 // u32: the bonfire you rest at (the open shows its bonfirename as the title)
#define TOP_AREAS     0x6d0  // area vector
#define AREAS_COUNT   0x488  // u64 count, relative to the vector (Top+0xb58)
#define MAX_AREAS     0x60   // the vector's capacity (DLFixedVector<12-byte entry, 0x60>)

#define MENU_LB    0x40000u
#define MENU_RB    0x80000u
#define MENU_PAGES (MENU_LB | MENU_RB)
#define MENU_DIRS  0xffu     // menu bits 0x1..0x8 (actions 0x21..0x24) and 0x10..0x80 (0x16..0x19): the direction sets

#define TEXT_BOFIRE    0x0b  // FMG kinds = index in the exe's name table at 0x141585e70: 0x0b bofire, 0x12 BonfireName
#define PAGE_NAME_TEXT 89010 // bofire.fmg 89010 + page: 89010 Drangleic, 89011 Lordran (Build/Maps/TravelPages.cs)
#define DS1_BONFIRE_MIN 40000
#define DS1_BONFIRE_MAX 49999

// Expected code bytes (from DarkSoulsII.exe v1.0.3.0; relative operands only, no relocated absolute addresses) and
// vtable slots. Parsed by data\sandbox\travel\tools\travel_sigcheck.py too: keep one SIG/SLOT per line.
typedef struct Sig { uint64_t va; const char *what; const char *bytes; } Sig;
typedef struct Slot { uint64_t vt; int index; uint64_t fn; const char *what; } Slot;
#define SIG(va, what, bytes) { va, what, bytes }
#define SLOT(vt, index, fn, what) { vt, index, fn, what }
static const Sig sigs[] = {
    SIG(0x1400d3d40, "area list copy", "48 89 5c 24 08 57 48 83 ec 20 48 8b 81 88 04 00 00 4c 8b d9"),
    SIG(0x1400d54d7, "Top open: the copy's only call", "e8 64 e8 ff ff"),
    SIG(0x140035ad2, "Top open: copy destination Top+0x6d0", "48 8d 8e d0 06 00 00 50 5a"),
    SIG(0x141c3067a, "Top open: title = bonfirename([Top+0x2d8]+0x2f60)", "48 8b 86 d8 02 00 00 48 8d 4c 24 20 ba 12 00 00 00 44 8b 80 60 2f 00 00"),
    SIG(0x1400d4360, "Top input (Top, float dt, ev)", "40 57 48 83 ec 60 48 89 74 24 48 4c 89 74 24 40 0f 29 74 24 30 0f 28 f1 49 8b f0 48 8b f9 e8 bd dd f4 ff f7 06 00 20 00 00"),
    SIG(0x14002722c, "group input: child stack at +0x18", "48 8d 4b 18"),
    SIG(0x140023f5b, "child stack: count at +0x50", "48 83 79 50 00"),
    SIG(0x1400d4120, "Top title setter", "40 53 48 81 ec 20 03 00 00 4c 8d 44 24 24 48 8d 44 24 20 48 8b da"),
    SIG(0x1400d4870, "Top grid rebuild (stub)", "e9 78 16 ab 01"),
    SIG(0x141b85eed, "Top grid rebuild (body)", "48 8d 64 24 f8 48 89 2c 24 48 89 74 24 f8"),
    SIG(0x141ca86d4, "grid rebuild: item count Top+0xb58", "8b b1 58 0b 00 00 48 8b f9 8b d6"),
    SIG(0x1400d5990, "Top caption", "40 53 48 83 ec 70 48 8b 05 b3 c2 50 01 48 33 c4 48 89 44 24 68 48 8d 54 24 28 48 8b d9 e8 2e f4 ff ff"),
    SIG(0x1400d4df8, "selected area: count Top+0xb58", "48 39 8f 58 0b 00 00"),
    SIG(0x140021b00, "group: set cursor", "48 89 5c 24 08 57 48 83 ec 20 48 8b f9 e8 de 15 00 00 48 8b cf 83 f8 01"),
    SIG(0x140503620, "text lookup (kind, id)", "48 89 6c 24 18 48 89 74 24 20 41 56 48 83 ec 20 48 8b 35 79 36 11 01 8b ea 4c 63 f1"),
    SIG(0x141b78eb2, "Top open: [[0x1416148f0]+0x70]", "48 8b 05 37 ba a9 ff 51 5e 48 8b 48 70"),
    SIG(0x140128c51, "Top open: bonfire manager = [..+0x58]", "48 8b 49 58 48 85 c9"),
    SIG(0x141afd126, "area list: record count u32 M+0x28", "41 8b 45 28"),
    SIG(0x140002187, "area list: record index * 3", "48 8d 0c 40"),
    SIG(0x141c4422c, "area list: records M+0x20, 8 * 3 bytes each", "49 8b 45 20 48 8d 1c c8"),
    SIG(0x141b8c712, "area list: record+8 = bonfire row", "48 8b 43 08"),
    SIG(0x141b76e83, "area list: row+4 = menuAreaID", "8b 40 04"),
    SIG(0x141cf1249, "bonfire list: record+0 = u16 bonfire id", "0f b7 17"),
};
static const Slot slots[] = {
    SLOT(0x1410baa18, 2, 0x1400d4360, "Top vtable: input"),
    SLOT(0x1410baa18, 12, 0x1400d5990, "Top vtable: caption"),
    SLOT(0x1410bab68, 3, 0x1400d4120, "Top secondary vtable: title"),
};

typedef struct AreaEntry { uint32_t area; uint8_t lit, sort; uint16_t pad; uint32_t new_mark; } AreaEntry;
_Static_assert(sizeof(AreaEntry) == 12, "AreaEntry is the game's 12-byte entry");

typedef void *(*CopyAreasFn)(char *dst, char *src);
typedef void (*TopInputFn)(char *top, float dt, uint32_t *ev);
typedef void (*TitleFn)(char *sub, const wchar_t *text);
typedef void (*TopFn)(char *top);
typedef char (*SetCursorFn)(char *group, int index);
typedef const wchar_t *(*GetTextFn)(int kind, int id);

static CopyAreasFn orig_copy_areas;
static TopInputFn orig_top_input;
static TitleFn orig_title;
static int active; // all three hooks in place

// The Travel menu last opened (one at a time: the Top is part of the bonfire menu).
static struct {
    char *top;              // its Top (NULL: none seen)
    int n;                  // areas in the full list
    AreaEntry all[MAX_AREAS];
    uint8_t lordran[MAX_AREAS];
    int count[2];           // areas per page
    int page;               // shown page: 0 Drangleic, 1 Lordran; -1 = unpaged (vanilla)
    uint32_t bonfire;       // the bonfire rested at
    wchar_t vanilla_title[64];
    wchar_t title[128];     // the title we set (static: the setter may keep the pointer)
} g = { .page = -1 };

static AreaEntry *area_entries(char *vector) { return (AreaEntry *)(vector + ((0 - (uintptr_t)vector) & 3)); }

static int hexval(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }

static int check_sig(const Sig *s)
{
    const uint8_t *p = (const uint8_t *)AT(s->va);
    int n = 0, ok = 1;
    for (const char *h = s->bytes; *h;) {
        if (*h == ' ') { h++; continue; }
        int hi = hexval(h[0]), lo = h[1] ? hexval(h[1]) : -1;
        if (hi < 0 || lo < 0) { lr_log(L"travel pages: bad signature text for %hs", s->what); return 0; }
        if (p[n] != (uint8_t)(hi << 4 | lo)) ok = 0;
        n++;
        h += 2;
    }
    if (!ok) {
        wchar_t found[3 * 48 + 1];
        int k = 0;
        for (int i = 0; i < n && i < 48; i++) k += swprintf(found + k, 4, L"%02x ", p[i]);
        found[k ? k - 1 : 0] = 0;
        lr_log(L"travel pages: %hs at exe+%llx differs: found %ls, expected %hs", s->what, (unsigned long long)(s->va - EXE_BASE), found, s->bytes);
    }
    return ok;
}

static uintptr_t *slot_at(uint64_t vt, int index) { return (uintptr_t *)AT(vt) + index; }

static int swap_slot(uint64_t vt, int index, void *hook, void **orig)
{
    uintptr_t *slot = slot_at(vt, index);
    DWORD old;
    if (!VirtualProtect(slot, sizeof *slot, PAGE_READWRITE, &old)) {
        lr_log(L"travel pages: VirtualProtect on vtable exe+%llx failed (%lu)", (unsigned long long)(vt - EXE_BASE), GetLastError());
        return 0;
    }
    *orig = (void *)*slot;
    *slot = (uintptr_t)hook;
    VirtualProtect(slot, sizeof *slot, old, &old);
    return 1;
}

static int is_ds1_bonfire(uint32_t id) { return id >= DS1_BONFIRE_MIN && id <= DS1_BONFIRE_MAX; }

// The bonfire manager M = [[gm+0x70]+0x58] (the Top open builds its lists from it): records at M+0x20, 24 bytes each,
// count u32 M+0x28: +0 u16 bonfire id, +8 MapObjectBonfireParam row (+4 menuAreaID).
static uintptr_t bonfire_manager(void)
{
    uintptr_t gm = *(uintptr_t *)AT(VA_GAME_MANAGER);
    uintptr_t a = gm ? *(uintptr_t *)(gm + 0x70) : 0;
    return a ? *(uintptr_t *)(a + 0x58) : 0;
}

static int area_is_lordran(uintptr_t m, uint32_t area)
{
    if (!m)
        return 0;
    const uint8_t *rec = *(const uint8_t *const *)(m + 0x20);
    uint32_t n = *(const uint32_t *)(m + 0x28);
    if (!rec || n > 4096)
        return 0;
    for (uint32_t i = 0; i < n; i++, rec += 24) {
        const uint8_t *row = *(const uint8_t *const *)(rec + 8);
        if (row && *(const uint32_t *)(row + 4) == area && is_ds1_bonfire(*(const uint16_t *)rec))
            return 1;
    }
    return 0;
}

static const wchar_t *page_name(int page)
{
    const wchar_t *t = ((GetTextFn)AT(VA_GET_TEXT))(TEXT_BOFIRE, PAGE_NAME_TEXT + page);
    return t && t[0] && t[0] != L' ' ? t : page ? L"Lordran" : L"Drangleic"; // absent text: L"" (id 0: L" ")
}

static void compose_title(void)
{
    const wchar_t *name = page_name(g.page);
    if (lr_config.travel_title == 2 && g.vanilla_title[0])
        swprintf(g.title, 128, L"%ls - %ls", name, g.vanilla_title);
    else
        lstrcpynW(g.title, name, 128);
}

// Session 11 (travel12, user crash: "if you haven't discovered a Lordran shrine and try to use the bumpers to see them,
// the game crashes"): the area list builder (0x14017dff0) skips unlit bonfire records unless event flag 201700 (0x313e4,
// tested with 0x1404517d0 before the record's lit bit; trace.py) is set; with it set, every registered map's area is
// listed, also the Lordran areas without a lit DS1 bonfire (entry.lit 0). The Lordran page of only such areas (switched to
// because the page count included them) crashed. The Lordran page now shows only areas with a lit bonfire, RB is refused
// while it has none, and the cursor never lands on an unlit cell from a page switch (the Drangleic page keeps the vanilla
// list, unlit "???" cells included).
static int shown_on(int i, int page)
{
    return g.lordran[i] == page && (page == 0 || g.all[i].lit);
}

// Writes the areas of one page into the Top (full-list order = the game's sort order); returns how many.
static int apply_page(char *top, int page)
{
    char *vector = top + TOP_AREAS;
    AreaEntry *out = area_entries(vector);
    int k = 0;
    for (int i = 0; i < g.n; i++)
        if (shown_on(i, page))
            out[k++] = g.all[i];
    *(uint64_t *)(vector + AREAS_COUNT) = (uint64_t)k;
    return k;
}

static void open_pages(char *top)
{
    g.top = top;
    g.page = -1;
    g.n = 0;
    g.count[0] = g.count[1] = 0;
    g.vanilla_title[0] = 0;
    uint64_t n = *(uint64_t *)(top + TOP_AREAS + AREAS_COUNT);
    uintptr_t owner = *(uintptr_t *)(top + TOP_OWNER);
    g.bonfire = owner ? *(uint32_t *)(owner + OWNER_BONFIRE) : 0;
    if (n == 0 || n > MAX_AREAS) {
        lr_log(L"travel pages: Travel opened at bonfire %u with %llu areas; not paged", g.bonfire, (unsigned long long)n);
        return;
    }
    g.n = (int)n;
    memcpy(g.all, area_entries(top + TOP_AREAS), (size_t)g.n * sizeof(AreaEntry));
    uintptr_t m = bonfire_manager();
    wchar_t list[512];
    int k = 0;
    for (int i = 0; i < g.n; i++) {
        g.lordran[i] = (uint8_t)area_is_lordran(m, g.all[i].area);
        if (shown_on(i, g.lordran[i]))
            g.count[g.lordran[i]]++;
        if (k < 480) // D = Drangleic, L = Lordran, l = Lordran without a lit bonfire (not shown)
            k += swprintf(list + k, 512 - k, L" %u%ls", g.all[i].area, g.lordran[i] ? (g.all[i].lit ? L"L" : L"l") : L"D");
    }
    list[k] = 0;
    int page = is_ds1_bonfire(g.bonfire);
    if (!g.count[page])
        page ^= 1;
    g.page = page;
    int shown = apply_page(top, page);
    lr_log(L"travel pages: Travel opened at bonfire %u: %d areas (%d Drangleic, %d Lordran shown)%ls%ls -> page %ls, %d shown",
           g.bonfire, g.n, g.count[0], g.count[1], m ? L":" : L" (no bonfire manager: all Drangleic)", list, page ? L"Lordran" : L"Drangleic", shown);
}

// LB / Shift+Left -> Drangleic (page 0, the left tab), RB / Shift+Right -> Lordran (page 1, the right tab), matching the
// key guide "<LB><RB>: Drangleic/Lordran" (bofire.fmg 89901, TravelPages.cs). The bumper of the page already shown does nothing.
static void switch_page(char *top, int to)
{
    const wchar_t *names[2] = { L"Drangleic", L"Lordran" };
    if (to == g.page) {
        lr_log(L"travel pages: %ls pressed, already on %ls", to ? L"RB" : L"LB", names[to]);
        return;
    }
    if (!g.count[to]) {
        lr_log(L"travel pages: %ls: no lit %ls areas, staying on %ls", to ? L"RB" : L"LB", names[to], names[g.page]);
        return;
    }
    g.page = to;
    int shown = apply_page(top, to);
    int first_lit = 0;
    for (AreaEntry *e = area_entries(top + TOP_AREAS); first_lit < shown && !e[first_lit].lit; first_lit++)
        ;
    if (first_lit >= shown)
        first_lit = 0;
    // the order of the game's own cell change (0x1400d4360's mouse path): cursor, then rebuild (item count, cells,
    // bonfire list of the selected cell); the caption follows the selected cell; the title names the page
    ((SetCursorFn)AT(VA_SET_CURSOR))(top, first_lit);
    ((TopFn)AT(VA_TOP_REBUILD))(top);
    ((TopFn)AT(VA_TOP_CAPTION))(top);
    if (lr_config.travel_title) {
        compose_title();
        orig_title(top + TOP_SUB, g.title);
    }
    lr_log(L"travel pages: %ls -> page %ls, %d areas", to ? L"RB" : L"LB", names[to], shown);
}

static void *hk_copy_areas(char *dst, char *src)
{
    void *r = orig_copy_areas(dst, src);
    char *top = dst - TOP_AREAS;
    if (active && *(uintptr_t *)top == AT(VA_TOP_VFT))
        open_pages(top);
    return r;
}

static void hk_top_input(char *top, float dt, uint32_t *ev)
{
    if (active && ev && top == g.top && g.page >= 0 && *(uint64_t *)(top + TOP_CHILDREN) == 0 && ((ev[0] | ev[1] | ev[2]) & MENU_PAGES)) {
        if ((ev[0] & MENU_PAGES) == MENU_LB)
            switch_page(top, 0);
        else if ((ev[0] & MENU_PAGES) == MENU_RB)
            switch_page(top, 1);
        // the bumpers and, while one is down, the directions are not passed on: a Shift+arrow chord must not also move
        // the cursor, and the grid group gets no LB/RB of its own
        uint32_t e[7];
        memcpy(e, ev, sizeof e);
        e[0] &= ~(MENU_PAGES | MENU_DIRS);
        e[1] &= ~(MENU_PAGES | MENU_DIRS);
        e[2] &= ~MENU_PAGES;
        orig_top_input(top, dt, e);
        return;
    }
    orig_top_input(top, dt, ev);
}

static void hk_title(char *sub, const wchar_t *text)
{
    if (active && lr_config.travel_title && sub - TOP_SUB == g.top && g.page >= 0) {
        if (text && text != g.title)
            lstrcpynW(g.vanilla_title, text, 64);
        compose_title();
        orig_title(sub, g.title);
        return;
    }
    orig_title(sub, text);
}

static void install(void)
{
    if (!lr_config.travel_pages) {
        lr_log(L"travel pages: off (loader.ini travel_pages=0)");
        return;
    }
    int ok = 1;
    for (size_t i = 0; i < sizeof sigs / sizeof *sigs; i++)
        ok &= check_sig(&sigs[i]);
    for (size_t i = 0; i < sizeof slots / sizeof *slots; i++) {
        uintptr_t v = *slot_at(slots[i].vt, slots[i].index);
        if (v != AT(slots[i].fn)) {
            lr_log(L"travel pages: %hs (exe+%llx slot %d) holds exe+%llx, expected exe+%llx", slots[i].what,
                   (unsigned long long)(slots[i].vt - EXE_BASE), slots[i].index, (unsigned long long)(v - lr_base),
                   (unsigned long long)(slots[i].fn - EXE_BASE));
            ok = 0;
        }
    }
    if (!ok) {
        lr_log(L"travel pages: DISABLED (not the DarkSoulsII.exe v1.0.3.0 the RE was done on, or another mod changed the Travel menu)");
        return;
    }
    MH_STATUS s = MH_Initialize();
    void *copy = (void *)AT(VA_COPY_AREAS);
    if ((s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) || (s = MH_CreateHook(copy, (void *)hk_copy_areas, (void **)&orig_copy_areas)) != MH_OK ||
        (s = MH_EnableHook(copy)) != MH_OK) {
        lr_log(L"travel pages: DISABLED (hooking the area list copy failed: MinHook %d)", (int)s);
        return;
    }
    if (!swap_slot(VA_TOP_VFT, SLOT_INPUT, (void *)hk_top_input, (void **)&orig_top_input) ||
        !swap_slot(VA_TOP_VFT2, SLOT2_TITLE, (void *)hk_title, (void **)&orig_title)) {
        lr_log(L"travel pages: DISABLED (vtable swap failed; the copy hook stays inert)");
        return;
    }
    active = 1;
    lr_log(L"travel pages: on (exe base %p; %d code signatures and %d vtable slots match; hooks: area copy exe+%llx (MinHook), Top input slot %d, title slot %d; travel_title=%d)",
           (void *)lr_base, (int)(sizeof sigs / sizeof *sigs), (int)(sizeof slots / sizeof *slots), (unsigned long long)(VA_COPY_AREAS - EXE_BASE),
           SLOT_INPUT, SLOT2_TITLE, lr_config.travel_title);
}

// Called from the keyboard poll (autopilot.c), every frame on the game thread; installs once.
void lr_travel_tick(void)
{
    static int done;
    if (done)
        return;
    done = 1;
    install();
}
