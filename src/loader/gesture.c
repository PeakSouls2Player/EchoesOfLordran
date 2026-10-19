// Drangleic / Lordran pages in DS2's gesture menu (session 11, track gest11; user: "Also include the poses. Add them to the
// pose menu same style as the travel menu where they can switch around etc.").
//
// Static RE (decompile decomp\ds2s + the unpacked exe; data\sandbox\gest11\NOTES.md):
//  - The gesture menu's grid is FeGroupInGameMenuGestureTop ("Top", primary vtable 0x1410b1428, ctor ~0x14006d9xx). Its 8
//    cells are DS2's 8 gesture shortcut slots = equip slots 0x2c..0x33 of the gesture inventory (list 6). Slot 12
//    (0x14006fa10, cursor moved) refills the cells when the byte Top+0xf9 is set: count = 0x140035740(inv, 6), per cell i
//    < count the icon = 0x140035e10(0x140035600(inv, out, 0x2c + i)) ({kind 6, iconId}); then the caption (FLO element
//    0x1eace4/0x1eac81/0x1eac46/0x5f5b9f2 under Top+8, element vtable +0x140 = set text) = ingamemenu (text kind 7) name
//    of the slot at the cursor Top+0x28. Slot 16 (0x14006ec50, "Activate") = 0x14006ee30 (play the slot: 0x140098c20 →
//    category of the equipped gesture item; sound 2, or 0x5f5e119 for an empty slot) + close the menu (0x1404fec90 on
//    [gm+0x22e0]); slot 18 (0x14006ede0 = jmp 0x14006ee30, "Test now") plays without closing; slot 20 (0x14006edf0,
//    "Change") opens the List (Select) and sets Top+0x1d; slot 2 (0x14006ea60, input) zeroes the event while Top+0x1d is
//    set; slot 25 (0x14006f0a0) opens: title 0x1eac45 = ingamemenu 19010000 "Gestures", key guide 0x1eac47 = 19019000.
//  - Playing a gesture by category: 0x140098c80(category) → 0x1401409b0 writes [chr+0xb8]+0x730 = category and sets bit
//    0x2000 of +0x4c0; the EventAction component then sets the network CP GestureCategory and fires request Gesture.
//    The pack's c0001 network answers DS1 categories 1000.. (Build/Chr/Ds1Gestures.cs, rule GS1).
// What this module does (loader.ini gesture_pages=1, the default; needs <pack>\lordran\gestures.ini from build-pack):
//  - LB (Shift+Left) / RB (Shift+Right) while the grid has focus move between the Drangleic page (page 0: vanilla, every
//    vanilla code path untouched) and the Lordran pages (the learned DS1 gestures in DS1's order, 8 per page);
//  - on a Lordran page the cell refill runs with a guard (this thread, inside the Top's slot 12 only) under which three
//    MinHooks answer for the Lordran cells: the list-6 count = gestures on the page, a cell's handle = 0xF000 | cell, its
//    icon = {6, the gesture's iconId}; the caption, title ("Gestures - Lordran") and key guide (no "Change") are set with
//    the game's own element setter; Activate / Test now play the DS1 category with 0x140098c80 (the List's own "Activate"
//    path) and the game's sounds; Change is refused (error sound): Lordran cells are not shortcut slots;
//  - learned = the gesture's flag (0x1404517d0) or flag 0 (DS1's six starting gestures) or unlock_all (--test-gestures).
// Safety: like travel.c, every address is rebased on lr_base and byte-checked (SIG/SLOT table, also parsed by
// data\sandbox\gest11\harness\gesture_sigcheck.py); one mismatch, a missing table or a failed hook disables the feature
// (vanilla menu). Nothing is written to the save: pages and paging live in the loader; learned state is the game's flags.
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#include <stdio.h>

#define EXE_BASE 0x140000000ull
#define AT(va) (lr_base + (uintptr_t)((va) - EXE_BASE))

