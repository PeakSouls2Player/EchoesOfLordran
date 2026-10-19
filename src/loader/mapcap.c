// Map cap: raise DS2's 42-map WorldMapList limit to N maps (loader.ini map_cap=N, default 64; 0 or 42 = off).
// Session 11 track cap11. Evidence, site list, save analysis and risks: data\sandbox\cap11\NOTES.md.
//
// DS2 gives every WorldMapList line a "map index" (its position) and keeps many per-map arrays of exactly 42 entries,
// indexed by it. A map index >= 42 is either rejected (the world refuses to build: 0x1403dc0e0 cmp r12d,0x2a; most
// accessors guard idx < 0x2a and silently drop the data) or overflows a fixed array (heap or stack corruption). Entity
// handles keep the index in 6 bits (FUN_14017b600), so 64 is the hard ceiling.
//
// How this module raises it (all on DarkSoulsII.exe v1.0.3.0; every site is checked byte for byte before anything is
// written, and one mismatch disables the whole feature):
//  - world object (FUN_1403bd230 allocates 0x200 bytes; slots[42] at +0x38, avail[42] at +0x188) and its environment
//    object (FUN_1403dbec0 allocates 0x870 bytes; mask arrays [42] x 16 at +0x50, +0x308, +0x5a8): the allocations are
//    grown (immediates patched) and an extension block with [MAX_MAPS] arrays is appended. Every function that walks
//    those arrays (all of them live in 0x1403da4d0..0x1403dd640, proven by a whole-binary scan of the field offsets)
//    is replaced by a C reimplementation (below) that uses the extension; the stack buffers of 42 x 16 bytes in
//    FUN_1403dc3e0 / FUN_1403dc930 become [MAX_MAPS] buffers here. The first 42 entries are mirrored in place, so a
//    reader we do not know about still sees vanilla data for maps 0..41.
//  - a replaced function called with a world/env object that was not created by our grown constructors (e.g. one that
//    existed before the patches went in) runs the original game code (MinHook trampoline): vanilla behaviour.
// The offline self-check (data\sandbox\cap11\tools\cap_harness.c) maps the unpacked exe at another base, installs all of
// this and runs each replacement against the original game function on the same inputs (differential test).
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#include <stdio.h>

#define EXE_BASE 0x140000000ull
#define AT(va) (lr_base + (uintptr_t)((va) - EXE_BASE))

#define VANILLA_MAPS 42
#define MAX_MAPS 64 // array capacity; 6-bit map index in entity handles (FUN_14017b600: (id << 6 | idx & 0x3f) << 4 | 4)

typedef struct Mask { uint32_t w[4]; } Mask; // a 128-bit draw/area group set (DS2's float4-sized masks)

// ---- game addresses -------------------------------------------------------------------------------------------------
#define VA_GAME_MANAGER   0x1416148f0ull
#define VA_REGISTRY_ROOT  0x1416751f8ull // [x+0x368] = map availability registry (FUN_1402db4f0)
#define VA_MASK_EMPTY     0x141614c20ull // Mask: "no group" (read at run time like the game does)
#define VA_MASK_FULL      0x141614c30ull // Mask: "every group"
#define VA_DEBUG_ALL      0x141614c41ull // u8: debug "show every map" switch read by FUN_1403dc3e0/dc930
#define VA_ENV_BYTE40     0x141614c40ull // u8: passed by FUN_1403da960 to FUN_1403da370
#define VA_LAST_COLL_A    0x141614c48ull // statics of FUN_1403dc930
#define VA_LAST_COLL_B    0x141614c50ull
#define VA_CUR_MAP_INDEX  0x141581818ull // u8: map index of the collision under the player (written by FUN_1403dc930)
#define VA_ONE            0x1410ac698ull // float 1.0 (FUN_1403db1a0)
#define VA_TEN            0x1410ad5e8ull // float 10.0 (FUN_1403da960)

// functions the replacements call (same arguments as the original call sites)
#define VA_ALLOC          0x140833320ull // void *(size, align, allocator)
#define VA_SLOT_CTOR      0x1403cb9a0ull // slot *(mem, allocator, mapmgr)   (0x210-byte per-map slot object)
#define VA_SLOT_INIT      0x1403cbc20ull // char (slot, map id, index)
#define VA_SLOT_LIGHTS    0x1403cbd60ull // void (slot)
#define VA_SLOT_UPDATE    0x1403cc3f0ull // void (slot, arg)
#define VA_SLOT_READY     0x1403cbcb0ull // char (slot)
#define VA_SLOT_SETCUR    0x1403cbae0ull // void (slot, coll, cur index, u8 flag)
#define VA_SLOT_COMMIT    0x1403cba60ull // void (slot)
#define VA_SLOT_PROPAGATE 0x1403cbb20ull // void (slot, Mask *, Mask buf[]): ORs connect masks into buf[target index]
#define VA_LOADER_STEP    0x1403de940ull // void (loader state at world+0x10)
#define VA_LIST_ID        0x1403bce90ull // int *(mapmgr, int *out, index)
#define VA_REGISTRY_HAS   0x1402db4f0ull // u8 (registry, map id)
#define VA_OBJ_MAP_INDEX  0x1403ba380ull // int (obj) = [[obj+0x28]+0xc]
#define VA_COLL_MAP_ID    0x1403ba320ull // int *(coll, int *out)
#define VA_COLL_CONNECTS  0x1401ca460ull // void *(coll+0xb8 [, coll])
#define VA_CONNECT_MASKS  0x1401c8cd0ull // void (connects, Mask buf[], int count, u8 avail[])
#define VA_RES_FLUSH      0x140b02450ull // void ([gm+0x30])
#define VA_IS_LOCAL       0x1403144c0ull // char ([gm+0xd0])
#define VA_GRAPHICS       0x14039a9f0ull // void *(gm)
#define VA_ENTITY_FIND    0x140bb2620ull // void *(x, id | 0x1ffff)
#define VA_ENV_ADD        0x1403db560ull // void (env, sub, entity, float)
#define VA_MAP_CLEAR      0x1403db7b0ull // void (std::map *)
#define VA_MAP_INSERT     0x1403da370ull // void (std::map *, out[16], 0, {u32 key, float}, u8)
#define VA_ENV_LIGHT      0x140bb2d60ull // void (x, int, float)

// functions this module replaces (MinHook) or wraps
#define VA_WORLD_CTOR     0x1403dbc60ull // wrapped: original + extension init
#define VA_ENV_CTOR       0x1403da4d0ull // wrapped: original + extension init
#define VA_W_PROPAGATE    0x1403dbd80ull // (world, coll, Mask buf[])            <- mapmgr FUN_1403bcc50 (jmp)
#define VA_W_SLOT         0x1403dbe60ull // slot *(world, index)                  <- getters FUN_1403bce00/30/60, ...
#define VA_W_FIND         0x1403dbe80ull // slot *(world, map id)
#define VA_W_SLOT_READY   0x1403dbf90ull // char (world, index)
#define VA_W_MAP_READY    0x1403dbfc0ull // char (world, map id)
#define VA_W_ALL_READY    0x1403dc010ull // char (world)
#define VA_W_BUILD        0x1403dc0a0ull // void (world)                          <- loader state FUN_1403de950
#define VA_W_DESTROY      0x1403dc2f0ull // void (world)
#define VA_W_FORWARD      0x1403dc370ull // void (world, arg, obj)
#define VA_W_UPDATE       0x1403dc3e0ull // void (world, arg)                     <- FUN_1403bdf49
// reimplemented without a hook (only called from replaced code): FUN_1403dc930, FUN_1403dd530, FUN_1403da960,
// FUN_1403db1a0, FUN_1403db080 (env sub-object init, ctor only)
#define VA_W_MASKS        0x1403dc930ull
#define VA_W_CURMASK      0x1403dd530ull
#define VA_E_UPDATE       0x1403da960ull
#define VA_E_SUB_UPDATE   0x1403db1a0ull

// allocation sizes grown in place (mov ecx, imm32 before call FUN_140833320)
#define VA_WORLD_SIZE_IMM 0x1403bd295ull // b9 [00 02 00 00]
#define VA_ENV_SIZE_IMM   0x1403dbf48ull // b9 [70 08 00 00]

// ---- object layouts -------------------------------------------------------------------------------------------------
#define W_ALLOCATOR 0x000
#define W_MAPMGR    0x008
#define W_LOADER    0x010
#define W_ENV       0x018
#define W_COLL      0x020
#define W_CURCOLL   0x028
#define W_CURIDX    0x030
#define W_SLOTS     0x038 // void *[42]
#define W_AVAIL     0x188 // u8[42]
#define W_LOADED    0x1b2 // short: slots in state 5
#define W_UNLOADED  0x1b4 // short: slots in state 0
#define W_COUNT     0x1b6 // short: slots created
#define W_LISTN     0x1b8 // short: list lines
#define W_MAPID     0x1bc
#define W_OVR_MASK  0x1d0 // Mask (FUN_1403bcc50's override)
#define W_OVR_MASK2 0x1e0
#define W_OVR_IDX   0x1f0 // u32: index of the override map, -1 none
#define W_NOCONNECT 0x1f4 // u8
#define W_COMMIT    0x1f5 // u8
#define W_DIRTY     0x1f6 // u8
#define W_SIZE      0x200

#define E_SUB_A     0x038 // sub-object: float p[4]; +0x10 list head; Mask m[42] at +0x18
#define E_SUB_B     0x2f0
#define E_SUB_MASKS 0x018
#define E_C         0x5a8 // Mask[42]
#define E_LIGHTSRC  0x860
#define E_SIZE      0x870

#define S_ID        0x008 // slot: map id
#define S_INDEX     0x00c // slot: map index
#define S_STATE     0x1e8 // u8: 0 unloaded .. 5 loaded
#define S_CURRENT   0x1ea // u8

#define EXT_MAGIC 0x3150414d50414d4cull // "LMAPMAP1"

typedef struct WorldExt {
    uint64_t magic;
    uint32_t n, pad;
    void *slot[MAX_MAPS];
    uint8_t avail[MAX_MAPS];
} WorldExt;

typedef struct EnvExt {
    uint64_t magic;
    uint32_t n, pad;
    Mask a[MAX_MAPS], b[MAX_MAPS], c[MAX_MAPS];
} EnvExt;

#define WORLD_SIZE ((uint32_t)(W_SIZE + sizeof(WorldExt)))
#define ENV_SIZE   ((uint32_t)(E_SIZE + sizeof(EnvExt)))
_Static_assert(sizeof(WorldExt) % 16 == 0 && sizeof(EnvExt) % 16 == 0, "16-byte multiples (the game allocates with 0x10 alignment)");

static int cap;    // active map cap (0 = off)
static int active; // all patches in place

