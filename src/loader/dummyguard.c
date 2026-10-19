// Dummy guard (session 13, track seam13, rule DM1 safety net): DS2's dummy-matrix function FUN_140b35cd0 (exe+b35cd0,
// decomp\ds2s\DarkSoulsII.c l.2134638) multiplies a dummy with Flag1 (runtime +0x31) and AttachBoneIndex >= 0 (+0x36) by the
// model's inverse-bind array at model+0x198 WITHOUT a null check. FUN_140b36040 (l.2134811) builds that array only for a model
// with a skinned mesh (model+0xB0, FUN_140b630d0), so a rigid model with such a dummy crashes at exe+0xb35dae
// (MOVAPS XMM3,[RCX], RCX = attach*0x30 + 0). Seen 3x on 10/19 (minidumps 14:50-14:52): the Burg's barrel o19_1330, whose TAE
// SFX event (FUN_1403e59b0 -> FUN_1403fc900 -> FUN_1403fceb0 -> FUN_140b37300) queried its dummies ~3 s after m19_41 loaded.
// The data fix is rule DM1 in the pack builder (Build\DummyRules.cs, lr dummy-check); this hook only keeps a future
// offender from crashing: it clears that dummy's Flag1 in the loaded model (DS2 then takes the parent-bone path = DS2's own
// bone-relative form) and logs "DATA BUG" once per dummy. Vanilla models never reach it (0 of 4774 vanilla FLVERs).
// loader.ini dummy_guard=0 turns it off.
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#define EXE_BASE 0x140000000ull
#define AT(va) (lr_base + (uintptr_t)((va) - EXE_BASE))
#define VA_DUMMY_MATRIX 0x140b35cd0ull

static const uint8_t sig[] = { 0x40, 0x55, 0x56, 0x57, 0x41, 0x56, 0x48, 0x81, 0xec, 0x98, 0x00, 0x00, 0x00, 0x49, 0x63, 0xf0,
                               0x48, 0x8b, 0xfa, 0x48, 0x8b, 0x94, 0x24, 0xe0, 0x00, 0x00, 0x00, 0x4d, 0x8b, 0xf1, 0x48, 0x8b,
                               0xe9, 0x39, 0xb1, 0x80, 0x00, 0x00, 0x00, 0x77, 0x18 };

typedef void *(*DummyFn)(uint8_t *model, void *out, uint32_t index, float *world, void *bones);
static DummyFn orig_dummy;
static volatile LONG reports;

static void *hk_dummy(uint8_t *model, void *out, uint32_t index, float *world, void *bones)
{
    if (model && *(void **)(model + 0x198) == NULL && index < *(uint32_t *)(model + 0x80)) {
        uint8_t *d = *(uint8_t **)(model + 0x88) + (size_t)index * 0x40;
        if (d[0x31] && *(int16_t *)(d + 0x36) >= 0) {
            d[0x31] = 0;
            if (InterlockedIncrement(&reports) <= 32)
                lr_log(L"dummy guard: DATA BUG (rule DM1): rigid model %p (no inverse-bind array) has dummy %d with Flag1 + attach bone %d (parent %d): Flag1 cleared, else DS2 crashes at exe+b35dae. Run lr dummy-check on the pack.",
                       (void *)model, (int)*(int16_t *)(d + 0x32), (int)*(int16_t *)(d + 0x36), (int)*(int16_t *)(d + 0x34));
        }
    }
    return orig_dummy(model, out, index, world, bones);
}

void lr_dummyguard_install(void)
{
    static int done;
    if (done)
        return;
    done = 1;
    if (!lr_config.dummy_guard) {
        lr_log(L"dummy guard: off (loader.ini dummy_guard=0)");
        return;
    }
    const uint8_t *p = (const uint8_t *)AT(VA_DUMMY_MATRIX);
    for (size_t i = 0; i < sizeof sig; i++)
        if (p[i] != sig[i]) {
            lr_log(L"dummy guard: DISABLED (FUN_140b35cd0 at exe+b35cd0 differs from DarkSoulsII.exe v1.0.3.0)");
            return;
        }
    MH_STATUS s = MH_Initialize();
    if ((s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) ||
        (s = MH_CreateHook((void *)p, (void *)hk_dummy, (void **)&orig_dummy)) != MH_OK || (s = MH_EnableHook((void *)p)) != MH_OK) {
        lr_log(L"dummy guard: DISABLED (hooking FUN_140b35cd0 failed: MinHook %d)", (int)s);
        return;
    }
    lr_log(L"dummy guard: on (FUN_140b35cd0 hooked at exe+b35cd0: a rigid model's Flag1 + attach-bone dummy is logged and cleared instead of crashing at exe+b35dae)");
}
