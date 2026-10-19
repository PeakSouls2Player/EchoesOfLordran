// Seam streaming gate (session 11, track seam12, rule SS1): lets DS2 stream a ported DS1 map in while the player walks
// toward it, like vanilla neighbours (Majula <-> Forest of Fallen Giants), instead of never loading it.
//
// Root cause (DarkSoulsII.exe v1.0.3.0, decompile line numbers in decomp\ds2s\DarkSoulsII.c):
//  - Every frame the world update FUN_1403dc3e0 (l.771260) fills avail[42] (world+0x188) with
//    FUN_1402db4f0(registry = [0x1416751f8]+0x368, map id of WorldMapList line i). Only slots with avail != 0 receive the
//    masks of the ConnectCollisions under the player (FUN_1401c8cd0 l.345433, "param_4[slot] != 0"), the second-order
//    propagation (FUN_1403dc930 l.771822 -> FUN_1403cbb20) and the category-23 stall (FUN_1401ea510 l.374256).
//  - FUN_1402db4f0 (l.566220) looks the map id up in a std::map built by FUN_1402db370 (l.566135) from a hard-coded
//    28-entry table at 0x141613c60 (static initializer FUN_141073830, l.3190091: {map, PlayGo chunk, 6 neighbour maps}),
//    the console "install chunk" table, and answers "chunk installed" (FUN_140b2c390 -> FUN_140b5f420 >= 1). A map that is
//    not in that table (all of ours: m18_xx / m19_xx) gets avail = 0, so its neighbours' connects are ignored and it is
//    never streamed: the play test's m19_42 -> m19_41 walk never opened m19_41's MSB (grey sky through the last arch).
//    The table lists exactly DS2's 28 seamless maps and every vanilla ConnectCollision pair (lr seam-stream-check V1).
//
// Fix: a MinHook detour on FUN_1402db4f0: the original answer, or 1 for a map DS2's table does not know (original
// answer 0 because it is absent) whose area is a Lordran host area (AA 18 or 19: rule H1 m19_xx, PIN1 m18_01; DS2 has
// no map in either area), i.e. "chunk installed" for the PC data we ship. Vanilla maps are never changed. loader.ini
// seam_stream=0 turns it off (vanilla behaviour: DS1 maps load only through warps / load screens).
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#include <stdio.h>

#define EXE_BASE 0x140000000ull
#define AT(va) (lr_base + (uintptr_t)((va) - EXE_BASE))

#define VA_REGISTRY_HAS 0x1402db4f0ull // bool (registry, packed map id AA<<24|BB<<16|CC<<8|DD)
#define VA_UPDATE_CALL  0x1403dc485ull // FUN_1403dc3e0: call FUN_1402db4f0 (avail[i])
#define VA_STALL_CALL   0x1401ea580ull // FUN_1401ea510: call FUN_1402db4f0 (category-23 box)

typedef struct Sig { uint64_t va; const char *what; const char *bytes; } Sig;
static const Sig sigs[] = {
    { VA_REGISTRY_HAS, "map availability lookup FUN_1402db4f0",
      "48 83 ec 28 4c 8b 49 18 4d 8b c1 49 8b 41 08 80 78 19 00 75 17 39 50 1c 73 06 48 8b 40 10 eb 06 4c 8b c0 48 8b 00 80 78 19 00 74 e9 4d 3b c1 74 0b 4c 89 44 24 30 41 3b 50 1c 73 05 4c 89 4c 24 30 48 8d 44 24 30 48 8b 00 49 3b c1 74 22 44 8b 40 24 45 85 c0 78 19 48 8b 49 10 48 8d 54 24 30 e8 3b 0e 85 00 83 38 01 0f 9d c0 48 83 c4 28 c3 32 c0 48 83 c4 28 c3" },
    { VA_UPDATE_CALL, "world update: avail[i] = FUN_1402db4f0(...)", "e8 66 f0 ef ff" },
    { VA_STALL_CALL, "category-23 stall: FUN_1402db4f0(neighbour)", "e8 6b 0f 0f 00" },
};

typedef uint8_t (*HasFn)(void *registry, uint32_t map);
static HasFn orig_has;
static int active;
static volatile LONG logged[64]; // first answer logged per (AA 18/19, BB) -- keeps the log small

static int lordran_area(uint32_t map) { uint32_t aa = map >> 24; return aa == 18 || aa == 19; }

static uint8_t hk_has(void *registry, uint32_t map)
{
    uint8_t r = orig_has(registry, map);
    if (r || !lordran_area(map))
        return r;
    uint32_t bb = (map >> 16) & 0xff;
    int k = (int)((map >> 24) == 18 ? bb % 32 : 32 + bb % 32);
    if (!InterlockedExchange(&logged[k], 1))
        lr_log(L"seam stream: m%02u_%02u_%02u_%02u is not in DS2's chunk table (FUN_1402db4f0 = 0): reported available, so its ConnectCollision neighbours stream it in",
               map >> 24, bb, (map >> 8) & 0xff, map & 0xff);
    return 1;
}

static int hexval(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }

static int check_sig(const Sig *s)
{
    const uint8_t *p = (const uint8_t *)AT(s->va);
    int n = 0, ok = 1;
    for (const char *h = s->bytes; *h;) {
        if (*h == ' ') { h++; continue; }
        int hi = hexval(h[0]), lo = h[1] ? hexval(h[1]) : -1;
        if (hi < 0 || lo < 0) return 0;
        if (p[n] != (uint8_t)(hi << 4 | lo)) ok = 0;
        n++;
        h += 2;
    }
    if (!ok)
        lr_log(L"seam stream: %hs at exe+%llx differs from DarkSoulsII.exe v1.0.3.0", s->what, (unsigned long long)(s->va - EXE_BASE));
    return ok;
}

static void install(void)
{
    if (!lr_config.seam_stream) {
        lr_log(L"seam stream: off (loader.ini seam_stream=0; DS1 maps are never streamed in by their neighbours)");
        return;
    }
    int ok = 1;
    for (size_t i = 0; i < sizeof sigs / sizeof *sigs; i++)
        ok &= check_sig(&sigs[i]);
    if (!ok) { lr_log(L"seam stream: DISABLED (code differs)"); return; }
    void *target = (void *)AT(VA_REGISTRY_HAS);
    MH_STATUS s = MH_Initialize();
    if ((s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) || (s = MH_CreateHook(target, (void *)hk_has, (void **)&orig_has)) != MH_OK ||
        (s = MH_EnableHook(target)) != MH_OK) {
        lr_log(L"seam stream: DISABLED (hooking FUN_1402db4f0 failed: MinHook %d)", (int)s);
        return;
    }
    active = 1;
    lr_log(L"seam stream: on (FUN_1402db4f0 hooked at exe+%llx: maps of areas 18/19 absent from DS2's 28-map chunk table count as available)",
           (unsigned long long)(VA_REGISTRY_HAS - EXE_BASE));
}

int lr_stream_active(void) { return active; }

// Called from the keyboard poll (autopilot.c) on the game thread, before the first map loads; installs once.
void lr_stream_tick(void)
{
    static int done;
    if (done)
        return;
    done = 1;
    install();
}