// ---- small helpers --------------------------------------------------------------------------------------------------
#define F(off, type) (*(type *)((char *)(base) + (off)))
static inline char *ptr_at(void *base, size_t off) { return *(char **)((char *)base + off); }
static inline Mask mask_g(uint64_t va) { return *(const Mask *)AT(va); }
static inline int mask_eq(Mask a, Mask b) { return a.w[0] == b.w[0] && a.w[1] == b.w[1] && a.w[2] == b.w[2] && a.w[3] == b.w[3]; }
static inline void mask_or(Mask *d, const Mask *s) { d->w[0] |= s->w[0]; d->w[1] |= s->w[1]; d->w[2] |= s->w[2]; d->w[3] |= s->w[3]; }
static inline Mask mask_and(Mask a, Mask b) { Mask r = { { a.w[0] & b.w[0], a.w[1] & b.w[1], a.w[2] & b.w[2], a.w[3] & b.w[3] } }; return r; }
static inline void put_mask(char *p, Mask m) { memcpy(p, &m, sizeof m); }
static inline Mask get_mask(const char *p) { Mask m; memcpy(&m, p, sizeof m); return m; }
// the 128-bit group set of an entity / collision: *(Mask *)([e+0x30]+0x70)
static inline Mask entity_groups(char *e) { return get_mask(*(char **)(*(char **)(e + 0x30) + 0x70)); }
// a collision's own area groups: [coll+0x30]+0x44
static inline Mask coll_area(char *coll) { return get_mask(*(char **)(coll + 0x30) + 0x44); }

typedef void *(*AllocFn)(uint64_t size, uint64_t align, void *allocator);
typedef void *(*SlotCtorFn)(void *mem, void *allocator, void *mapmgr);
typedef char (*SlotInitFn)(void *slot, int map_id, int index);
typedef void (*SlotFn)(void *slot);
typedef void (*SlotArgFn)(void *slot, void *arg);
typedef char (*SlotBoolFn)(void *slot);
typedef void (*SlotSetCurFn)(void *slot, void *coll, int cur, uint8_t flag);
typedef void (*SlotPropFn)(void *slot, const Mask *m, Mask *buf);
typedef void (*VoidFn)(void *x);
typedef int *(*ListIdFn)(void *mapmgr, int *out, int index);
typedef uint8_t (*RegistryFn)(void *reg, int map_id);
typedef int (*MapIndexFn)(void *obj);
typedef int *(*CollIdFn)(void *coll, int *out);
typedef void *(*ConnectsFn)(void *p, void *coll);
typedef void (*ConnectMasksFn)(void *connects, Mask *buf, int count, uint8_t *avail);
typedef char (*BoolObjFn)(void *x);
typedef void *(*GraphicsFn)(void *gm);
typedef void *(*EntityFindFn)(void *x, uint32_t id);
typedef void (*EnvAddFn)(void *env, void *sub, void *entity, float dist);
typedef void (*MapClearFn)(void *map);
typedef void (*MapInsertFn)(void *map, void *out, int zero, void *kv, uint8_t flag);
typedef void (*EnvLightFn)(void *x, int id, float v);
typedef void (*AllocatorFreeFn)(void *allocator, void *p);
typedef void (*DtorFn)(void *obj, int flags);

#define CALL(type, va) ((type)AT(va))

static void game_free(void *allocator, void *p)
{
    ((AllocatorFreeFn)(*(void ***)allocator)[0x68 / 8])(allocator, p);
}

// ---- known objects (grown by our constructors) ----------------------------------------------------------------------
static inline WorldExt *wext(char *w) { return (WorldExt *)(w + W_SIZE); }
static inline EnvExt *eext(char *e) { return (EnvExt *)(e + E_SIZE); }

#define KNOWN 8
static void *known_worlds[KNOWN], *known_envs[KNOWN];
static void remember(void **set, void *p)
{
    for (int i = 0; i < KNOWN; i++) if (set[i] == p) return;
    for (int i = 0; i < KNOWN; i++) if (!set[i]) { set[i] = p; return; }
    memmove(set, set + 1, (KNOWN - 1) * sizeof *set); // oldest out (a freed object's address can only come back grown)
    set[KNOWN - 1] = p;
}
static int is_known(void *const *set, const void *p)
{
    for (int i = 0; i < KNOWN; i++) if (set[i] == p) return 1;
    return 0;
}
static int ext_world(char *w) { return w && is_known(known_worlds, w) && wext(w)->magic == EXT_MAGIC; }
static int ext_env(char *e) { return e && is_known(known_envs, e) && eext(e)->magic == EXT_MAGIC; }

// slot table access (extension; first 42 mirrored in place)
static inline int w_count(char *w) { return *(short *)(w + W_COUNT); }
static inline char *w_slot(char *w, int i) { return (char *)wext(w)->slot[i]; }
static inline void w_set_slot(char *w, int i, void *s)
{
    wext(w)->slot[i] = s;
    if (i < VANILLA_MAPS) ((void **)(w + W_SLOTS))[i] = s;
}
static inline void w_set_avail(char *w, int i, uint8_t v)
{
    wext(w)->avail[i] = v;
    if (i < VANILLA_MAPS) ((uint8_t *)(w + W_AVAIL))[i] = v;
}

// ---- originals (MinHook trampolines) ---------------------------------------------------------------------------------
typedef void *(*WorldCtorFn)(void *w, void *allocator, void *mapmgr);
typedef void *(*EnvCtorFn)(void *e, void *allocator);
typedef void (*WPropagateFn)(void *w, void *coll, Mask *buf);
typedef void *(*WSlotFn)(void *w, int index);
typedef char (*WReadyFn)(void *w, int index);
typedef char (*WAllReadyFn)(void *w);
typedef void (*WVoidFn)(void *w);
typedef void (*WForwardFn)(void *w, void *arg, void *obj);
typedef void (*WUpdateFn)(void *w, void *arg);
static WorldCtorFn orig_world_ctor;
static EnvCtorFn orig_env_ctor;
static WPropagateFn orig_w_propagate;
static WSlotFn orig_w_slot, orig_w_find;
static WReadyFn orig_w_slot_ready, orig_w_map_ready;
static WAllReadyFn orig_w_all_ready;
static WVoidFn orig_w_build, orig_w_destroy;
static WForwardFn orig_w_forward;
static WUpdateFn orig_w_update;

// ---- constructors: original + extension ---------------------------------------------------------------------------
static void *hk_world_ctor(void *w, void *allocator, void *mapmgr)
{
    void *r = orig_world_ctor(w, allocator, mapmgr);
    WorldExt *x = wext((char *)w);
    memset(x, 0, sizeof *x); // the vanilla ctor zeroes slots and avail the same way
    x->magic = EXT_MAGIC;
    x->n = (uint32_t)cap;
    remember(known_worlds, w);
    return r;
}

static void env_init_ext(char *e)
{
    EnvExt *x = eext(e);
    memset(x, 0, sizeof *x);
    Mask empty = mask_g(VA_MASK_EMPTY);
    for (int i = 0; i < MAX_MAPS; i++) x->a[i] = x->b[i] = empty; // FUN_1403db080 for both sub-objects
    // c[] stays zero: the ctor memsets its 0x2a0 bytes (0x1403da69a)
    x->magic = EXT_MAGIC;
    x->n = (uint32_t)cap;
}

static void *hk_env_ctor(void *e, void *allocator)
{
    void *r = orig_env_ctor(e, allocator);
    env_init_ext((char *)e);
    remember(known_envs, e);
    return r;
}

// ---- world: small accessors (FUN_1403dbe60 .. FUN_1403dc010, FUN_1403dc2f0, FUN_1403dc370) ---------------------------
static void *hk_w_slot(void *wv, int index) // FUN_1403dbe60
{
    char *w = (char *)wv;
    if (!ext_world(w)) return orig_w_slot(wv, index);
    if (index >= 0 && index < w_count(w)) return w_slot(w, index);
    return NULL;
}

static void *hk_w_find(void *wv, int map_id) // FUN_1403dbe80
{
    char *w = (char *)wv;
    if (!ext_world(w)) return orig_w_find(wv, map_id);
    for (int i = 0; i < w_count(w); i++) {
        char *s = w_slot(w, i);
        if (s && *(int *)(s + S_ID) == map_id) return s;
    }
    return NULL;
}

static char hk_w_slot_ready(void *wv, int index) // FUN_1403dbf90
{
    char *w = (char *)wv;
    if (!ext_world(w)) return orig_w_slot_ready(wv, index);
    if (index < 0 || index >= w_count(w)) return 0;
    char *s = w_slot(w, index);
    return s ? *(uint8_t *)(s + S_STATE) == 5 : 0;
}

static char hk_w_map_ready(void *wv, int map_id) // FUN_1403dbfc0
{
    char *w = (char *)wv;
    if (!ext_world(w)) return orig_w_map_ready(wv, map_id);
    for (int i = 0; i < w_count(w); i++) {
        char *s = w_slot(w, i);
        if (s && *(int *)(s + S_ID) == map_id) return *(uint8_t *)(s + S_STATE) == 5;
    }
    return 0;
}

static char hk_w_all_ready(void *wv) // FUN_1403dc010
{
    char *w = (char *)wv;
    if (!ext_world(w)) return orig_w_all_ready(wv);
    char *loader = ptr_at(w, W_LOADER);
    char ok = 1;
    if (loader && *(int *)(loader + 0x10) != 4) ok = 0;
    for (int i = 0; i < w_count(w); i++) {
        char *s = w_slot(w, i);
        if (s && !CALL(SlotBoolFn, VA_SLOT_READY)(s)) return 0;
    }
    return ok;
}

static void hk_w_destroy(void *wv) // FUN_1403dc2f0
{
    char *w = (char *)wv;
    if (!ext_world(w)) { orig_w_destroy(wv); return; }
    int n = *(short *)(w + W_LISTN);
    void *allocator = ptr_at(w, W_ALLOCATOR);
    for (int i = 0; i < n && i < MAX_MAPS; i++) {
        char *s = w_slot(w, i);
        if (s) {
            ((DtorFn)(*(void ***)s)[0])(s, 0);
            game_free(allocator, s);
            w_set_slot(w, i, NULL);
        }
    }
    *(uint32_t *)(w + W_COUNT) = 0; // +0x1b6 and +0x1b8 (a dword store in the original)
}

static void hk_w_forward(void *wv, void *arg, void *obj) // FUN_1403dc370
{
    char *w = (char *)wv;
    if (!ext_world(w)) { orig_w_forward(wv, arg, obj); return; }
    if (!obj) return;
    int i = CALL(MapIndexFn, VA_OBJ_MAP_INDEX)(obj);
    if (i < 0 || i >= w_count(w)) return;
    char *s = w_slot(w, i);
    if (s) ((void (*)(void *, void *, void *))(*(void ***)s)[0x20 / 8])(s, arg, obj);
}

static void hk_w_propagate(void *wv, void *coll, Mask *buf) // FUN_1403dbd80 (via mapmgr FUN_1403bcc50)
{
    char *w = (char *)wv;
    if (!ext_world(w)) { orig_w_propagate(wv, coll, buf); return; }
    if (*(uint8_t *)(w + W_NOCONNECT)) return;
    void *c = CALL(ConnectsFn, VA_COLL_CONNECTS)((char *)coll + 0xb8, coll);
    if (c) CALL(ConnectMasksFn, VA_CONNECT_MASKS)(c, buf, w_count(w), wext(w)->avail);
}