#define VA_GAME_MANAGER 0x1416148f0ull
#define VA_TOP_VFT      0x1410b1428ull // FeGroupInGameMenuGestureTop, primary vtable
#define VA_TOP_INPUT    0x14006ea60ull // slot 2: void (Top *, float dt, uint32_t ev[7])
#define VA_TOP_FILL     0x14006fa10ull // slot 12: void (Top *): cells (if Top+0xf9) + caption
#define VA_TOP_DECIDE   0x14006ec50ull // slot 16: Activate (play + close)
#define VA_TOP_TEST     0x14006ede0ull // slot 18: Test now (play)
#define VA_TOP_CHANGE   0x14006edf0ull // slot 20: Change (open the List)
#define VA_TOP_OPEN     0x14006f0a0ull // slot 25: open
#define VA_LIST_COUNT   0x140035740ull // u16 (inv, u8 list)
#define VA_SLOT_HANDLE  0x140035600ull // u8 *(inv, u8 *out, u32 equip slot)
#define VA_HANDLE_ICON  0x140035e10ull // u32 *(u8 *handle, u32 *out {kind, id})
#define VA_PLAY_CAT     0x140098c80ull // void (int gesture category)
#define VA_FLAG_GET     0x1404517d0ull // bool (int event flag)
#define VA_SOUND_PLAY   0x140040e70ull // void (void *buf, int sound)
#define VA_SOUND_DONE   0x140afe4a0ull // void (void *buf)
#define VA_MENU_CLOSE   0x1404fec90ull // void ([gm+0x22e0])
#define VA_CMD_DONE     0x140021ad0ull // void (group *): the end of the vanilla Change command
#define VA_FLO_FIND     0x140afda00ull // void *(root, id1, id2, id3, id4, 0 x6)
#define VA_SET_CURSOR   0x140021b00ull // char (group *, int index)
#define VA_GET_TEXT     0x140503620ull // const wchar_t *(int kind, int id)
#define SLOT_INPUT  2
#define SLOT_FILL   12
#define SLOT_VALID  9   // the Top's "can act" predicate (+0x48), called by the vanilla commands
#define SLOT_DECIDE 16
#define SLOT_TEST   18
#define SLOT_CHANGE 20
#define SLOT_OPEN   25

#define TOP_FLO      0x08
#define TOP_LISTOPEN 0x1d  // byte: the List (Select) is open
#define TOP_CURSOR   0x28
#define TOP_CHILDREN 0xc0  // u64: children on the Top's group stack
#define TOP_DIRTY    0xf9  // byte: slot 12 refills the cells
#define GM_MENU      0x22e0

#define GESTURE_LIST  6
#define SLOT_FIRST    0x2c // equip slot of cell 0 (0x14034e2c0: lea eax, [rcx+0x2c])
#define CELLS         8
#define FAKE_HANDLE   0xF000u
#define ICON_KIND     6
#define SOUND_OK      2
#define SOUND_ERROR   0x5f5e119
#define TEXT_MENU     7          // ingamemenu.fmg
#define TEXT_TITLE    19010000   // "Gestures"
#define TEXT_GUIDE    19019000   // the Top's key guide (the pack adds the LB/RB hint)
#define FLO_ROOT      0x1eace4
#define FLO_PANE      0x1eac81
#define FLO_TITLE     0x1eac45
#define FLO_CAPTION   0x1eac46
#define FLO_GUIDE     0x1eac47
#define FLO_TEXT      0x5f5b9f2

#define MENU_LB    0x40000u
#define MENU_RB    0x80000u
#define MENU_PAGES (MENU_LB | MENU_RB)
#define MENU_DIRS  0xffu

