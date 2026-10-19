# Travel menu: Drangleic / Lordran pages (static RE, session 7)

Goal: in the bonfire Travel menu, LB/RB (keyboard Shift+Left/Right) flips the area grid between
Drangleic (vanilla areas) and Lordran (DS1 areas), with a page title.

All addresses are VAs in `bin\ds2s\DarkSoulsII.unpacked.exe` (image base 0x140000000, so RVA = VA - 0x140000000).
Everything below is **static analysis only** (nothing was run in game). All functions use the MS x64 ABI (rcx, rdx, r8, r9).

## Tools written (data\research\travel\)

- `trace.py <va> [n]`: **de-chainer for the obfuscated code.** Follows `jmp` chains and resolves the anti-tamper
  tricks: `lea/movabs R,X; xchg [rsp],R; ... jmp [rsp-8]` / `ret` (= jmp X), `push X; jmp Y` (= call Y, back to X)
  and the `cmovCC`+`ret` conditional jumps (printed as `jCC A (else B)`). This makes the "obfuscated fragments"
  around 0x1400D3F50-0x1400D5BA0 readable; Ghidra's export cannot follow them.
- `xref.py <va>...`: brute-force rel32 call/jmp and RIP-relative xrefs over `.text`.
- `fmgfind.py <folder> <id>...`: FMG lookup (DS2 FMG v1).

## Classes and vtables

| class | primary vft | secondary vft (obj+0x58) | third (obj+0xF8) | ctor |
|---|---|---|---|---|
| `FeGroupTestBonfireTransitionBase` | 0x1410ba6b8 | 0x1410ba808 | 0x1410ba850 | 0x1400d2af0 / 0x1400d2f90 |
| `...TransitionList` (bonfire list) | 0x1410ba868 | 0x1410ba9b8 | 0x1410baa00 | 0x1400d3360 |
| `...TransitionTop` (area grid) | 0x1410baa18 | 0x1410bab68 | 0x1410babb0 | 0x1400d34b0 (called from 0x1400d6c40, the bonfire root menu) |

The **List object is embedded in the Top at Top+0x308** (`param_1[0x61]`). Top+0x2d8 = owner (bonfire root menu),
Top+0x2e0 = owner+0x278.

Primary vtable slots (41 slots; the secondary vtable starts right after the RTTI pointer at +0x148):

| slot (+off) | Base | List | Top | meaning |
|---|---|---|---|---|
| 2 (+0x10) | 0x14001bdd0 | 0x1400d42f0 | **0x1400d4360** | per-frame input `(this, int type, uint32 *ev)` |
| 10 (+0x50) | 0x140021cb0 | 0x1400d4c80 | 0x1400d4ce0 | close/release |
| 12 (+0x60) | 0x14001d160 | 0x1400d5800 | 0x1400d5990 | cursor moved: caption texts (+0x130 name, +0x1c0, +0x250) |
| 16 (+0x80) | ret | 0x1400d5170 | 0x1400d5300 | **decide** (command "A", mask 0x100) |
| 17 (+0x88) | ret | 0x1400d5150 | 0x1400d5160 | **cancel** (command "B", mask 0x200), both `jmp 0x140105b00` |
| 25 (+0xc8) | 0x140023690 | 0x1400d5370 | **0x1400d5480** | open/activate (Top: builds everything) |
| 27 (+0xd8) | 0x14001bf70 | — | 0x1400d4d20 | |
| 29 (+0xe8) | 0x14001d010 | 0x1400d5680 | 0x1400d5780 | back out (Top: closes the list if open, sound 0x68, then slot 10) |

Secondary (obj+0x58) slot 3 (+0x18): Top **0x1400d4120 = set the window title** `(Top+0x58, const wchar_t *)`
(finds FLO element 0x5f5c47c/0x5f5c481/0x5f5c481/0x5f5b9f2 under Top+0x2e8, `FUN_1400299c0(elem+0x30, text)`).
Secondary slot 4 (+0x20) = set the prompt-bar text. Secondary slot 6 (+0x30) 0x1400d40a0 returns the command
list at obj+0x58+0x258 = **Top+0x2b0** (vector of 0x20-byte commands: begin +0x2b0, end +0x2b8, cap +0x2c0,
allocator +0x2c8). Base ctor helper 0x1400d4f30 puts decide (`DAT_14160c0d0` → vt+0x80) and cancel
(`DAT_14160c118` → vt+0x88) there.

## (1) Where the area list and bonfire list come from

The data owner is the **bonfire manager `M = [[gm+0x70]+0x58]`** (gm = `[0x1416148f0]`; the same object whose
+0x68 = travel target bonfire id and +0x67 = travel request flag, set by List decide 0x1400d5170).