// ---- world: build (FUN_1403dc0a0) -----------------------------------------------------------------------------------
static void hk_w_build(void *wv)
{
    char *w = (char *)wv;
    if (!ext_world(w)) { orig_w_build(wv); return; }
    void *allocator = ptr_at(w, W_ALLOCATOR), *mapmgr = ptr_at(w, W_MAPMGR);
    char *list = ptr_at(mapmgr, 0x10);
    int map_id = *(int *)(w + W_MAPID);
    if (list) {
        int n = *(int *)(list + 0x18);
        const int *ids = *(const int **)(list + 0x20);
        if (n <= cap && n > 0) {
            int listed = 0;
            for (int i = 0; i < n; i++) if (ids[i] == map_id) listed = 1;
            if (listed) {
                *(short *)(w + W_LISTN) = (short)n;
                for (int i = 0; i < n; i++) {
                    void *mem = CALL(AllocFn, VA_ALLOC)(0x210, 8, allocator);
                    void *s = mem ? CALL(SlotCtorFn, VA_SLOT_CTOR)(mem, allocator, mapmgr) : NULL;
                    w_set_slot(w, i, s);
                    if (!s) continue;
                    if (CALL(SlotInitFn, VA_SLOT_INIT)(s, ids[i], i))
                        (*(short *)(w + W_COUNT))++;
                    else {
                        ((DtorFn)(*(void ***)s)[0])(s, 0);
                        game_free(allocator, s);
                        w_set_slot(w, i, NULL);
                    }
                }
                goto find_current;
            }
        }
        if (n > cap)
            lr_log(L"map cap: WorldMapList has %d lines, more than map_cap=%d: no map can load (raise map_cap, max %d)", n, cap, MAX_MAPS);
    }
    if (*(uint8_t *)(w + W_MAPID + 3) >= 0x5a) { // AA >= 90: a debug map outside the list gets one slot
        void *mem = CALL(AllocFn, VA_ALLOC)(0x210, 8, allocator);
        void *s = mem ? CALL(SlotCtorFn, VA_SLOT_CTOR)(mem, allocator, mapmgr) : NULL;
        w_set_slot(w, 0, s);
        if (s) {
            short n1 = 1;
            if (!CALL(SlotInitFn, VA_SLOT_INIT)(s, map_id, 0)) {
                ((DtorFn)(*(void ***)s)[0])(s, 0);
                game_free(allocator, s);
                w_set_slot(w, 0, NULL);
                n1 = 0;
            }
            *(short *)(w + W_LISTN) = n1;
            *(short *)(w + W_COUNT) = n1;
        }
    }
find_current:
    for (int i = 0; i < w_count(w); i++) {
        char *s = w_slot(w, i);
        if (s && *(int *)(s + S_ID) == map_id) {
            *(int *)(w + W_CURIDX) = *(int *)(s + S_INDEX);
            return;
        }
    }
    *(int *)(w + W_CURIDX) = 0;
}

// ---- environment object: sub-object update (FUN_1403db1a0) ---------------------------------------------------------
typedef struct EnvNode {
    struct EnvNode *next;
    int id;
    int pad0;
    void *entity;
    uint8_t active, was_active, state, pad1;
    float timer;
    float dist;
} EnvNode;
_Static_assert(__builtin_offsetof(EnvNode, active) == 0x18 && __builtin_offsetof(EnvNode, timer) == 0x1c && __builtin_offsetof(EnvNode, dist) == 0x20, "EnvNode layout");

static Mask *sub_masks(char *env, char *sub) { return sub == env + E_SUB_A ? eext(env)->a : eext(env)->b; }

static void env_sub_update(char *env, const float *dt, const uint8_t *avail, char *sub)
{
    float *p = (float *)sub;
    float t = p[1] - *(const float *)AT(VA_ONE);
    p[0] = t > p[0] ? p[0] : t;
    Mask *m = sub_masks(env, sub);
    Mask empty = mask_g(VA_MASK_EMPTY);
    for (int i = 0; i < MAX_MAPS; i++) m[i] = empty; // the original clears all 42
    float step = *dt;
    EnvNode *node = *(EnvNode **)(sub + 0x10);
    *(EnvNode **)(sub + 0x10) = NULL;
    void *allocator = ptr_at(env, 0);
    while (node) {
        EnvNode *cur = node;
        uint8_t near1 = p[1] >= cur->dist;
        uint8_t act = cur->active;
        cur->was_active = act;
        uint8_t near0 = p[0] >= cur->dist ? near1 : 0;
        node = cur->next;
        float timer = step + cur->timer;
        cur->timer = timer;
        if (act) cur->active = 0; else cur->id = -1;
        uint8_t st = cur->state;
        int ns;
        switch (st) {
        case 1:
            if (!act || !near0) goto free_node;
            ns = 1 + (timer >= p[2]);
            break;
        case 2:
            ns = !act ? 3 : (near1 ? 2 : 3);
            break;
        case 3:
            if (act && near0) { ns = 2; break; }
            if (timer >= p[3]) goto free_node;
            ns = 3;
            break;
        default:
            goto free_node;
        }
        if (ns != st) {
            cur->state = (uint8_t)ns;
            cur->timer = 0.0f;
        }
        cur->next = *(EnvNode **)(sub + 0x10);
        *(EnvNode **)(sub + 0x10) = cur;
        if ((unsigned)(ns - 2) <= 1) {
            char *g = CALL(GraphicsFn, VA_GRAPHICS)(*(void **)AT(VA_GAME_MANAGER));
            char *e = g ? CALL(EntityFindFn, VA_ENTITY_FIND)(*(void **)(g + 0x88), (uint32_t)cur->id | 0x1ffff) : NULL;
            char *ent = e ? *(char **)(e + 8) : NULL;
            int idx = ent ? CALL(MapIndexFn, VA_OBJ_MAP_INDEX)(ent) : -1;
            if (idx >= 0 && idx < MAX_MAPS && avail[idx]) {
                Mask g2 = entity_groups(ent);
                mask_or(&m[idx], &g2);
            }
        }
        continue;
    free_node:
        game_free(allocator, cur);
    }
}

// ---- environment object: update (FUN_1403da960) --------------------------------------------------------------------
static char env_update(char *env, const float *dt, const uint8_t *avail)
{
    char changed = 0;
    if (*(uint8_t *)(env + 0x31) == 1) {
        uint8_t f = *(uint8_t *)(ptr_at(env, E_LIGHTSRC) + 0x30);
        if (f & 2) {
            if (!(f & 4)) {
                CALL(MapClearFn, VA_MAP_CLEAR)(env + 0x848);
                char *src = ptr_at(env, E_LIGHTSRC);
                char *b = ptr_at(src, 0x40), *e = ptr_at(src, 0x48);
                int cnt = (int)((e - b) >> 3);
                for (int i = 0; i < cnt; i++) {
                    char *rec = ptr_at(src, 0x40) + (size_t)i * 8;
                    struct { uint32_t key; float v; } kv = { *(uint32_t *)rec, *(float *)(rec + 4) };
                    uint8_t out[16];
                    CALL(MapInsertFn, VA_MAP_INSERT)(env + 0x848, out, 0, &kv, *(uint8_t *)AT(VA_ENV_BYTE40));
                }
            }
            changed = 1;
            *(uint8_t *)(env + 0x31) = 0;
        }
    }
    if (*(uint8_t *)(env + 0x31) == 0 && *(int *)(env + 8) != -1) {
        float a = *(float *)(env + 0x3c), b = *(float *)(env + 0x2f4);
        float v = a > b ? a : b; // comiss a, b; cmovbe -> b when a <= b or unordered
        CALL(EnvLightFn, VA_ENV_LIGHT)(ptr_at(env, E_LIGHTSRC), *(int *)(env + 8), v);
        *(uint8_t *)(env + 0x31) = 1;
    }
    char *g = CALL(GraphicsFn, VA_GRAPHICS)(*(void **)AT(VA_GAME_MANAGER));
    if (g) { // std::map (MSVC tree, head at env+0x848): in-order walk, node +0x19 = is-nil, key +0x1c, value +0x20
        char *head = ptr_at(env, 0x848);
        char *node = *(char **)head; // leftmost
        while (node != head) {
            float v = *(float *)(node + 0x20);
            void *ent = CALL(EntityFindFn, VA_ENTITY_FIND)(*(void **)(g + 0x88), *(uint32_t *)(node + 0x1c) | 0x1ffff);
            if (ent) {
                CALL(EnvAddFn, VA_ENV_ADD)(env, env + E_SUB_A, ent, v);
                CALL(EnvAddFn, VA_ENV_ADD)(env, env + E_SUB_B, ent, v);
            }
            if (!*(uint8_t *)(node + 0x19)) {
                char *r = *(char **)(node + 0x10);
                if (!*(uint8_t *)(r + 0x19)) {
                    node = r;
                    while (!*(uint8_t *)(*(char **)node + 0x19)) node = *(char **)node;
                } else {
                    char *par = *(char **)(node + 8);
                    while (!*(uint8_t *)(par + 0x19) && node == *(char **)(par + 0x10)) {
                        node = par;
                        par = *(char **)(par + 8);
                    }
                    node = par;
                }
            }
        }
    }
    env_sub_update(env, dt, avail, env + E_SUB_A);
    env_sub_update(env, dt, avail, env + E_SUB_B);
    Mask empty = mask_g(VA_MASK_EMPTY);
    Mask *c = eext(env)->c;
    for (int i = 0; i < MAX_MAPS; i++) c[i] = empty;
    g = CALL(GraphicsFn, VA_GRAPHICS)(*(void **)AT(VA_GAME_MANAGER));
    if (g) {
        float ten = *(const float *)AT(VA_TEN);
        for (EnvNode *nd = *(EnvNode **)(env + E_SUB_B + 0x10); nd; nd = nd->next) {
            if (!(ten >= nd->dist)) continue;
            char *e = CALL(EntityFindFn, VA_ENTITY_FIND)(*(void **)(g + 0x88), (uint32_t)nd->id | 0x1ffff);
            char *ent = e ? *(char **)(e + 8) : NULL;
            if (!ent) continue;
            int idx = CALL(MapIndexFn, VA_OBJ_MAP_INDEX)(ent);
            if (idx < 0 || idx >= MAX_MAPS || !avail[idx]) continue;
            Mask g2 = entity_groups(ent);
            mask_or(&c[idx], &g2);
        }
    }
    return changed;
}

// ---- world: current collision's mask (FUN_1403dd530) ---------------------------------------------------------------
static void w_current_mask(char *w, char *coll, Mask *buf)
{
    Mask area = coll_area(coll);
    int idx = CALL(MapIndexFn, VA_OBJ_MAP_INDEX)(coll);
    if (idx >= 0 && idx < MAX_MAPS) mask_or(&buf[idx], &area);
    if (!*(uint8_t *)(w + W_NOCONNECT)) {
        void *c = CALL(ConnectsFn, VA_COLL_CONNECTS)(coll + 0xb8, coll);
        if (c) CALL(ConnectMasksFn, VA_CONNECT_MASKS)(c, buf, w_count(w), wext(w)->avail);
    }
}

