// Morpheme asset pool growth (session 13, track seam13, rule I1p-L): DS2's global Morpheme asset manager (DAT_141618950,
// constructor FUN_1405f4e00, decomp\ds2s\DarkSoulsII.c l.1113297) keeps one 0x20-byte entry per registered NMB bundle id
// in a FIXED pool made by FUN_1405f5dd0(..., {0x20, 4}, 0x3000) = 12,288 entries (pool header: +0x10 capacity, +0x14 free
// count, +0x18 free-pointer stack). FUN_1405f65c0 (exe+5f65c0, l.1114496; only caller the NMB loader FUN_1404e0f40) pops an
// entry and, with none left, writes through NULL: crash exe+0x5f6634 (MOV byte [RAX+0x1c],1; minidumps 10/19 12:54 and
// 15:13, the second right after the Burg's characters (c2520 ... c5350) loaded while Firelink's were still resident).
// FUN_1405f6f00 (exe+5f6f00) pushes entries back with no capacity check. The entry index / hash maps (+0x20, +0x50) grow
// on their own (FUN_1405f6aa0); only this pool is fixed.
// Fix: detour FUN_1405f65c0; when fewer than GROW_MARGIN entries are free, give the pool a new block of GROW entries and a
// new free stack sized for every entry it now owns (old capacity + GROW), so pushes back never overflow. The original block
// stays where it is (DS2 frees the header at shutdown, which is still the original allocation; the extra blocks live until
// exit). The hook only acts on a nearly empty pool, where DS2 would crash anyway; with a vanilla roster it never triggers.
// loader.ini nmb_pool_grow=0 turns it off.
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#define EXE_BASE 0x140000000ull
#define AT(va) (lr_base + (uintptr_t)((va) - EXE_BASE))
#define VA_POOL_TAKE 0x1405f65c0ull
#define GROW 0x2000u
#define GROW_MARGIN 64u

static const uint8_t sig[] = { 0x4c, 0x89, 0x4c, 0x24, 0x20, 0x48, 0x89, 0x54, 0x24, 0x10, 0x53, 0x55, 0x56, 0x57, 0x41, 0x56,
                               0x48, 0x83, 0xec, 0x30, 0x45, 0x8b, 0xf0, 0x48, 0x8b, 0xf2, 0x48, 0x8b, 0xd9, 0x4c, 0x8d, 0x44,
                               0x24, 0x60, 0x48, 0x8d, 0x54, 0x24, 0x20, 0x48, 0x83, 0xc1, 0x20 };
// the empty-pool branch at exe+5f6616: MOV RCX,[RBX+0x18]; MOV EAX,[RCX+0x14]; TEST EAX,EAX; JNZ; XOR EAX,EAX; JMP; DEC EAX;
// MOV [RCX+0x14],EAX; MOV EDX,EAX; MOV RAX,[RCX+0x18]; MOV RAX,[RAX+RDX*8]; MOV byte [RAX+0x1c],1 (the crash)
static const uint8_t sig2[] = { 0x48, 0x8b, 0x4b, 0x18, 0x8b, 0x41, 0x14, 0x85, 0xc0, 0x75, 0x04, 0x33, 0xc0, 0xeb, 0x0f, 0xff,
                                0xc8, 0x89, 0x41, 0x14, 0x8b, 0xd0, 0x48, 0x8b, 0x41, 0x18, 0x48, 0x8b, 0x04, 0xd0, 0xc6, 0x40,
                                0x1c, 0x01 };

typedef uint8_t (*TakeFn)(uint8_t *mgr, void *asset, uint32_t type, uint64_t id);
static TakeFn orig_take;
static CRITICAL_SECTION grow_lock;
static uint32_t grown_total;

static void grow(uint8_t *pool)
{
    uint32_t cap = *(uint32_t *)(pool + 0x10), free_n = *(uint32_t *)(pool + 0x14);
    uint64_t newcap = (uint64_t)cap + GROW;
    uint8_t *block = (uint8_t *)VirtualAlloc(NULL, (SIZE_T)GROW * 0x20, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    void **stack = (void **)VirtualAlloc(NULL, (SIZE_T)newcap * sizeof(void *), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!block || !stack) {
        lr_log(L"nmb pool: growing the Morpheme asset pool FAILED (VirtualAlloc); %u free of %u", free_n, cap);
        if (block) VirtualFree(block, 0, MEM_RELEASE);
        if (stack) VirtualFree(stack, 0, MEM_RELEASE);
        return;
    }
    void **old = *(void ***)(pool + 0x18);
    for (uint32_t i = 0; i < free_n; i++)
        stack[i] = old[i];
    for (uint32_t i = 0; i < GROW; i++)
        stack[free_n + i] = block + (size_t)i * 0x20;
    *(void ***)(pool + 0x18) = stack;
    *(uint32_t *)(pool + 0x10) = (uint32_t)newcap;
    *(uint32_t *)(pool + 0x14) = free_n + GROW;
    grown_total += GROW;
    lr_log(L"nmb pool: %u entries added by the loader so far", grown_total);
    lr_log(L"nmb pool: Morpheme asset pool had %u of %u entries free: grown by %u to %u entries (rule I1p-L; vanilla would crash at exe+5f6634 once it is empty)",
           free_n, cap, GROW, (uint32_t)newcap);
}

static uint8_t hk_take(uint8_t *mgr, void *asset, uint32_t type, uint64_t id)
{
    uint8_t *pool = mgr ? *(uint8_t **)(mgr + 0x18) : NULL;
    if (pool && *(uint32_t *)(pool + 0x14) < GROW_MARGIN) {
        EnterCriticalSection(&grow_lock);
        if (*(uint32_t *)(pool + 0x14) < GROW_MARGIN)
            grow(pool);
        LeaveCriticalSection(&grow_lock);
    }
    return orig_take(mgr, asset, type, id);
}

void lr_nmbpool_install(void)
{
    static int done;
    if (done)
        return;
    done = 1;
    if (!lr_config.nmb_pool_grow) {
        lr_log(L"nmb pool: off (loader.ini nmb_pool_grow=0: DS2's fixed 12,288-entry Morpheme asset pool)");
        return;
    }
    const uint8_t *p = (const uint8_t *)AT(VA_POOL_TAKE), *p2 = (const uint8_t *)AT(VA_POOL_TAKE + 0x56);
    for (size_t i = 0; i < sizeof sig; i++)
        if (p[i] != sig[i]) { lr_log(L"nmb pool: DISABLED (FUN_1405f65c0 at exe+5f65c0 differs from DarkSoulsII.exe v1.0.3.0)"); return; }
    for (size_t i = 0; i < sizeof sig2; i++)
        if (p2[i] != sig2[i]) { lr_log(L"nmb pool: DISABLED (FUN_1405f65c0's pool pop at exe+5f6616 differs)"); return; }
    InitializeCriticalSection(&grow_lock);
    MH_STATUS s = MH_Initialize();
    if ((s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) ||
        (s = MH_CreateHook((void *)p, (void *)hk_take, (void **)&orig_take)) != MH_OK || (s = MH_EnableHook((void *)p)) != MH_OK) {
        lr_log(L"nmb pool: DISABLED (hooking FUN_1405f65c0 failed: MinHook %d)", (int)s);
        return;
    }
    lr_log(L"nmb pool: on (FUN_1405f65c0 hooked at exe+5f65c0: the 12,288-entry Morpheme asset pool grows by %u when fewer than %u entries are free)",
           GROW, GROW_MARGIN);
}