- `M+0x20` = records, 24 bytes each, count `u32 M+0x28`: `+0 u16 bonfireId`, `+2+[M+0x44]` u8 lit bit0
  (per-character byte), `+8` pointer to the `MapObjectBonfireParam` row (`+0 mapId`, `+4 menuAreaID`, `+0x23 u8 menuSortID`).

**Area list builder `0x14017dff0 (M, out)` → out** (stack buffer, 0x490 bytes):
fixed vector of up to 0x60 entries of 12 bytes at `out + (-(int)out & 3)`, count `u64 out+0x488`:

```
struct AreaEntry { u32 menuAreaID; u8 lit; u8 sortKey; u16 pad; u32 newMark; };   // 12 bytes
```

For every record: skip unless lit (or event flag 201700 `0x313e4` set, `FUN_1404517d0`), skip if the row's map
fails `FUN_1403bf610`/`FUN_1403bf6c0` (map registered/valid), skip menuAreaID 0; find-or-append the area
(`FUN_14017db10`), `lit |= rec lit`, `sortKey = max(sortKey, row.menuSortID)`; `newMark` = 1+index of the map in a
list from `[gm+0x22f0]` vfunc +0x1a8 (not for areas 10310001/10230001). Then **sorted ascending by sortKey**
(`FUN_14017ccf0`; insertion sort ≤ 32 entries, so equal keys keep param order; introsort above 32).
Callers: 0x1400d54c8 (Top open) and an unknown fragment 0x1402992fe.

**Bonfire list builder `0x14017ddd0 (M, out, u32 menuAreaID)`**: fixed vector of ≤ 0x20 entries, 6 bytes, count
`u64 out+0xC8`:

```
struct BonfireEntry { u16 bonfireId; u8 lit; u8 intensity(1..99); u8 menuSortID; u8 pad; };
```

Same lit/map filters, `row.menuAreaID == arg`, stops silently at 32, sorted ascending by menuSortID (`FUN_14017ce80`).
Callers: 0x1400d4bdd (grid rebuild), 0x1400d5637 (Top open), fragment 0x14029939b.

**Top storage:** `FUN_1400d3d40(Top+0x6d0, src)` (only caller 0x1400d54d7, Top open) copies the area list into the
Top: **entries at Top+0x6d0, count `u64 Top+0xb58`**. Readers: `0x1400d4de0` (selected AreaEntry; `FUN_140022140`
= cursor index), `0x1400d4870`. The List's bonfire vector is at List+0x2e8 (6-byte entries), count List+0x3b0
(Top+0x5f0 / Top+0x6b8); `0x1400d4460(List, src)` copies and fills the 10 visible rows.

**Top open (slot 25, 0x1400d5480 → chunk 0x141b78e86), de-chained:**
1. `FUN_14017dff0(M, buf)`; `FUN_1400d3d40(Top+0x6d0, buf)`
2. title: `text = FUN_140503620(0x12, [Top->owner+0x2f60])` (bonfirename of the bonfire you rest at),
   `vt(Top+0x58)[3](Top+0x58, text)`
3. prompt bar: menu texts (kind 0xb) 0x15ba9 + 0x15f2d (`bofire.fmg` 89901 = "⑤：Select ⑲：Done ⑳：Back")
4. sound 0x66 on Top+0x2f0, vt+0xb0, **`FUN_1400d4870(Top)`**, `FUN_140021b00(Top, 0)`
5. `FUN_1400d4de0` → `FUN_14017ddd0(M, buf, area)` → `FUN_1400d4460(Top+0x308, buf)`

## (4) Rebuild function

**`0x1400d4870 (Top)` = rebuild the grid** (stub `jmp 0x141b85eed`): `FUN_140021b30(Top, count)` (item count),
for each cell i < `[Top+0xb58]`: colour 0x70 (lit) / 0x7a, tile image `{kind 0xd, FUN_14002f6b0(entry)}` (ic_area),
lit flag, new-mark `entry.newMark + 30000`; empty cells otherwise; vt+0x40; then refills the List for the selected
area (`0x14017ddd0` + `0x1400d4460`). **The game calls it from the Top input handler 0x1400d4360 whenever the cursor
index changes** (0x1400d4406/0x1400d4448), so calling it after changing Top+0x6d0/+0xb58 is the vanilla path.
Cursor: `char FUN_140021b00(group, int idx)` (sets index, scrolls). Caption: Top vt slot 12 (`0x1400d5990`).

## (2) Input

Menu events are 7 dwords; `ev[0]` = pressed this frame, `ev[1]` = repeat, `ev[2]` = held, `ev[3..4]` pointer.
The pad-action → menu-bit table is built in `FUN_14010b9b0`/`FUN_14010bc50` (table at 0x141567b40,
`{u32 menuBit; u64 actionMask}` × 16, action bit n = action id n):