// ---- world: per-frame masks (FUN_1403dc930) -------------------------------------------------------------------------
static void w_masks(char *w, void *arg)
{
    char *gm = *(char **)AT(VA_GAME_MANAGER);
    *(uint8_t *)(w + W_NOCONNECT) = 0;
    char *g70 = ptr_at(gm, 0x70);
    if (g70) {
        char *x = ptr_at(g70, 0x88);
        if (*(int *)(x + 0x204) == 1 && *(int *)(x + 0x14) == 0xfbf40) *(uint8_t *)(w + W_NOCONNECT) = 1;
    }
    int n = w_count(w);
    int cur = *(int *)(w + W_CURIDX);
    Mask empty = mask_g(VA_MASK_EMPTY), full = mask_g(VA_MASK_FULL);
    if (*(uint8_t *)AT(VA_DEBUG_ALL)) {
        for (int i = 0; i < n; i++) *(uint8_t *)(w_slot(w, i) + S_CURRENT) = i == cur; // no null check, as the original
        for (int i = 0; i < n; i++) {
            char *s = w_slot(w, i);
            if (!s) continue;
            Mask m = i == cur ? full : empty;
            put_mask(s + 0x20, m); put_mask(s + 0x30, m); put_mask(s + 0x50, m); put_mask(s + 0x60, m);
        }
        for (int i = 0; i < n; i++) {
            char *s = w_slot(w, i);
            if (!s) continue;
            CALL(SlotSetCurFn, VA_SLOT_SETCUR)(s, ptr_at(w, W_CURCOLL), cur, 1);
            CALL(SlotFn, VA_SLOT_COMMIT)(s);
        }
        return;
    }
    char *env = ptr_at(w, W_ENV);
    EnvExt *ex = eext(env);
    Mask buf_a[MAX_MAPS], buf_b[MAX_MAPS]; // the original's two 42 x 16 stack buffers
    for (int i = 0; i < MAX_MAPS; i++) buf_a[i] = buf_b[i] = empty;
    if (env_update(env, (const float *)arg, wext(w)->avail)) {
        char *coll = ptr_at(w, W_CURCOLL);
        if (!coll) {
            *(void **)AT(VA_LAST_COLL_A) = NULL;
            *(void **)AT(VA_LAST_COLL_B) = NULL;
        } else {
            CALL(MapIndexFn, VA_OBJ_MAP_INDEX)(coll);
            Mask cm = entity_groups(coll);
            Mask a = (cur >= 0 && cur < MAX_MAPS) ? ex->a[cur] : empty;
            if (mask_eq(mask_and(cm, a), empty)) {
                char local = CALL(BoolObjFn, VA_IS_LOCAL)(ptr_at(gm, 0xd0));
                coll = ptr_at(w, W_CURCOLL);
                if (local) {
                    if (*(void **)AT(VA_LAST_COLL_B) != coll) {
                        *(void **)AT(VA_LAST_COLL_A) = coll;
                        *(void **)AT(VA_LAST_COLL_B) = coll;
                    }
                } else if (*(void **)AT(VA_LAST_COLL_A) != coll)
                    *(void **)AT(VA_LAST_COLL_A) = coll;
            } else {
                *(void **)AT(VA_LAST_COLL_A) = NULL;
                *(void **)AT(VA_LAST_COLL_B) = NULL;
            }
            *(uint8_t *)AT(VA_CUR_MAP_INDEX) = (uint8_t)CALL(MapIndexFn, VA_OBJ_MAP_INDEX)(coll);
        }
    }
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (!s || !wext(w)->avail[i]) continue;
        Mask a = ex->a[i], b = ex->b[i];
        CALL(SlotPropFn, VA_SLOT_PROPAGATE)(s, &a, buf_a);
        CALL(SlotPropFn, VA_SLOT_PROPAGATE)(s, &b, buf_b);
    }
    if (ptr_at(w, W_CURCOLL)) w_current_mask(w, ptr_at(w, W_CURCOLL), buf_a);
    uint32_t ovr = *(uint32_t *)(w + W_OVR_IDX);
    int has_ovr = ovr < (uint32_t)cap;
    n = w_count(w); // re-read, as the original
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i); // no null check, as the original
        if (has_ovr)
            *(uint8_t *)(s + S_CURRENT) = (uint32_t)i == ovr;
        else if (i == cur)
            *(uint8_t *)(s + S_CURRENT) = 1;
        else
            *(uint8_t *)(s + S_CURRENT) = !(mask_eq(buf_b[i], empty) && mask_eq(buf_a[i], empty));
    }
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (!s) continue;
        if (has_ovr) {
            if ((uint32_t)i == ovr) {
                put_mask(s + 0x50, get_mask(w + W_OVR_MASK2));
                put_mask(s + 0x60, get_mask(w + W_OVR_MASK));
                put_mask(s + 0x70, ex->c[i]);
            } else {
                put_mask(s + 0x50, empty); put_mask(s + 0x60, empty); put_mask(s + 0x70, empty);
            }
        } else {
            Mask u = buf_b[i];
            mask_or(&u, &buf_a[i]);
            put_mask(s + 0x50, ex->b[i]);
            put_mask(s + 0x60, u);
            put_mask(s + 0x70, ex->c[i]);
        }
    }
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (s) CALL(SlotSetCurFn, VA_SLOT_SETCUR)(s, ptr_at(w, W_CURCOLL), *(int *)(w + W_CURIDX), *(uint8_t *)(w + W_COMMIT));
    }
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (!s) continue;
        if (has_ovr) {
            if ((uint32_t)i == ovr) {
                put_mask(s + 0x20, get_mask(w + W_OVR_MASK2));
                put_mask(s + 0x30, get_mask(w + W_OVR_MASK));
            } else {
                put_mask(s + 0x20, empty); put_mask(s + 0x30, empty);
            }
        } else {
            put_mask(s + 0x20, ex->a[i]);
            put_mask(s + 0x30, buf_a[i]);
        }
    }
    if (*(uint8_t *)(w + W_COMMIT))
        for (int i = 0; i < n; i++) {
            char *s = w_slot(w, i);
            if (s) CALL(SlotFn, VA_SLOT_COMMIT)(s);
        }
}

// ---- world: per-frame update (FUN_1403dc3e0) ------------------------------------------------------------------------
static void hk_w_update(void *wv, void *arg)
{
    char *w = (char *)wv;
    if (!ext_world(w) || !ext_env(ptr_at(w, W_ENV))) { orig_w_update(wv, arg); return; }
    char *gm = *(char **)AT(VA_GAME_MANAGER);
    if (ptr_at(w, W_LOADER)) CALL(VoidFn, VA_LOADER_STEP)(ptr_at(w, W_LOADER));
    void *reg = ptr_at(*(char **)AT(VA_REGISTRY_ROOT), 0x368);
    void *mapmgr = ptr_at(gm, 0x38);
    if (reg && mapmgr) {
        char *list = ptr_at(mapmgr, 0x10);
        int listed = list ? *(int *)(list + 0x18) : 0;
        for (int i = 0; i < cap; i++) {
            // the original asks for all 42 indices and over-reads the id array past the list (FUN_1403bce90 does not
            // bound a non-negative index); the result only matters for i < list lines, so ask only for those
            uint8_t v = 0;
            if (i < listed) {
                int id;
                v = CALL(RegistryFn, VA_REGISTRY_HAS)(reg, *CALL(ListIdFn, VA_LIST_ID)(mapmgr, &id, i));
            }
            w_set_avail(w, i, v);
        }
    } else
        for (int i = 0; i < cap; i++) w_set_avail(w, i, 1);
    w_masks(w, arg);
    int n = w_count(w);
    Mask empty = mask_g(VA_MASK_EMPTY);
    Mask buf[MAX_MAPS];
    for (int i = 0; i < MAX_MAPS; i++) buf[i] = empty;
    uint32_t ovr = *(uint32_t *)(w + W_OVR_IDX);
    char *coll = ptr_at(w, W_COLL);
    if (ovr < (uint32_t)cap)
        buf[ovr] = get_mask(w + W_OVR_MASK);
    else if (coll) {
        int idx = CALL(MapIndexFn, VA_OBJ_MAP_INDEX)(coll);
        if (idx >= 0 && idx < MAX_MAPS) {
            Mask area = coll_area(coll);
            mask_or(&buf[idx], &area);
        }
        coll = ptr_at(w, W_COLL);
        if (!*(uint8_t *)(w + W_NOCONNECT)) {
            void *c = CALL(ConnectsFn, VA_COLL_CONNECTS)(coll + 0xb8, coll);
            if (c) CALL(ConnectMasksFn, VA_CONNECT_MASKS)(c, buf, n, wext(w)->avail);
        }
    }
    if (*(uint8_t *)AT(VA_DEBUG_ALL) && n > 0) {
        Mask full = mask_g(VA_MASK_FULL);
        buf[0] = full;
        for (int i = 1; i < n; i++) buf[i] = buf[0];
    }
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (!s) continue;
        put_mask(s + 0x10, buf[i]);
        put_mask(s + 0x40, buf[i]);
    }
    char *found = NULL;
    coll = ptr_at(w, W_COLL);
    int want;
    if (coll) {
        int id;
        want = *CALL(CollIdFn, VA_COLL_MAP_ID)(coll, &id);
    } else
        want = *(int *)(w + W_MAPID);
    n = w_count(w);
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (s && *(int *)(s + S_ID) == want) { found = s; break; }
    }
    if (found) CALL(SlotFn, VA_SLOT_LIGHTS)(found);
    n = w_count(w);
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (s) CALL(SlotArgFn, VA_SLOT_UPDATE)(s, arg);
    }
    *(uint32_t *)(w + W_LOADED) = 0; // +0x1b2 and +0x1b4 (dword store)
    for (int i = 0; i < n; i++) {
        char *s = w_slot(w, i);
        if (!s) continue;
        uint8_t st = *(uint8_t *)(s + S_STATE);
        if (st == 0) (*(short *)(w + W_UNLOADED))++;
        else if (st == 5) (*(short *)(w + W_LOADED))++;
    }
    if (*(uint8_t *)(w + W_DIRTY)) {
        if (ptr_at(gm, 0x30)) CALL(VoidFn, VA_RES_FLUSH)(ptr_at(gm, 0x30));
        char *loader = ptr_at(w, W_LOADER);
        int done = !loader || *(int *)(loader + 0x10) == 4;
        n = w_count(w);
        for (int i = 0; i < n; i++) {
            char *s = w_slot(w, i);
            if (s && !CALL(SlotBoolFn, VA_SLOT_READY)(s)) return;
        }
        if (done) *(uint8_t *)(w + W_DIRTY) = 0;
    }
}

// =====================================================================================================================
// Generator manager GM40 = [gm+0x40] (one per game session; research notes: data\sandbox\cap11\NOTES.md "GM40"):
//  - B ptr[42] at +0x170 moves to +0x440 (allocation 0x440 -> 0x440 + 8N): every disp32 0x170 site is patched in place;
//  - A ptr[42] at +0x20 grows in place over B's old room (all its disp8 sites stay); its memsets grow to 0x240 bytes and
//    also zero the per-map flag bytes, moved from +0x306 to +0x220;
//  - guards 0x2a/0x29 -> N/N-1, loop counts -> N (FUN_14041bab0 keeps two 48-byte stack arrays: N <= 48);
//  - the enemy-state store at GM40+0x10 (8 + 42 x 0xb10 + guest record at +0x1d0a8) is grown and records 42.. are
//    appended after the guest record; its two record-address functions are replaced below.
// All of these objects are created at the start of a game session, after this module installs (install() refuses to
// run if one already exists), so every instance the patched code sees is a grown one.
// Event flags (FS object): k0 per-map blocks [42] x 0x19 live inline at FS+0x9cc and in the save (chunk 0x6b): maps
// 42.. get extension blocks here, never written into the vanilla save layout (side file below).
// Per-map ESD variables (table [[gm+0x70]+0x28]+0x18, 8 + 42 x 16 bytes, not saved): grown, guards raised.
// =====================================================================================================================
#define MAX_ALLOWED 48            // FUN_14041bab0's stack arrays (2 x 48 bytes before its cookie)
#define GM40_SIZE   0x440
#define GM40_B_NEW  0x440
#define GM40_FLAGS  0x220
#define STORE_SIZE  0x1dbb8
#define STORE_REC   0xb10
#define STORE_GUEST 0x1d0a8
#define K0_SIZE     0x19