typedef struct Sig { uint64_t va; const char *what; const char *bytes; } Sig;
typedef struct Slot { uint64_t vt; int index; uint64_t fn; const char *what; } Slot;
#define SIG(va, what, bytes) { va, what, bytes }
#define SLOT(vt, index, fn, what) { vt, index, fn, what }
static const Sig sigs[] = {
    SIG(0x14006ea60, "Top input: List open = Top+0x1d", "48 83 ec 68 48 8b 05 e5 31 57 01 48 33 c4 48 89 44 24 50 80 79 1d 00 74 31"),
    SIG(0x14006eaaa, "Top input: group input on Top+0x58", "48 83 c1 58 e8 3d 87 fb ff"),
    SIG(0x14002722c, "group input: child stack at +0x18", "48 8d 4b 18"),
    SIG(0x140023f5b, "child stack: count at +0x50", "48 83 79 50 00"),
    SIG(0x14006fa10, "Top fill: inventory [[gm+0x22e0]+0x130], dirty Top+0xf9, count of list 6", "4c 8b dc 55 53 56 49 8d ab 68 fd ff ff 48 81 ec 80 03 00 00 48 8b 05 c5 4e 5a 01 48 8b d9 48 8b 90 e0 22 00 00 48 8b b2 30 01 00 00 48 85 f6 0f 84 fd 02 00 00 49 89 7b 08 4d 89 73 10 45 33 f6 44 38 b1 f9 00 00 00 0f 84 40 02 00 00 44 88 b1 f9 00 00 00 b2 06 48 8b ce e8 d2 5c fc ff"),
    SIG(0x14006fc0b, "Top fill: cell icon = handle_icon(slot_handle(inv, out, 0x2c + i))", "8b 8d b0 02 00 00 e8 aa e6 2d 00 48 8b 4d 78 48 8d 95 b8 02 00 00 48 89 4c 24 78 44 8b c0 48 8b ce 44 89 75 80 c7 45 84 01 00 00 00 e8 c4 59 fc ff 48 8d 55 20 48 8b c8 e8 c8 61 fc ff"),
    SIG(0x14006fcbe, "Top fill: caption element, cursor Top+0x28, text kind 7, element +0x140", "ba e4 ac 1e 00 44 89 74 24 30 44 8d 42 9d 41 b9 46 ac 1e 00 44 89 74 24 28 c7 44 24 20 f2 b9 f5 05 e8 1c dd a8 00 48 8b f8 48 85 c0 74 46 48 8d 4d d0 e8 bb 40 fc ff 44 8b 4b 28 48 8d 55 d0 41 b0 06 48 8b ce e8 58 59 fc ff 48 8d 4d d0 ba 44 00 00 00 e8 9a 75 fc ff b9 07 00 00 00 8b d0 e8 fe 38 49 00 4c 8b 07 48 8b cf 48 8b d0 41 ff 90 40 01 00 00"),
    SIG(0x14006f0a0, "Top open: FLO root Top+8", "48 8b c4 48 89 70 20 57 48 83 ec 60 48 8b 71 08 48 8b f9 48 85 f6"),
    SIG(0x14006f188, "Top open: title element = ingamemenu 19010000", "ba e4 ac 1e 00 89 6c 24 30 44 8d 42 9d 41 b9 45 ac 1e 00 48 8b ce 89 6c 24 28 c7 44 24 20 f2 b9 f5 05 e8 51 e8 a8 00 4c 8b f0 48 85 c0 74 1e 48 8b 18 ba d0 11 22 01 b9 07 00 00 00 e8 57 44 49 00 49 8b ce 48 8b d0 ff 93 40 01 00 00"),
    SIG(0x14006f235, "Top open: key-guide element = ingamemenu 19019000", "ba e4 ac 1e 00 89 6c 24 30 44 8d 42 9d 41 b9 47 ac 1e 00 48 8b ce 89 6c 24 28 c7 44 24 20 f2 b9 f5 05 e8 a4 e7 a8 00 4c 8b f0 48 85 c0 74 1e 48 8b 18 ba f8 34 22 01 b9 07 00 00 00 e8 aa 43 49 00 49 8b ce 48 8b d0 ff 93 40 01 00 00"),
    SIG(0x14006ec50, "Top Activate: play, predicate +0x48, close [gm+0x22e0]", "40 53 48 83 ec 20 48 8b d9 e8 d2 01 00 00 48 8b 03 48 8b cb ff 50 48 84 c0 74 18 48 8b 0d 7e 5c 5a 01 48 8b 89 e0 22 00 00 48 83 c4 20 5b e9 0d 00 49 00 48 83 c4 20 5b c3"),
    SIG(0x14006ede0, "Top Test now = jmp play", "e9 4b 00 00 00"),
    SIG(0x14006edf0, "Top Change: open the List, Top+0x1d = 1, command end", "40 53 48 83 ec 20 48 8b 01 48 8b d9 ff 50 48 84 c0 74 17 48 8b 8b 00 01 00 00 48 81 c1 08 71 00 00 e8 9a 72 09 00 c6 43 1d 01 48 8b cb 48 83 c4 20 5b e9 a9 2c fb ff"),
    SIG(0x14006ee4b, "Top play: predicate +0x48", "48 8b 01 48 8b f9 ff 50 48 84 c0 0f 84 a8 00 00 00"),
    SIG(0x14006ee8d, "Top play: list-6 item at the cursor Top+0x28", "44 8b 4f 28 48 8d 54 24 20 41 b0 06 48 8b cb e8 bf 67 fc ff"),
    SIG(0x14006eedc, "Top play: play slot, sounds 2 / 0x5f5e119 (buffer, play, done)", "8b 4f 28 e8 3c 9d 02 00 ba 02 00 00 00 eb 05 ba 19 e1 f5 05 48 8d 4c 24 70 e8 76 1f fd ff 48 8d 4c 24 70 e8 9c f5 a8 00"),
    SIG(0x140035600, "slot handle (inv, out, equip slot)", "40 53 48 83 ec 20 48 8b 05 e3 f2 5d 01 48 8b da 48 8b 88 a8 00 00 00 48 85 c9 74 30 48 8b 49 10 48 85 c9 74 27 41 8b d0 e8 d3 65 17 00"),
    SIG(0x140035740, "list count (inv, list)", "88 54 24 10 53 48 83 ec 20 48 8b 05 a0 f1 5d 01 48 8b 88 a8 00 00 00 48 85 c9 74 27 48 8b 59 10 48 85 db 74 1e 48 8d 4c 24 38 e8 c1 78 17 00"),
    SIG(0x140035e10, "handle icon (handle, out)", "40 53 48 83 ec 70 48 8b da 48 8d 54 24 20 e8 ad 64 00 00 48 83 78 30 00 74 32"),
    SIG(0x14034e2c0, "cell -> equip slot + 0x2c", "8d 41 2c c3"),
    SIG(0x140098c80, "play gesture by category", "40 53 48 83 ec 20 48 8b 05 63 bc 57 01 8b d9 48 8b 88 d0 00 00 00 48 85 c9 74 07 48 8b 89 80 04 00 00 48 85 c9 74 1d 48 8b 01 ff 90 c0 00 00 00"),
    SIG(0x1401409b0, "gesture request: [chr+0xb8]+0x730 = category, +0x4c0 |= 0x2000", "48 89 5c 24 08 57 48 83 ec 20 48 8b f9 48 8b 09 8b da 48 8b 81 c0 00 00 00 44 0f b6 40 2a 41 f6 c0 02 74 32 41 80 e0 1a 41 80 f8 1a 74 28 e8 fd 99 01 00 84 c0 75 1f 85 db 78 1b 48 8b 07 48 8b 88 b8 00 00 00 89 99 30 07 00 00 48 81 89 c0 04 00 00 00 20 00 00"),
    SIG(0x1404517d0, "event flag (id)", "85 c9 74 1b 48 8b 05 15 31 1c 01 4c 8b 40 70 4d 85 c0 74 0b 8b d1 49 8b 48 20 e9 41 2a 02 00 32 c0 c3"),
    SIG(0x140040e70, "menu sound (buffer, id)", "48 89 5c 24 18 57 48 81 ec 40 01 00 00 48 8b 05 cc 0d 5a 01 48 33 c4 48 89 84 24 30 01 00 00 48 8b f9 48 8d 4c 24 20 8b da"),
    SIG(0x140afe4a0, "menu sound buffer done", "48 8d 05 a9 7f 6d 00 48 89 01 c3"),
    SIG(0x1404fec90, "menu close", "48 8b 81 f8 00 00 00 48 85 c0 74 0e 83 a1 b8 03 00 00 fd c6 80 32 0d 00 00 01 f3 c3"),
    SIG(0x140021ad0, "command end", "48 83 ec 28 83 b9 c8 00 00 00 00 74 19 8b 51 28 e8 7b 65 0e 00 48 85 c0 74 0c 48 8b c8 48 83 c4 28 e9 8a 8c 0e 00 48 83 c4 28 c3"),
    SIG(0x140afda00, "FLO element lookup", "48 83 ec 58 48 8b 05 45 42 ae 00 48 33 c4 48 89 44 24 48 8b 84 24 80 00 00 00 4c 8b d1 44 89 44 24 24 89 44 24 2c 8b 84 24 88 00 00 00"),
    SIG(0x140021b00, "group: set cursor", "48 89 5c 24 08 57 48 83 ec 20 48 8b f9 e8 de 15 00 00 48 8b cf 83 f8 01"),
    SIG(0x140503620, "text lookup (kind, id)", "48 89 6c 24 18 48 89 74 24 20 41 56 48 83 ec 20 48 8b 35 79 36 11 01 8b ea 4c 63 f1"),
};
static const Slot slots[] = {
    SLOT(0x1410b1428, 2, 0x14006ea60, "Top vtable: input"),
    SLOT(0x1410b1428, 12, 0x14006fa10, "Top vtable: fill + caption"),
    SLOT(0x1410b1428, 16, 0x14006ec50, "Top vtable: Activate"),
    SLOT(0x1410b1428, 18, 0x14006ede0, "Top vtable: Test now"),
    SLOT(0x1410b1428, 20, 0x14006edf0, "Top vtable: Change"),
    SLOT(0x1410b1428, 25, 0x14006f0a0, "Top vtable: open"),
};