| menu bit | action | | menu bit | action |
|---|---|---|---|---|
| 0x1/0x2/0x4/0x8 | 0x21..0x24 (likely menu d-pad; Ranking pages ±100 on 4/8) | | 0x100 / 0x200 | 0x10 / 0x11 (confirm / back) |
| 0x10/0x20/0x40/0x80 | 0x16..0x19 | | 0x400 / 0x800 | 0x27 / 0x28 |
| 0x1000 / 0x4000 | 0x04 / 0x05 | | **0x40000 / 0x80000** | **0x25 / 0x26 = LB / RB** (Shift+Left/Right) |

Commands (key-guide descriptors, initialised in 0x14106b0b0…): `{u32 mask; u8; text {kind 0, id}; u64 order}`;
**LB = `DAT_14160c070` (mask 0x40000, no text), RB = `DAT_14160c058` (mask 0x80000, common.fmg 10011
"⑨⑦：Toggle")**. Vanilla LB/RB user to copy: `FeNpcItemSelectBaseMenu` ctor (decomp ~line 140355):
`cmd = FUN_1400b72b0(functor, this, exec{FUN_1400bc600}, pred{0x1400be710}); FUN_14002eea0(&DAT_14160c058, tmp, cmd);
FUN_14001df70(this+cmdlist, ...)` and the same for `DAT_14160c070`. (Ranking uses 0x4/0x8 = left/right for ±100.)

Input forwarding (`FUN_1400271f0` / `FUN_140023f40`): the focused group gets the real event, every other group in
the stack gets a zeroed event. The List is pushed as a child of the Top: **child count `u64 Top+0xC0`** (≠ 0 while the
bonfire list or any popup is open); the List's own "open" byte is Top+0x326 (List+0x1e).

## (3) Titles and text

- Window title: Top secondary vt slot 3 (0x1400d4120), any `wchar_t*`. Vanilla = current bonfire name.
- Area caption under the grid: slot 12 → `FUN_14002f630(entry)` = `{kind 5 (mapname), menuAreaID}` if lit, else
  menu text 89999 "???". Tile picture: `ic_area_<menuAreaID:D9>`.
- Text lookup: `const wchar_t *FUN_140503620(int kind, int id)` (kind 5 mapname, 0x12 bonfirename, 0xb menu/bofire).
- Prompt bar: bofire.fmg 89901 (grid), 89902 (list). Data-editable.

## Recommended implementation (lordran.dll, MinHook)