#define VA_STORE_INIT   0x1401f6e50ull // void (store)
#define VA_STORE_REC    0x1401f6f40ull // int *(store, index)
#define VA_FS_K0        0x1401855f0ull // char *(fs, int remote, int map id): k0 block
#define VA_FS_RESET     0x140185320ull // void (fs, int remote)
#define VA_FS_PACKET    0x140185ac0ull // char (fs, u8 *packet, int map id): join-sync world state
#define VA_FS_SAVE      0x140185770ull // void (fs, u8 *chunk 0x6b, int *ids, uint n)
#define VA_FS_LOAD      0x1401865a0ull // void (fs, u8 *chunk 0x6b, char skip_mru)
#define VA_MAP_INDEX    0x1403bcec0ull // int (mapmgr, map id): WorldMapList position or -1
#define VA_LIST_COUNT   0x1403bcf20ull // int (mapmgr)
#define VA_IS_GUEST     0x1405135f0ull // char ([gm+0x22f0])
#define VA_MEMSET       0x140c2df70ull

typedef int (*MapIndexOfFn)(void *mapmgr, int map_id);
typedef int (*ListCountFn)(void *mapmgr);
typedef void (*MemsetFn)(void *p, int v, size_t n);
typedef char *(*FsK0Fn)(void *fs, int remote, int map_id);
typedef void (*FsResetFn)(void *fs, int remote);
typedef uint64_t (*FsPacketFn)(void *fs, uint8_t *packet, int map_id);
typedef void (*FsSaveFn)(void *fs, uint8_t *chunk, int *ids, uint32_t n);
typedef void (*FsLoadFn)(void *fs, uint8_t *chunk, char skip);
typedef void (*StoreInitFn)(void *store);
typedef int *(*StoreRecFn)(void *store, uint32_t index);
static FsK0Fn orig_fs_k0;
static FsResetFn orig_fs_reset;
static FsPacketFn orig_fs_packet;
static FsSaveFn orig_fs_save;
static FsLoadFn orig_fs_load;
static StoreInitFn orig_store_init;
static StoreRecFn orig_store_rec;

static uint32_t fnv1a(const uint8_t *p, size_t n)
{
    uint32_t h = 2166136261u;
    for (size_t i = 0; i < n; i++) h = (h ^ p[i]) * 16777619u;
    return h;
}

static char *mapmgr_now(void) { char *gm = *(char **)AT(VA_GAME_MANAGER); return gm ? *(char **)(gm + 0x38) : NULL; }
static int map_index(int map_id) { char *mm = mapmgr_now(); return mm ? CALL(MapIndexOfFn, VA_MAP_INDEX)(mm, map_id) : -1; }
static int list_id(int index)
{
    char *mm = mapmgr_now();
    char *l = mm ? *(char **)(mm + 0x10) : NULL;
    return l && index >= 0 && index < *(int *)(l + 0x18) ? (*(int **)(l + 0x20))[index] : -1;
}

// ---- enemy-state store: record i < 42 at +8 + i*0xb10 (vanilla), i >= 42 at +0x1dbb8 + (i-42)*0xb10 ------------------
static inline int *store_rec(char *store, int i)
{
    return (int *)(i < VANILLA_MAPS ? store + 8 + (size_t)i * STORE_REC : store + STORE_SIZE + (size_t)(i - VANILLA_MAPS) * STORE_REC);
}

static void hk_store_init(void *sv) // FUN_1401f6e50 (GM40 ctor and the session reset): ids of the list, -1 for the rest
{
    char *store = (char *)sv;
    MemsetFn ms = CALL(MemsetFn, VA_MEMSET);
    ms(store + 8, 0, (size_t)VANILLA_MAPS * STORE_REC);
    ms(store + STORE_GUEST, 0, STORE_REC);
    ms(store + STORE_SIZE, 0, (size_t)(cap - VANILLA_MAPS) * STORE_REC);
    char *mm = mapmgr_now();
    if (!mm) return;
    int n = CALL(ListCountFn, VA_LIST_COUNT)(mm);
    if (n > cap) n = cap; // the original writes one id per line with no bound (heap overflow past 42 lines)
    for (int i = 0; i < n; i++) {
        int id;
        *store_rec(store, i) = *CALL(ListIdFn, VA_LIST_ID)(mm, &id, i);
    }
    for (int i = n < 0 ? 0 : n; i < cap; i++) *store_rec(store, i) = -1;
}

static int *hk_store_rec(void *sv, uint32_t index) // FUN_1401f6f40
{
    char *store = (char *)sv;
    char *gm = *(char **)AT(VA_GAME_MANAGER);
    void *sess = *(void **)(gm + 0x22f0);
    if (!sess || !CALL(BoolObjFn, VA_IS_GUEST)(sess))
        return index < (uint32_t)cap ? store_rec(store, (int)index) : NULL;
    char *mm = *(char **)(gm + 0x38);
    if (!mm) return NULL;
    int id;
    CALL(ListIdFn, VA_LIST_ID)(mm, &id, (int)index);
    return id == *(int *)(store + STORE_GUEST) ? (int *)(store + STORE_GUEST) : NULL;
}

// ---- event flags: k0 blocks of maps 42.. ---------------------------------------------------------------------------
// Without this a map with index >= 42 gets its k0 flag block registered with a NULL data pointer (FUN_1404745c0 ->
// FUN_140474db0(.., FUN_1401855f0() = 0, 0x19)), and the first read or write of one of its 200 k0 flags crashes.
#define FS_KNOWN 4
typedef uint8_t K0Ext[MAX_ALLOWED - VANILLA_MAPS][K0_SIZE];
static struct { void *fs; K0Ext k0; } fs_ext[FS_KNOWN];
static K0Ext *fs_k0_ext(void *fs)
{
    for (int i = 0; i < FS_KNOWN; i++) if (fs_ext[i].fs == fs) return &fs_ext[i].k0;
    for (int i = 0; i < FS_KNOWN; i++)
        if (!fs_ext[i].fs) { fs_ext[i].fs = fs; memset(fs_ext[i].k0, 0, sizeof(K0Ext)); return &fs_ext[i].k0; }
    memmove(fs_ext, fs_ext + 1, sizeof fs_ext[0] * (FS_KNOWN - 1));
    fs_ext[FS_KNOWN - 1].fs = fs;
    memset(fs_ext[FS_KNOWN - 1].k0, 0, sizeof(K0Ext));
    return &fs_ext[FS_KNOWN - 1].k0;
}

static char *hk_fs_k0(void *fs, int remote, int map_id) // FUN_1401855f0
{
    if (remote == 0 && mapmgr_now()) {
        int i = map_index(map_id);
        if (i >= VANILLA_MAPS && i < cap) return (char *)(*fs_k0_ext(fs))[i - VANILLA_MAPS];
    }
    return orig_fs_k0(fs, remote, map_id);
}

static void hk_fs_reset(void *fs, int remote) // FUN_140185320
{
    orig_fs_reset(fs, remote);
    if (remote == 0) memset(*fs_k0_ext(fs), 0, sizeof(K0Ext));
}

static uint64_t hk_fs_packet(void *fsv, uint8_t *pk, int map_id) // FUN_140185ac0: the host's world state for a joiner
{
    uint64_t r = orig_fs_packet(fsv, pk, map_id);
    if ((uint8_t)r) return r;
    char *fs = (char *)fsv;
    int slot = -1;
    for (int k = 0; k < 3; k++) if (*(int *)(fs + 0xe7c + 4 * k) == map_id) { slot = k; break; }
    int i = mapmgr_now() ? map_index(map_id) : -1;
    if (slot < 0 || i < VANILLA_MAPS || i >= cap) return r;
    memcpy(pk, fs + 8, 0x4e2);
    memcpy(pk + 0x4e2, fs + 0x4ea, 0x4e2);
    memcpy(pk + 0x9c4, (*fs_k0_ext(fs))[i - VANILLA_MAPS], K0_SIZE);
    memcpy(pk + 0x9dd, fs + 0xde6 + K0_SIZE * slot, K0_SIZE);
    memcpy(pk + 0x9f6, fs + 0xe31 + K0_SIZE * slot, K0_SIZE);
    return 1;
}

// ---- side file: k0 blocks and enemy-state records of maps 42.. (the vanilla save keeps exactly 42 of each) ----------
// <game>\lordran\mapcap\slot<n>.bin = up to SIDE_KEEP records {SideHdr, ids[], k0[][0x19], store records[][0xb10]},
// newest first. A record is tied to one save write by a fingerprint of the chunk-0x6b bytes it was written with (global
// flags + the 42 k0 blocks), so loading an older .sl2 backup finds its own record and another save finds none.
#define SIDE_MAGIC 0x3150434dU // "MCP1"
#define SIDE_KEEP  8
typedef struct SideHdr { uint32_t magic, slot, fp, n; } SideHdr;
static wchar_t side_dir[MAX_PATH]; // set by lr_mapcap_set_dir (main.c); empty = no side file
static int side_writes, side_loads, side_restored;

void lr_mapcap_set_dir(const wchar_t *lordran_dir)
{
    swprintf(side_dir, MAX_PATH, L"%lsmapcap\\", lordran_dir);
    CreateDirectoryW(side_dir, NULL);
}

static int char_slot(void)
{
    char *gm = *(char **)AT(VA_GAME_MANAGER);
    char *a8 = gm ? *(char **)(gm + 0xa8) : NULL;
    char *d8 = a8 ? *(char **)(a8 + 0xd8) : NULL;
    int s = d8 ? *(int *)(d8 + 0x1368) : -1;
    return s >= 0 && s < 10 ? s : -1;
}
static uint32_t chunk_fp(const uint8_t *chunk) { return fnv1a(chunk, 0xde2); }
static char *store_now(void)
{
    char *gm = *(char **)AT(VA_GAME_MANAGER);
    char *g40 = gm ? *(char **)(gm + 0x40) : NULL;
    return g40 ? *(char **)(g40 + 0x10) : NULL;
}
static size_t side_rec_size(int n) { return sizeof(SideHdr) + (size_t)n * (4 + K0_SIZE + STORE_REC); }
static void side_path(int slot, wchar_t *out) { swprintf(out, MAX_PATH, L"%lsslot%d.bin", side_dir, slot); }