typedef void (*TopInputFn)(char *top, float dt, uint32_t *ev);
typedef void (*TopFn)(char *top);
typedef char (*TopPredFn)(char *top);
typedef uint16_t (*ListCountFn)(void *inv, uint8_t list);
typedef uint8_t *(*SlotHandleFn)(void *inv, uint8_t *out, uint32_t slot);
typedef uint32_t *(*HandleIconFn)(uint8_t *handle, uint32_t *out);
typedef void (*PlayFn)(int category);
typedef char (*FlagFn)(int flag);
typedef void (*SoundFn)(void *buf, int id);
typedef void (*SoundDoneFn)(void *buf);
typedef void (*CloseFn)(void *menu);
typedef void *(*FloFindFn)(void *root, int a, int b, int c, int d, int e, int f, int g, int h, int i, int j);
typedef void (*SetTextFn)(void *elem, const wchar_t *text);
typedef char (*SetCursorFn)(char *group, int index);
typedef const wchar_t *(*GetTextFn)(int kind, int id);

static TopInputFn orig_input;
static TopFn orig_fill, orig_decide, orig_test, orig_change, orig_open;
static ListCountFn orig_count;
static SlotHandleFn orig_handle;
static HandleIconFn orig_icon;
static int active;

#define MAX_GESTURES 32
typedef struct Gesture { int ds1, category, text, icon, flag; } Gesture;
static struct {
    int n, unlock_all, page_text, guide_text;
    Gesture all[MAX_GESTURES];
} table;