```c
#define VA(x) ((void*)(x))           /* exe is not relocated (image base 0x140000000) */
typedef struct { uint32_t area; uint8_t lit, sort; uint16_t pad; uint32_t newMark; } AreaEntry;
static AreaEntry g_all[0x60]; static int g_nall, g_page;      /* 0 Drangleic, 1 Lordran */
static void (*o_copyAreas)(char *dst, char *src);              /* 0x1400d3d40 */
static void (*o_topInput)(char *top, int type, uint32_t *ev);  /* 0x1400d4360 */
static void (*rebuildGrid)(char *top) = VA(0x1400d4870);
static char (*setCursor)(char *grp, int idx) = VA(0x140021b00);
static const wchar_t *(*getText)(int kind, int id) = VA(0x140503620);

static int is_lordran_area(uint32_t area) {      /* any DS1 bonfire (40000..49999) in that area */
    char *m = mgr();                              /* [[[0x1416148f0]+0x70]+0x58] */
    char *rec = *(char **)(m + 0x20); uint32_t n = *(uint32_t *)(m + 0x28);
    for (uint32_t i = 0; i < n; i++, rec += 24) {
        uint16_t id = *(uint16_t *)rec; char *row = *(char **)(rec + 8);
        if (row && *(uint32_t *)(row + 4) == area) return id >= 40000 && id < 50000;
    }
    return area / 1000000 != 10 && area / 1000000 != 20 && area / 1000000 != 40 && area / 1000000 != 50;
}
static int filter(char *top, int page) {          /* writes Top+0x6d0 / +0xb58 */
    AreaEntry *v = (AreaEntry *)(top + 0x6d0); int n = 0;
    for (int i = 0; i < g_nall; i++) if (is_lordran_area(g_all[i].area) == page) v[n++] = g_all[i];
    *(uint64_t *)(top + 0xb58) = n; return n;
}
static void set_title(char *top) {
    wchar_t t[128]; uint32_t cur = *(uint32_t *)(*(char **)(top + 0x2d8) + 0x2f60);
    swprintf(t, 128, L"%s  –  %s", g_page ? L"Lordran" : L"Drangleic", getText(0x12, cur));
    char *sub = top + 0x58; ((void (*)(char *, const wchar_t *))(*(void ***)sub)[3])(sub, t);
}
static void hk_copyAreas(char *dst, char *src) {  /* Top open: capture full list, apply page */
    o_copyAreas(dst, src);
    char *top = dst - 0x6d0;
    if (*(void **)top != VA(0x1410baa18)) return;
    g_nall = (int)*(uint64_t *)(dst + 0x488); memcpy(g_all, dst, g_nall * sizeof(AreaEntry));
    uint32_t cur = *(uint32_t *)(*(char **)(top + 0x2d8) + 0x2f60);
    g_page = (cur >= 40000 && cur < 50000);       /* open on the page of the bonfire you rest at */
    if (!filter(top, g_page)) filter(top, g_page ^= 1);
    /* the title is written by the game after this call: hook slot 25 (0x1400d5480) post-call, or the
       title setter 0x1400d4120, to prefix the page name */
}
static void hk_topInput(char *top, int type, uint32_t *ev) {
    if (ev && (ev[0] & 0xC0000) && *(uint64_t *)(top + 0xC0) == 0) {   /* LB/RB, no list/popup open */
        if (filter(top, g_page ^ 1)) {
            g_page ^= 1;
            setCursor(top, 0);
            rebuildGrid(top);                         /* grid + list for cell 0 */
            ((void (*)(char *))(*(void ***)top)[12])(top);   /* caption of the selected area */
            set_title(top);
        } else filter(top, g_page);                   /* other page empty: stay (optionally buzz) */
        uint32_t e[7]; memcpy(e, ev, sizeof e); e[0] &= ~0xC000Fu; e[1] &= ~0xC000Fu;  /* eat LB/RB + L/R */
        o_topInput(top, type, e); return;
    }
    o_topInput(top, type, ev);
}
/* MH_CreateHook(VA(0x1400d3d40), hk_copyAreas, &o_copyAreas); MH_CreateHook(VA(0x1400d4360), hk_topInput, &o_topInput);
   + post-hook of 0x1400d5480 (starts with jmp rel32: MinHook copes) calling set_title(top). */
```

Simplest viable version: the two hooks above (d3d40 + d4360) and no title change; add the page name to the
prompt bar with data instead (bofire.fmg 89901 → "⑤：Select ⑨⑦：Lordran/Drangleic ⑲：Done ⑳：Back";
⑨⑦ is the glyph pair vanilla uses for the LB/RB "Toggle", exact left/right order to confirm in game).

Native alternative for input (shows in the command system like vanilla): append two commands to Top+0x2b0 after
the Top ctor (0x1400d34b0) with `FUN_140018cc0`/`FUN_14002eea0(&DAT_14160c070 / &DAT_14160c058, ...)`/`FUN_14001df70`,
exec = a C function `void f(char *top)`. Needs the DLReferencePointer ref-count handling of 0x1400d4f30; more work
and more risk than the input hook.

## Data-only fallbacks (evaluated)

- **menuSortID** (row +0x23, byte): area order = ascending max(menuSortID) of the area's *lit* bonfires; bonfire order
  in the list = ascending menuSortID. DS2 uses 1..~199, DLC 200..222, debug 250+ (MapBuilder note); our DS1 rows use
  230, so **Lordran areas already sort after all of Drangleic**. Give each DS1 area its own value (230, 231, …) to fix the
  order (ties are only stable below 33 areas). There is no page break or header: Lordran just continues the grid.
- **menuAreaID** groups bonfires into tiles: tile picture `ic_area_<id>`, caption `mapname.fmg[id]` (also used by the
  character-select list). Nothing else in the Travel code keys on it (only 0 = skip, 10310001/10230001 special for the
  new-mark). **Single "Lordran" tile:** give every DS1 bonfire one menuAreaID (e.g. 18000000, mapname "Lordran",
  own ic_area tile) → one tile opening a list of all lit DS1 bonfires. Limit: 32 bonfires per list (extra ones are
  silently dropped), so DS1 needs two tiles (e.g. "Lordran" / "Lordran (DLC and depths)"); the character list would
  show "Lordran" for every DS1 location.

## Confidence

High: builders, entry layouts, Top storage offsets, rebuild path, title setter, LB/RB menu bits (table + vanilla
LB/RB command users). Medium: `Top+0xC0` as the "child open" test (Top+0x326 is the alternative), `owner+0x2f60` as the
current bonfire id (read as u32), and that 0x1400d4360 is only fed real events while Travel is focused (the
framework zeroes events for unfocused groups). Not verified in game.