static void hk_fs_save(void *fs, uint8_t *chunk, int *ids, uint32_t n) // FUN_140185770 (chunk 0x6b writer)
{
    orig_fs_save(fs, chunk, ids, n);
    int slot = char_slot(), extra = cap - VANILLA_MAPS;
    char *store = store_now();
    if (slot < 0 || extra <= 0 || !side_dir[0]) return;
    size_t rs = side_rec_size(extra);
    uint8_t *rec = calloc(SIDE_KEEP, rs);
    if (!rec) return;
    SideHdr *h = (SideHdr *)rec;
    *h = (SideHdr){ SIDE_MAGIC, (uint32_t)slot, chunk_fp(chunk), (uint32_t)extra };
    uint8_t *p = rec + sizeof *h;
    for (int i = 0; i < extra; i++, p += 4) { int id = list_id(VANILLA_MAPS + i); memcpy(p, &id, 4); }
    memcpy(p, *fs_k0_ext(fs), (size_t)extra * K0_SIZE);
    p += (size_t)extra * K0_SIZE;
    for (int i = 0; i < extra; i++, p += STORE_REC)
        if (store) memcpy(p, store_rec(store, VANILLA_MAPS + i), STORE_REC);
    wchar_t path[MAX_PATH];
    side_path(slot, path);
    int kept = 1;
    HANDLE f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (f != INVALID_HANDLE_VALUE) {
        DWORD got = 0;
        uint8_t *old = malloc(rs * SIDE_KEEP);
        if (old && ReadFile(f, old, (DWORD)(rs * SIDE_KEEP), &got, NULL))
            for (size_t k = 0; k + rs <= got && kept < SIDE_KEEP; k += rs) {
                SideHdr *o = (SideHdr *)(old + k);
                if (o->magic == SIDE_MAGIC && o->n == (uint32_t)extra && o->fp != h->fp) memcpy(rec + rs * kept++, old + k, rs);
            }
        free(old);
        CloseHandle(f);
    }
    wchar_t tmp[MAX_PATH];
    swprintf(tmp, MAX_PATH, L"%ls.tmp", path);
    f = CreateFileW(tmp, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f != INVALID_HANDLE_VALUE) {
        DWORD put = 0;
        BOOL ok = WriteFile(f, rec, (DWORD)(rs * kept), &put, NULL);
        CloseHandle(f);
        if (ok && put == rs * kept && MoveFileExW(tmp, path, MOVEFILE_REPLACE_EXISTING)) side_writes++;
    }
    free(rec);
}

static void hk_fs_load(void *fs, uint8_t *chunk, char skip) // FUN_1401865a0 (chunk 0x6b reader)
{
    orig_fs_load(fs, chunk, skip);
    int extra = cap - VANILLA_MAPS, slot = char_slot();
    memset(*fs_k0_ext(fs), 0, sizeof(K0Ext));
    side_loads++;
    if (slot < 0 || extra <= 0 || !side_dir[0]) return;
    size_t rs = side_rec_size(extra);
    wchar_t path[MAX_PATH];
    side_path(slot, path);
    HANDLE f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    uint8_t *buf = malloc(rs * SIDE_KEEP);
    DWORD got = 0;
    uint32_t fp = chunk_fp(chunk);
    int restored = 0;
    if (buf && ReadFile(f, buf, (DWORD)(rs * SIDE_KEEP), &got, NULL))
        for (size_t k = 0; k + rs <= got; k += rs) {
            SideHdr *h = (SideHdr *)(buf + k);
            if (h->magic != SIDE_MAGIC || h->n != (uint32_t)extra || h->slot != (uint32_t)slot || h->fp != fp) continue;
            const uint8_t *p = buf + k + sizeof *h;
            const uint8_t *k0 = p + 4 * extra, *recs = k0 + (size_t)extra * K0_SIZE;
            char *store = store_now();
            for (int i = 0; i < extra; i++) {
                int id;
                memcpy(&id, p + 4 * i, 4);
                if (id == -1 || id != list_id(VANILLA_MAPS + i)) continue; // the list changed: drop that map's data
                memcpy((*fs_k0_ext(fs))[i], k0 + (size_t)i * K0_SIZE, K0_SIZE);
                if (store && *store_rec(store, VANILLA_MAPS + i) == id) memcpy(store_rec(store, VANILLA_MAPS + i), recs + (size_t)i * STORE_REC, STORE_REC);
                restored++;
            }
            break;
        }
    free(buf);
    CloseHandle(f);
    side_restored += restored;
    lr_log(L"map cap: save slot %d loaded; %d extension map record(s) restored from the side file", slot, restored);
}

// =====================================================================================================================
// Map manager sub-objects with slots[42] at +8 (created at session start by FUN_1403bd510; research notes NOTES.md):
//  - MM+0x1b0 "visibility groups" (0x330 bytes) and MM+0x1a8 "area sound" (0x190 bytes): the allocation grows and a
//    [N] extension follows the object (VIS_EXT / SND_EXT). Insert and remove (unguarded in the game: an index >= 42
//    would write into the next fields) are replaced; the guarded get-and-tail-jump entries are replaced by the asm
//    stubs below (same register contract, extension table); clear-all and the per-frame update are wrapped and run
//    the original for entries 0..41, which stay mirrored in place.
//  - MM+0x1c0 "graphics params" (0x250 bytes) keeps slots[42] at its tail (+0x100): it grows in place (loop counts are
//    patched); the wrapped ctor zeroes the new entries.
// =====================================================================================================================
#define VIS_SIZE  0x330
#define SND_SIZE  0x190
#define GFX_SIZE  0x250
#define GFX_SLOTS 0x100
#define POOL_HDR  0x24
#define POOL_REC  0xc08

#define VA_VIS_CTOR    0x1403c8730ull // obj *(obj, allocator)
#define VA_VIS_INSERT  0x1403c8870ull // void (obj, slot object)
#define VA_VIS_REMOVE  0x1403c88f0ull // void (obj, index)
#define VA_VIS_CLEAR   0x1403c8950ull // void (obj)
#define VA_VIS_HIDE    0x1403c89e0ull // tail-jumps to FUN_1403c7d60(entry, r8d)
#define VA_VIS_SHOW    0x1403c8ff0ull // tail-jumps to FUN_1403c7df0(entry, r8d)
#define VA_VIS_UPDATE  0x1403c8f20ull // void (obj, float *dt)
#define VA_SND_CTOR    0x1403e3fe0ull
#define VA_SND_INSERT  0x1403e4060ull
#define VA_SND_REMOVE  0x1403e41b0ull
#define VA_SND_CLEAR   0x1403e42b0ull
#define VA_SND_A       0x1403e45c0ull // tail-jumps to FUN_1403e2c30
#define VA_SND_B       0x1403e45f0ull // tail-jumps to FUN_1403e2d40
#define VA_SND_C       0x1403e46e0ull // tail-jumps to FUN_1403e2f00
#define VA_SND_UPDATE  0x1403e4700ull // void (obj, arg)
#define VA_GFX_CTOR    0x14040b270ull
#define VA_SLOT_INDEX  0x1403bb3d0ull // int (slot object) = [[obj+8]+0xc]
#define VA_VIS_NEW     0x1403c73a0ull // entry *(mem, allocator)
#define VA_VIS_BIND    0x1403c74c0ull // void (entry, slot object)
#define VA_VIS_UNBIND  0x1403c73d0ull // void (entry)
#define VA_VIS_DTOR    0x1403c73c0ull // void (entry)
#define VA_VIS_TICK    0x1403c7e80ull // void (entry, float *dt)
#define VA_SND_NEW     0x1403e21f0ull // entry *(mem, allocator, obj+0x160)
#define VA_SND_BIND    0x1403e24f0ull
#define VA_SND_UNBIND  0x1403e2410ull
#define VA_SND_DTOR    0x1403e23e0ull
#define VA_SND_TICK    0x1403e2fe0ull // void (entry, arg, u8, u8)

typedef void *(*ObjCtorFn)(void *obj, void *allocator);
typedef void *(*EntryNewFn)(void *mem, void *allocator);
typedef void *(*EntryNew3Fn)(void *mem, void *allocator, void *x);
typedef void (*EntryBindFn)(void *e, void *slotobj);
typedef void (*EntryTickFn)(void *e, void *arg);
typedef void (*SndTickFn)(void *e, void *arg, uint32_t a, uint32_t b);
typedef void (*ObjIndexFn)(void *obj, int index);
typedef void (*ObjArgFn)(void *obj, void *arg);
static ObjCtorFn orig_vis_ctor, orig_snd_ctor, orig_gfx_ctor;
static EntryBindFn orig_vis_insert, orig_snd_insert;
static ObjIndexFn orig_vis_remove, orig_snd_remove;
static VoidFn orig_vis_clear, orig_snd_clear;
static ObjArgFn orig_vis_update, orig_snd_update;
static void *orig_vis_hide, *orig_vis_show, *orig_snd_a, *orig_snd_b, *orig_snd_c; // unused trampolines

// read by the asm stubs
uint32_t mapcap_n1 = VANILLA_MAPS - 1;
void *mapcap_t_vis_hide, *mapcap_t_vis_show, *mapcap_t_snd_a, *mapcap_t_snd_b, *mapcap_t_snd_c;

static inline void **mm_ext(char *obj, int size) { return (void **)(obj + size); }
static inline void mm_set(char *obj, int size, int i, void *e)
{
    mm_ext(obj, size)[i] = e;
    if (i < VANILLA_MAPS) ((void **)(obj + 8))[i] = e;
}

static void *hk_vis_ctor(void *obj, void *allocator)
{
    void *r = orig_vis_ctor(obj, allocator);
    memset(mm_ext((char *)obj, VIS_SIZE), 0, sizeof(void *) * MAX_MAPS);
    return r;
}
static void *hk_snd_ctor(void *obj, void *allocator)
{
    void *r = orig_snd_ctor(obj, allocator);
    memset(mm_ext((char *)obj, SND_SIZE), 0, sizeof(void *) * MAX_MAPS);
    return r;
}
static void *hk_gfx_ctor(void *obj, void *allocator)
{
    void *r = orig_gfx_ctor(obj, allocator);
    memset((char *)obj + GFX_SIZE, 0, 8 * (size_t)(cap - VANILLA_MAPS)); // slots 42.. of the in-place-grown array
    return r;
}

static void hk_vis_insert(void *objv, void *slotobj) // FUN_1403c8870
{
    char *obj = (char *)objv;
    if (!slotobj) return;
    int i = CALL(MapIndexFn, VA_SLOT_INDEX)(slotobj);
    if (i < 0 || i >= cap || mm_ext(obj, VIS_SIZE)[i]) return;
    void *allocator = *(void **)obj;
    void *mem = CALL(AllocFn, VA_ALLOC)(0x20, 8, allocator);
    void *e = mem ? CALL(EntryNewFn, VA_VIS_NEW)(mem, allocator) : NULL;
    mm_set(obj, VIS_SIZE, i, e);
    if (e) CALL(EntryBindFn, VA_VIS_BIND)(e, slotobj);
}
static void hk_snd_insert(void *objv, void *slotobj) // FUN_1403e4060
{
    char *obj = (char *)objv;
    if (!slotobj) return;
    int i = CALL(MapIndexFn, VA_SLOT_INDEX)(slotobj);
    if (i < 0 || i >= cap || mm_ext(obj, SND_SIZE)[i]) return;
    void *allocator = *(void **)obj;
    void *mem = CALL(AllocFn, VA_ALLOC)(0x58, 8, allocator);
    void *e = mem ? CALL(EntryNew3Fn, VA_SND_NEW)(mem, allocator, obj + 0x160) : NULL;
    mm_set(obj, SND_SIZE, i, e);
    if (e) CALL(EntryBindFn, VA_SND_BIND)(e, slotobj);
}