static struct {
    char *top;
    int page;                // 0 Drangleic (vanilla), 1.. Lordran pages
    int pages;               // Lordran pages (learned / 8, rounded up)
    int learned[MAX_GESTURES], nlearned;
    int shown[CELLS], nshown; // table indices on the current Lordran page
    wchar_t title[96], caption[96];
} g;

static volatile DWORD fill_thread; // the guard: the Lordran cells are answered only inside our slot-12 call, on its thread

static int hexval(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }

static int check_sig(const Sig *s)
{
    const uint8_t *p = (const uint8_t *)AT(s->va);
    int n = 0, ok = 1;
    for (const char *h = s->bytes; *h;) {
        if (*h == ' ') { h++; continue; }
        int hi = hexval(h[0]), lo = h[1] ? hexval(h[1]) : -1;
        if (hi < 0 || lo < 0) { lr_log(L"gesture pages: bad signature text for %hs", s->what); return 0; }
        if (p[n] != (uint8_t)(hi << 4 | lo)) ok = 0;
        n++;
        h += 2;
    }
    if (!ok)
        lr_log(L"gesture pages: %hs at exe+%llx differs from DarkSoulsII.exe v1.0.3.0", s->what, (unsigned long long)(s->va - EXE_BASE));
    return ok;
}

static uintptr_t *slot_at(uint64_t vt, int index) { return (uintptr_t *)AT(vt) + index; }

static int swap_slot(uint64_t vt, int index, void *hook, void **orig)
{
    uintptr_t *slot = slot_at(vt, index);
    DWORD old;
    if (!VirtualProtect(slot, sizeof *slot, PAGE_READWRITE, &old))
        return 0;
    *orig = (void *)*slot;
    *slot = (uintptr_t)hook;
    VirtualProtect(slot, sizeof *slot, old, &old);
    return 1;
}

static int is_top(char *top) { return active && top && *(uintptr_t *)top == AT(VA_TOP_VFT); }

static int learned(const Gesture *x)
{
    return table.unlock_all || x->flag == 0 || ((FlagFn)AT(VA_FLAG_GET))(x->flag);
}

static void refresh_learned(void)
{
    g.nlearned = 0;
    for (int i = 0; i < table.n; i++)
        if (learned(&table.all[i]))
            g.learned[g.nlearned++] = i;
    g.pages = (g.nlearned + CELLS - 1) / CELLS;
    if (g.page > g.pages)
        g.page = g.pages;
    g.nshown = 0;
    if (g.page > 0)
        for (int k = (g.page - 1) * CELLS; k < g.nlearned && g.nshown < CELLS; k++)
            g.shown[g.nshown++] = g.learned[k];
}

static const wchar_t *text(int id, const wchar_t *fallback)
{
    const wchar_t *t = id ? ((GetTextFn)AT(VA_GET_TEXT))(TEXT_MENU, id) : NULL;
    return t && t[0] && t[0] != L' ' ? t : fallback; // absent text: L"" (id 0: L" ")
}