static void mm_drop(char *obj, int size, int i, uint64_t unbind, uint64_t dtor)
{
    void *e = mm_ext(obj, size)[i];
    if (!e) return;
    CALL(SlotFn, unbind)(e);
    void *allocator = *(void **)obj;
    void *e2 = mm_ext(obj, size)[i];
    if (e2) {
        CALL(SlotFn, dtor)(e2);
        game_free(allocator, e2);
    }
    mm_set(obj, size, i, NULL);
}
static void hk_vis_remove(void *obj, int i) { if (i >= 0 && i < cap) mm_drop((char *)obj, VIS_SIZE, i, VA_VIS_UNBIND, VA_VIS_DTOR); } // FUN_1403c88f0
static void hk_snd_remove(void *obj, int i) { if (i >= 0 && i < cap) mm_drop((char *)obj, SND_SIZE, i, VA_SND_UNBIND, VA_SND_DTOR); } // FUN_1403e41b0

static void hk_vis_clear(void *objv) // FUN_1403c8950: extension entries, then the original for 0..41 (mirrored in place)
{
    char *obj = (char *)objv;
    for (int i = VANILLA_MAPS; i < cap; i++) mm_drop(obj, VIS_SIZE, i, VA_VIS_UNBIND, VA_VIS_DTOR);
    orig_vis_clear(objv);
    for (int i = 0; i < VANILLA_MAPS; i++) mm_ext(obj, VIS_SIZE)[i] = ((void **)(obj + 8))[i];
}
static void hk_snd_clear(void *objv) // FUN_1403e42b0
{
    char *obj = (char *)objv;
    for (int i = VANILLA_MAPS; i < cap; i++) mm_drop(obj, SND_SIZE, i, VA_SND_UNBIND, VA_SND_DTOR);
    orig_snd_clear(objv);
    for (int i = 0; i < VANILLA_MAPS; i++) mm_ext(obj, SND_SIZE)[i] = ((void **)(obj + 8))[i];
}

static void hk_vis_update(void *objv, void *dt) // FUN_1403c8f20
{
    orig_vis_update(objv, dt);
    if (0.0f >= *(float *)dt) return;
    for (int i = VANILLA_MAPS; i < cap; i++) {
        void *e = mm_ext((char *)objv, VIS_SIZE)[i];
        if (e) CALL(EntryTickFn, VA_VIS_TICK)(e, dt);
    }
}
static void hk_snd_update(void *objv, void *arg) // FUN_1403e4700
{
    char *obj = (char *)objv;
    orig_snd_update(objv, arg);
    if (!*(uint8_t *)(obj + 0x15b)) return;
    char *mm = mapmgr_now();
    int cur = mm ? *(int *)(mm + 0x170) : -1;
    for (int i = VANILLA_MAPS; i < cap; i++) {
        void *e = mm_ext(obj, SND_SIZE)[i];
        if (e) CALL(SndTickFn, VA_SND_TICK)(e, arg, *(uint8_t *)(obj + 0x159), *(uint8_t *)(obj + 0x15a) ? i == cur : 0);
    }
}

// Guarded get-and-tail-jump entries: the original bodies with 0x29 -> N-1 and the table read from the extension.
void mapcap_stub_vis_hide(void), mapcap_stub_vis_show(void), mapcap_stub_snd_a(void), mapcap_stub_snd_b(void), mapcap_stub_snd_c(void);
__asm__(
    ".intel_syntax noprefix\n"
    ".text\n"
    ".globl mapcap_stub_vis_hide\n"
    "mapcap_stub_vis_hide:\n"                          // FUN_1403c89e0
    "  cmp edx, dword ptr [rip + mapcap_n1]\n"
    "  ja 1f\n"
    "  movsxd rax, edx\n"
    "  mov rcx, qword ptr [rcx + 8*rax + 0x330]\n"
    "  test rcx, rcx\n"
    "  je 1f\n"
    "  mov edx, r8d\n"
    "  jmp qword ptr [rip + mapcap_t_vis_hide]\n"
    "1: ret\n"
    ".globl mapcap_stub_vis_show\n"
    "mapcap_stub_vis_show:\n"                          // FUN_1403c8ff0
    "  cmp edx, dword ptr [rip + mapcap_n1]\n"
    "  ja 1f\n"
    "  movsxd rax, edx\n"
    "  mov rcx, qword ptr [rcx + 8*rax + 0x330]\n"
    "  test rcx, rcx\n"
    "  je 1f\n"
    "  mov edx, r8d\n"
    "  jmp qword ptr [rip + mapcap_t_vis_show]\n"
    "1: ret\n"
    ".globl mapcap_stub_snd_a\n"
    "mapcap_stub_snd_a:\n"                             // FUN_1403e45c0
    "  mov r10d, r8d\n"
    "  cmp edx, dword ptr [rip + mapcap_n1]\n"
    "  ja 1f\n"
    "  movsxd rax, edx\n"
    "  mov rcx, qword ptr [rcx + 8*rax + 0x190]\n"
    "  test rcx, rcx\n"
    "  je 1f\n"
    "  movzx r8d, r9b\n"
    "  mov edx, r10d\n"
    "  jmp qword ptr [rip + mapcap_t_snd_a]\n"
    "1: ret\n"
    ".globl mapcap_stub_snd_b\n"
    "mapcap_stub_snd_b:\n"                             // 0x1403e45f0
    "  movaps xmm2, xmm3\n"
    "  cmp edx, dword ptr [rip + mapcap_n1]\n"
    "  ja 1f\n"
    "  movsxd rax, edx\n"
    "  mov rcx, qword ptr [rcx + 8*rax + 0x190]\n"
    "  test rcx, rcx\n"
    "  je 1f\n"
    "  movss xmm0, dword ptr [rsp + 0x30]\n"
    "  movss xmm3, dword ptr [rsp + 0x28]\n"
    "  mov edx, r8d\n"
    "  movss dword ptr [rsp + 0x28], xmm0\n"
    "  jmp qword ptr [rip + mapcap_t_snd_b]\n"
    "1: ret\n"
    ".globl mapcap_stub_snd_c\n"
    "mapcap_stub_snd_c:\n"                             // FUN_1403e46e0
    "  cmp edx, dword ptr [rip + mapcap_n1]\n"
    "  ja 1f\n"
    "  movsxd rax, edx\n"
    "  mov rcx, qword ptr [rcx + 8*rax + 0x190]\n"
    "  test rcx, rcx\n"
    "  je 1f\n"
    "  mov edx, r8d\n"
    "  jmp qword ptr [rip + mapcap_t_snd_c]\n"
    "1: ret\n"
    ".att_syntax prefix\n");
_Static_assert(VIS_SIZE == 0x330 && SND_SIZE == 0x190, "the asm stubs hard-code the extension offsets");

static void mm_stub_targets(void)
{
    mapcap_n1 = (uint32_t)cap - 1;
    mapcap_t_vis_hide = (void *)AT(0x1403c7d60);
    mapcap_t_vis_show = (void *)AT(0x1403c7df0);
    mapcap_t_snd_a = (void *)AT(0x1403e2c30);
    mapcap_t_snd_b = (void *)AT(0x1403e2d40);
    mapcap_t_snd_c = (void *)AT(0x1403e2f00);
}

// ---- immediate patches (generated table) ----------------------------------------------------------------------------
enum { K_N, K_N1, K_GM_SIZE, K_B_OFF, K_B_MINUS_A, K_A_ZERO, K_B_ZERO, K_FLAGS_OFF, K_STORE_SIZE, K_EV_SIZE, K_EV_ZERO,
       K_VIS_SIZE, K_SND_SIZE, K_GFX_SIZE, K_POOL_SIZE };
typedef struct IPatch { uint64_t va; int size; uint32_t orig; int kind; const char *what; } IPatch;
#define IPATCH(va, size, orig, kind, what) { va, size, orig, kind, what },
static const IPatch ipatches[] = {
    { 0x1403bd295, 4, W_SIZE, -1, "world allocation size" },
    { 0x1403dbf48, 4, E_SIZE, -2, "env allocation size" },
#include "mapcap_patches.inc"
};
static uint32_t patch_value(const IPatch *p)
{
    switch (p->kind) {
    case -1: return WORLD_SIZE;
    case -2: return ENV_SIZE;
    case K_N: return (uint32_t)cap;
    case K_N1: return (uint32_t)cap - 1;
    case K_GM_SIZE: return GM40_SIZE + 8u * (uint32_t)cap;
    case K_B_OFF: return GM40_B_NEW;
    case K_B_MINUS_A: return GM40_B_NEW - 0x20;
    case K_A_ZERO: return GM40_FLAGS + MAX_MAPS - 0x20; // A[N] and the flag bytes at +0x220..+0x25f
    case K_B_ZERO: return 8u * (uint32_t)cap;
    case K_FLAGS_OFF: return GM40_FLAGS;
    case K_STORE_SIZE: return STORE_SIZE + STORE_REC * (uint32_t)(cap - VANILLA_MAPS);
    case K_EV_SIZE: return 8 + 16u * (uint32_t)cap;
    case K_EV_ZERO: return 16u * (uint32_t)cap;
    case K_VIS_SIZE: return VIS_SIZE + 8u * MAX_MAPS;
    case K_SND_SIZE: return SND_SIZE + 8u * MAX_MAPS;
    case K_GFX_SIZE: return GFX_SLOTS + 8u * (uint32_t)cap;
    case K_POOL_SIZE: return POOL_HDR + POOL_REC * (uint32_t)cap;
    }
    return p->orig;
}
_Static_assert(0x20 + 8 * MAX_ALLOWED <= GM40_FLAGS, "GM40 A[N] must end before the relocated flag bytes");
_Static_assert(GM40_FLAGS + MAX_MAPS <= 0x2c0, "relocated flag bytes must stay inside B's old room (0x170..0x2bf)");

// =====================================================================================================================
// Self-check tables. RANGE = a whole function (or chunk) this module replaces or relies on: its bytes must hash to the
// value taken from DarkSoulsII.exe v1.0.3.0 (FNV-1a 32; no base relocation inside any range, so the bytes are the same
// at any load address). Because call/jmp displacements and RIP-relative data offsets are part of the hashed bytes, a
// match also proves every callee and global the replacement uses is where it was. CSIG = exact bytes at a patch site.
// All three tables are generated and checked against the exe offline by data\sandbox\cap11\captools\capsig.py.
// =====================================================================================================================
typedef struct Range { uint64_t va; uint32_t len; uint32_t fnv; const char *what; } Range;
typedef struct CapSig { uint64_t va; const char *what; const char *bytes; } CapSig;
#define RANGE(va, len, fnv, what) { va, len, fnv, what },
#define CSIG(va, what, bytes) { va, what, bytes },

static const Range ranges[] = {
#include "mapcap_ranges.inc"
};
static const CapSig csigs[] = {
#include "mapcap_sigs.inc"
};