static void set_element(char *top, int id, const wchar_t *s)
{
    void *root = *(void **)(top + TOP_FLO);
    if (!root || !s)
        return;
    void *e = ((FloFindFn)AT(VA_FLO_FIND))(root, FLO_ROOT, FLO_PANE, id, FLO_TEXT, 0, 0, 0, 0, 0, 0);
    if (e)
        ((SetTextFn)(*(void ***)e)[0x140 / 8])(e, s);
}

// Title "Gestures - Drangleic" / "Gestures - Lordran (1/2)" and the key guide of the page.
static void page_texts(char *top)
{
    const wchar_t *page = g.page ? text(table.page_text + 1, L"Lordran") : text(table.page_text, L"Drangleic");
    if (g.page && g.pages > 1)
        swprintf(g.title, 96, L"%ls - %ls (%d/%d)", text(TEXT_TITLE, L"Gestures"), page, g.page, g.pages);
    else
        swprintf(g.title, 96, L"%ls - %ls", text(TEXT_TITLE, L"Gestures"), page);
    set_element(top, FLO_TITLE, g.title);
    set_element(top, FLO_GUIDE, g.page ? text(table.guide_text, text(TEXT_GUIDE, L"")) : text(TEXT_GUIDE, L""));
}

static void sound(int id)
{
    __attribute__((aligned(16))) uint8_t buf[64];
    memset(buf, 0, sizeof buf);
    ((SoundFn)AT(VA_SOUND_PLAY))(buf, id);
    ((SoundDoneFn)AT(VA_SOUND_DONE))(buf);
}

static int can_act(char *top) { return ((TopPredFn)(*(void ***)top)[SLOT_VALID])(top) != 0; }

// The Lordran page's play (0x14006ee30's role): the gesture under the cursor by its category, sound 2; else the error sound.
static void play_cell(char *top)
{
    if (!can_act(top))
        return;
    int cur = *(int *)(top + TOP_CURSOR);
    if (cur >= 0 && cur < g.nshown) {
        const Gesture *x = &table.all[g.shown[cur]];
        ((PlayFn)AT(VA_PLAY_CAT))(x->category);
        sound(SOUND_OK);
        lr_log(L"gesture pages: play DS1 %d (category %d)", x->ds1, x->category);
    } else
        sound(SOUND_ERROR);
}

// ---- the three guarded hooks (inside our slot-12 call only)
static int guarded(void) { return fill_thread && fill_thread == GetCurrentThreadId(); }

static uint16_t hk_count(void *inv, uint8_t list)
{
    if (guarded() && list == GESTURE_LIST)
        return (uint16_t)g.nshown;
    return orig_count(inv, list);
}

static uint8_t *hk_handle(void *inv, uint8_t *out, uint32_t slot)
{
    if (guarded() && slot >= SLOT_FIRST && slot < SLOT_FIRST + CELLS) {
        uint32_t i = slot - SLOT_FIRST;
        *(uint16_t *)(out + 4) = 0;
        out[0] = 1;
        *(uint16_t *)(out + 2) = (uint16_t)(i < (uint32_t)g.nshown ? FAKE_HANDLE | i : 0xffff);
        return out;
    }
    return orig_handle(inv, out, slot);
}

static uint32_t *hk_icon(uint8_t *handle, uint32_t *out)
{
    if (guarded() && handle && (*(uint16_t *)(handle + 2) & 0xfff0) == FAKE_HANDLE) {
        int i = *(uint16_t *)(handle + 2) & 0xf;
        if (i < g.nshown) {
            out[0] = ICON_KIND;
            out[1] = (uint32_t)table.all[g.shown[i]].icon;
        } else
            out[0] = out[1] = 0;
        return out;
    }
    return orig_icon(handle, out);
}

// ---- Top vtable slots
static void hk_fill(char *top)
{
    if (!is_top(top) || g.page <= 0) {
        orig_fill(top);
        return;
    }
    g.top = top;
    fill_thread = GetCurrentThreadId();
    orig_fill(top);
    fill_thread = 0;
    int cur = *(int *)(top + TOP_CURSOR);
    const wchar_t *name = cur >= 0 && cur < g.nshown ? text(table.all[g.shown[cur]].text, L"") : L"";
    lstrcpynW(g.caption, name, 96);
    set_element(top, FLO_CAPTION, g.caption);
}

static void switch_page(char *top, int to)
{
    refresh_learned();
    if (to < 0 || to > g.pages || to == g.page) {
        lr_log(L"gesture pages: %ls: staying on page %d of %d", to < g.page ? L"LB" : L"RB", g.page, g.pages);
        return;
    }
    g.page = to;
    refresh_learned();
    *(uint8_t *)(top + TOP_DIRTY) = 1;
    ((SetCursorFn)AT(VA_SET_CURSOR))(top, 0);
    hk_fill(top);
    page_texts(top);
    lr_log(L"gesture pages: -> page %d of %d (%ls), %d gestures shown", g.page, g.pages, g.page ? L"Lordran" : L"Drangleic", g.page ? g.nshown : -1);
}

static void hk_input(char *top, float dt, uint32_t *ev)
{
    if (is_top(top) && ev && !*(uint8_t *)(top + TOP_LISTOPEN) && *(uint64_t *)(top + TOP_CHILDREN) == 0 && ((ev[0] | ev[1] | ev[2]) & MENU_PAGES)) {
        g.top = top;
        if ((ev[0] & MENU_PAGES) == MENU_LB)
            switch_page(top, g.page - 1);
        else if ((ev[0] & MENU_PAGES) == MENU_RB)
            switch_page(top, g.page + 1);
        uint32_t e[7];
        memcpy(e, ev, sizeof e);
        e[0] &= ~(MENU_PAGES | MENU_DIRS);
        e[1] &= ~(MENU_PAGES | MENU_DIRS);
        e[2] &= ~MENU_PAGES;
        orig_input(top, dt, e);
        return;
    }
    orig_input(top, dt, ev);
}

static void hk_open(char *top)
{
    orig_open(top);
    if (!is_top(top))
        return;
    g.top = top;
    refresh_learned();
    page_texts(top); // the open sets +0xf9: the next slot-12 call fills the remembered page
    lr_log(L"gesture pages: menu opened on page %d of %d; DS1 gestures learned %d of %d%ls", g.page, g.pages, g.nlearned, table.n, table.unlock_all ? L" (unlock_all)" : L"");
}

static void hk_decide(char *top)
{
    if (!is_top(top) || g.page <= 0) {
        orig_decide(top);
        return;
    }
    play_cell(top);
    if (can_act(top)) {
        uintptr_t gm = *(uintptr_t *)AT(VA_GAME_MANAGER);
        void *menu = gm ? *(void **)(gm + GM_MENU) : NULL;
        if (menu)
            ((CloseFn)AT(VA_MENU_CLOSE))(menu);
    }
}

static void hk_test(char *top)
{
    if (!is_top(top) || g.page <= 0) {
        orig_test(top);
        return;
    }
    play_cell(top);
}

static void hk_change(char *top)
{
    if (!is_top(top) || g.page <= 0) {
        orig_change(top);
        return;
    }
    if (can_act(top))
        sound(SOUND_ERROR); // Lordran cells are not shortcut slots
    ((TopFn)AT(VA_CMD_DONE))(top);
}

static int load_table(void)
{
    wchar_t ini[MAX_PATH];
    if (!lr_pack_dir[0])
        return 0;
    swprintf(ini, MAX_PATH, L"%ls%lslordran\\gestures.ini", lr_pack_dir, lr_pack_dir[lstrlenW(lr_pack_dir) - 1] == L'\\' ? L"" : L"\\");
    if (GetFileAttributesW(ini) == INVALID_FILE_ATTRIBUTES || GetPrivateProfileIntW(L"gestures", L"version", 0, ini) != 1) {
        lr_log(L"gesture pages: no %ls (version 1): the pack was built without DS1 gestures", ini);
        return 0;
    }
    int n = (int)GetPrivateProfileIntW(L"gestures", L"count", 0, ini);
    if (n <= 0 || n > MAX_GESTURES)
        return 0;
    table.n = n;
    table.unlock_all = (int)GetPrivateProfileIntW(L"gestures", L"unlock_all", 0, ini);
    table.page_text = (int)GetPrivateProfileIntW(L"gestures", L"page_text", 0, ini);
    table.guide_text = (int)GetPrivateProfileIntW(L"gestures", L"lordran_guide_text", 0, ini);
    for (int i = 0; i < n; i++) {
        wchar_t sec[16];
        swprintf(sec, 16, L"g%d", i);
        Gesture *x = &table.all[i];
        x->ds1 = (int)GetPrivateProfileIntW(sec, L"ds1", 0, ini);
        x->category = (int)GetPrivateProfileIntW(sec, L"category", -1, ini);
        x->text = (int)GetPrivateProfileIntW(sec, L"text", 0, ini);
        x->icon = (int)GetPrivateProfileIntW(sec, L"icon", 0, ini);
        x->flag = (int)GetPrivateProfileIntW(sec, L"flag", 0, ini);
        if (x->category < 1000 || x->icon <= 0) {
            lr_log(L"gesture pages: %ls [%ls] is incomplete", ini, sec);
            return 0;
        }
    }
    return 1;
}