typedef struct HookDef { uint64_t va; void *detour; void **orig; const char *what; } HookDef;
static const HookDef hooks[] = {
    { VA_WORLD_CTOR, (void *)hk_world_ctor, (void **)&orig_world_ctor, "world ctor" },
    { VA_ENV_CTOR, (void *)hk_env_ctor, (void **)&orig_env_ctor, "env ctor" },
    { VA_W_PROPAGATE, (void *)hk_w_propagate, (void **)&orig_w_propagate, "world propagate connects" },
    { VA_W_SLOT, (void *)hk_w_slot, (void **)&orig_w_slot, "world slot(index)" },
    { VA_W_FIND, (void *)hk_w_find, (void **)&orig_w_find, "world slot(map id)" },
    { VA_W_SLOT_READY, (void *)hk_w_slot_ready, (void **)&orig_w_slot_ready, "world slot ready(index)" },
    { VA_W_MAP_READY, (void *)hk_w_map_ready, (void **)&orig_w_map_ready, "world map ready(map id)" },
    { VA_W_ALL_READY, (void *)hk_w_all_ready, (void **)&orig_w_all_ready, "world all ready" },
    { VA_W_BUILD, (void *)hk_w_build, (void **)&orig_w_build, "world build" },
    { VA_W_DESTROY, (void *)hk_w_destroy, (void **)&orig_w_destroy, "world destroy" },
    { VA_W_FORWARD, (void *)hk_w_forward, (void **)&orig_w_forward, "world forward" },
    { VA_W_UPDATE, (void *)hk_w_update, (void **)&orig_w_update, "world update" },
    { VA_STORE_INIT, (void *)hk_store_init, (void **)&orig_store_init, "enemy-state store init" },
    { VA_STORE_REC, (void *)hk_store_rec, (void **)&orig_store_rec, "enemy-state store record" },
    { VA_FS_K0, (void *)hk_fs_k0, (void **)&orig_fs_k0, "flag k0 block lookup" },
    { VA_FS_RESET, (void *)hk_fs_reset, (void **)&orig_fs_reset, "flag object reset" },
    { VA_FS_PACKET, (void *)hk_fs_packet, (void **)&orig_fs_packet, "join-sync flag packet" },
    { VA_FS_SAVE, (void *)hk_fs_save, (void **)&orig_fs_save, "flag save writer" },
    { VA_FS_LOAD, (void *)hk_fs_load, (void **)&orig_fs_load, "flag save reader" },
    { VA_VIS_CTOR, (void *)hk_vis_ctor, (void **)&orig_vis_ctor, "visibility ctor" },
    { VA_VIS_INSERT, (void *)hk_vis_insert, (void **)&orig_vis_insert, "visibility insert" },
    { VA_VIS_REMOVE, (void *)hk_vis_remove, (void **)&orig_vis_remove, "visibility remove" },
    { VA_VIS_CLEAR, (void *)hk_vis_clear, (void **)&orig_vis_clear, "visibility clear" },
    { VA_VIS_HIDE, (void *)mapcap_stub_vis_hide, (void **)&orig_vis_hide, "visibility hide" },
    { VA_VIS_SHOW, (void *)mapcap_stub_vis_show, (void **)&orig_vis_show, "visibility show" },
    { VA_VIS_UPDATE, (void *)hk_vis_update, (void **)&orig_vis_update, "visibility update" },
    { VA_SND_CTOR, (void *)hk_snd_ctor, (void **)&orig_snd_ctor, "area sound ctor" },
    { VA_SND_INSERT, (void *)hk_snd_insert, (void **)&orig_snd_insert, "area sound insert" },
    { VA_SND_REMOVE, (void *)hk_snd_remove, (void **)&orig_snd_remove, "area sound remove" },
    { VA_SND_CLEAR, (void *)hk_snd_clear, (void **)&orig_snd_clear, "area sound clear" },
    { VA_SND_A, (void *)mapcap_stub_snd_a, (void **)&orig_snd_a, "area sound entry A" },
    { VA_SND_B, (void *)mapcap_stub_snd_b, (void **)&orig_snd_b, "area sound entry B" },
    { VA_SND_C, (void *)mapcap_stub_snd_c, (void **)&orig_snd_c, "area sound entry C" },
    { VA_SND_UPDATE, (void *)hk_snd_update, (void **)&orig_snd_update, "area sound update" },
    { VA_GFX_CTOR, (void *)hk_gfx_ctor, (void **)&orig_gfx_ctor, "graphics params ctor" },
};

#define COUNT(a) (sizeof(a) / sizeof *(a))

static int cap_hexval(char c) { return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; }

static int check_csig(const CapSig *s)
{
    const uint8_t *p = (const uint8_t *)AT(s->va);
    int n = 0;
    for (const char *h = s->bytes; *h;) {
        if (*h == ' ') { h++; continue; }
        int hi = cap_hexval(h[0]), lo = h[1] ? cap_hexval(h[1]) : -1;
        if (hi < 0 || lo < 0 || p[n] != (uint8_t)(hi << 4 | lo)) {
            lr_log(L"map cap: %hs at exe+%llx differs from v1.0.3.0 (byte %d)", s->what, (unsigned long long)(s->va - EXE_BASE), n);
            return 0;
        }
        n++;
        h += 2;
    }
    return 1;
}

static int check_range(const Range *r)
{
    uint32_t h = fnv1a((const uint8_t *)AT(r->va), r->len);
    if (h != r->fnv) {
        lr_log(L"map cap: %hs (exe+%llx, %u bytes) differs from v1.0.3.0 (hash %08x, expected %08x)", r->what,
               (unsigned long long)(r->va - EXE_BASE), r->len, h, r->fnv);
        return 0;
    }
    return 1;
}

static uint32_t read_field(const IPatch *p) { return p->size == 1 ? *(uint8_t *)AT(p->va) : *(uint32_t *)AT(p->va); }

static int write_field(const IPatch *p, uint32_t v)
{
    void *a = (void *)AT(p->va);
    DWORD old;
    if (!VirtualProtect(a, 4, PAGE_EXECUTE_READWRITE, &old)) return 0;
    if (p->size == 1) *(volatile uint8_t *)a = (uint8_t)v; else *(volatile uint32_t *)a = v;
    VirtualProtect(a, 4, old, &old);
    FlushInstructionCache(GetCurrentProcess(), a, 4);
    return 1;
}

static void undo(int hooks_created, int patches_done)
{
    for (int i = 0; i < patches_done; i++) write_field(&ipatches[i], ipatches[i].orig);
    for (int i = 0; i < hooks_created; i++) {
        MH_DisableHook((void *)AT(hooks[i].va));
        MH_RemoveHook((void *)AT(hooks[i].va));
    }
}

// Objects whose layout this module changes must not exist yet: they are created at the start of a game session (after
// the title screen, where this runs). A world object made earlier keeps the vanilla code (see ext_world).
static const char *existing_object(void)
{
    char *gm = *(char **)AT(VA_GAME_MANAGER);
    if (!gm) return NULL;
    if (*(char **)(gm + 0x40)) return "generator manager [gm+0x40]";
    char *g70 = *(char **)(gm + 0x70);
    char *ev = g70 ? *(char **)(g70 + 0x28) : NULL;
    if (ev && *(char **)(ev + 0x18)) return "ESD variable table [[gm+0x70]+0x28]+0x18";
    char *mm = *(char **)(gm + 0x38);
    if (mm && (*(char **)(mm + 0x1a8) || *(char **)(mm + 0x1b0) || *(char **)(mm + 0x1c0) || *(char **)(mm + 0x1f8)))
        return "map manager sub-objects [mm+0x1a8/0x1b0/0x1c0/0x1f8]";
    return NULL;
}

static void install(void)
{
    int want = lr_config.map_cap;
    if (want <= VANILLA_MAPS) {
        lr_log(L"map cap: off (loader.ini map_cap=%d; vanilla limit %d)", want, VANILLA_MAPS);
        return;
    }
    if (want > MAX_ALLOWED) {
        lr_log(L"map cap: map_cap=%d clamped to %d (FUN_14041bab0 keeps two 48-byte per-map stack arrays)", want, MAX_ALLOWED);
        want = MAX_ALLOWED;
    }
    int ok = 1;
    for (size_t i = 0; i < COUNT(ranges); i++) ok &= check_range(&ranges[i]);
    for (size_t i = 0; i < COUNT(csigs); i++) ok &= check_csig(&csigs[i]);
    for (size_t i = 0; i < COUNT(ipatches); i++)
        if (read_field(&ipatches[i]) != ipatches[i].orig) {
            lr_log(L"map cap: %hs at exe+%llx is %x, expected %x", ipatches[i].what, (unsigned long long)(ipatches[i].va - EXE_BASE),
                   read_field(&ipatches[i]), ipatches[i].orig);
            ok = 0;
        }
    if (!ok) {
        lr_log(L"map cap: DISABLED (not the DarkSoulsII.exe v1.0.3.0 the RE was done on, or another mod patched these functions); vanilla 42-map limit");
        return;
    }
    const char *exists = existing_object();
    if (exists) {
        lr_log(L"map cap: DISABLED (%hs already exists; the patches must go in before a game session starts)", exists);
        return;
    }
    cap = want;
    mm_stub_targets();
    MH_STATUS s = MH_Initialize();
    if (s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) {
        lr_log(L"map cap: DISABLED (MH_Initialize %d)", (int)s);
        cap = 0;
        return;
    }
    int created = 0;
    for (; created < (int)COUNT(hooks); created++) {
        s = MH_CreateHook((void *)AT(hooks[created].va), hooks[created].detour, hooks[created].orig);
        if (s != MH_OK) {
            lr_log(L"map cap: DISABLED (hooking %hs at exe+%llx failed: MinHook %d)", hooks[created].what,
                   (unsigned long long)(hooks[created].va - EXE_BASE), (int)s);
            undo(created, 0);
            cap = 0;
            return;
        }
    }
    int done = 0;
    for (; done < (int)COUNT(ipatches); done++)
        if (!write_field(&ipatches[done], patch_value(&ipatches[done]))) {
            lr_log(L"map cap: DISABLED (VirtualProtect on %hs failed: %lu)", ipatches[done].what, GetLastError());
            undo(created, done);
            cap = 0;
            return;
        }
    for (int i = 0; i < created; i++) MH_QueueEnableHook((void *)AT(hooks[i].va));
    if ((s = MH_ApplyQueued()) != MH_OK) {
        lr_log(L"map cap: DISABLED (enabling the hooks failed: MinHook %d)", (int)s);
        undo(created, done);
        cap = 0;
        return;
    }
    active = 1;
    lr_log(L"map cap: on, map_cap=%d (exe base %p; %d function ranges and %d sites match v1.0.3.0; %d hooks, %d immediates patched; side file %ls)",
           cap, (void *)lr_base, (int)COUNT(ranges), (int)COUNT(csigs), created, done, side_dir[0] ? side_dir : L"off");
}

int lr_mapcap_active(void) { return active ? cap : 0; }

void lr_mapcap_report(void)
{
    if (active) lr_log(L"map cap: session summary: %d side-file writes, %d save loads, %d extension records restored", side_writes, side_loads, side_restored);
}

// Called from the keyboard poll (autopilot.c) on the game thread, before a game session starts; installs once.
void lr_mapcap_tick(void)
{
    static int done;
    if (done)
        return;
    done = 1;
    install();
}