static void install(void)
{
    if (!lr_config.gesture_pages) {
        lr_log(L"gesture pages: off (loader.ini gesture_pages=0)");
        return;
    }
    if (!load_table()) {
        lr_log(L"gesture pages: DISABLED (no DS1 gesture table; the vanilla gesture menu stays)");
        return;
    }
    int ok = 1;
    for (size_t i = 0; i < sizeof sigs / sizeof *sigs; i++)
        ok &= check_sig(&sigs[i]);
    for (size_t i = 0; i < sizeof slots / sizeof *slots; i++)
        if (*slot_at(slots[i].vt, slots[i].index) != AT(slots[i].fn)) {
            lr_log(L"gesture pages: %hs (slot %d) holds exe+%llx", slots[i].what, slots[i].index, (unsigned long long)(*slot_at(slots[i].vt, slots[i].index) - lr_base));
            ok = 0;
        }
    if (!ok) {
        lr_log(L"gesture pages: DISABLED (not the DarkSoulsII.exe v1.0.3.0 the RE was done on, or another mod changed the gesture menu)");
        return;
    }
    MH_STATUS s = MH_Initialize();
    if (s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) {
        lr_log(L"gesture pages: DISABLED (MinHook %d)", (int)s);
        return;
    }
    void *fc = (void *)AT(VA_LIST_COUNT), *fh = (void *)AT(VA_SLOT_HANDLE), *fi = (void *)AT(VA_HANDLE_ICON);
    if ((s = MH_CreateHook(fc, (void *)hk_count, (void **)&orig_count)) != MH_OK || (s = MH_CreateHook(fh, (void *)hk_handle, (void **)&orig_handle)) != MH_OK ||
        (s = MH_CreateHook(fi, (void *)hk_icon, (void **)&orig_icon)) != MH_OK) {
        lr_log(L"gesture pages: DISABLED (hooking the cell functions failed: MinHook %d)", (int)s);
        MH_RemoveHook(fc); MH_RemoveHook(fh); MH_RemoveHook(fi);
        return;
    }
    if ((s = MH_EnableHook(fc)) != MH_OK || (s = MH_EnableHook(fh)) != MH_OK || (s = MH_EnableHook(fi)) != MH_OK) {
        lr_log(L"gesture pages: DISABLED (enabling the cell hooks failed: MinHook %d)", (int)s);
        MH_RemoveHook(fc); MH_RemoveHook(fh); MH_RemoveHook(fi);
        return;
    }
    // the hooks above are inert without the guard; the vtable swaps make the feature live
    if (!swap_slot(VA_TOP_VFT, SLOT_FILL, (void *)hk_fill, (void **)&orig_fill) || !swap_slot(VA_TOP_VFT, SLOT_DECIDE, (void *)hk_decide, (void **)&orig_decide) ||
        !swap_slot(VA_TOP_VFT, SLOT_TEST, (void *)hk_test, (void **)&orig_test) || !swap_slot(VA_TOP_VFT, SLOT_CHANGE, (void *)hk_change, (void **)&orig_change) ||
        !swap_slot(VA_TOP_VFT, SLOT_OPEN, (void *)hk_open, (void **)&orig_open) || !swap_slot(VA_TOP_VFT, SLOT_INPUT, (void *)hk_input, (void **)&orig_input)) {
        lr_log(L"gesture pages: DISABLED (vtable swap failed)");
        return;
    }
    active = 1;
    lr_log(L"gesture pages: on (exe base %p; %d code signatures and %d vtable slots match; %d DS1 gestures, unlock_all=%d)",
           (void *)lr_base, (int)(sizeof sigs / sizeof *sigs), (int)(sizeof slots / sizeof *slots), table.n, table.unlock_all);
}

// Called from the keyboard poll (autopilot.c), every frame on the game thread; installs once.
void lr_gesture_tick(void)
{
    static int done;
    if (done)
        return;
    done = 1;
    install();
}
