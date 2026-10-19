// Virtual file system hooks.
// Every Open on DS2's encrypted-archive device (gamedata:, *lq:) is checked against the content pack
// (<pack>\<device>\<path>). If the pack has the file, the game still gets its own archive file operator
// (DLEBL::DLEncryptedBinderLightFileOperator, from the original Open), but with a copy of that class's vtable whose
// Read / Clone / destructor serve the pack file instead of the encrypted .bdt. To the rest of the game it is an
// ordinary archive file. (Loader v1-v2.3 handed back a MicrosoftDiskFileOperator instead; every file opened, but the
// boot sequence never saw those loads finish and the title screen stayed black.)
// Disk Opens are only observed: the disk device rewrites the caller's path in place, so loose files (title:) are
// redirected at the OS level instead (winfs.c).
//
// Archive crypto (session 11, tae12): the archive operator's Read returns the RAW .bdt bytes. Decryption and the SHA
// check happen one layer up, in DLIO's FileLoader (FileLoader.cpp read loop ~0x1408fe4xx): every transfer task asks
// the DLEncryptedBinderLightUtility (vftable 0x141292a80, slot 1 = 0x140ff0ac0, registered at DLIO fs+0x1410 by
// FUN_140b5ad90) for a DLEncryptedBinderLightObject (vftable 0x141293368) at task init (FUN_1408f8270, task+0x150).
// Its init (slot 1 = FUN_140ff1860(obj, path, device)) looks the PATH up in the device's BHD (FUN_140fef650) and keeps
// the entry's AES key/ranges (+0x18) and SHA-256 (+0x20). The loader then AES-decrypts in place every chunk it read
// (slots 8/7 = FUN_140ff1980/FUN_140ff1730), hashes it (slot 4) and at the end compares the SHA (slots 3/5/6,
// FUN_1408deb40 / FUN_1408e0750: mismatch = load error 0xffffff0b). That is keyed by path, not by operator, so a pack
// file that REPLACES an encrypted archive file (all TimeAct/*.tae and the per-map Event/Generator/... params are
// AES files) got "decrypted" a second time: the TAE reached TimeActResourceObject garbled (or failed the SHA check)
// and the character ran without TimeAct events. New pack-only files are not in the BHD, init fails, no object is
// made, and the bytes pass untouched. ebl_obj_init_hook makes pack-served files take that same path.
#include "lordran.h"

#define DETAILED_LOG_LIMIT 4000

// DLEncryptedBinderLightFileOperator (0x70 bytes): +0x10 last error, +0x28 device, +0x60 data offset in the .bdt
// (-1 = file not in the archive), +0x68 size, +0x6c position. Apart from Read, its methods only compare +0x60 with -1,
// so for pack files +0x60 holds our PackFile pointer instead.
#define RVA_EBL_OP_VFTABLE 0x01293168u
#define EBL_OP_SLOTS       40
#define EBL_OP_DTOR        0   // scalar deleting destructor (this, flags)
#define EBL_OP_CLONE       7   // returns a copy made with the original vtable
#define EBL_OP_READ        25  // int Read(this, void *buffer, uint64 length)
static const uint32_t ebl_op_expected[][2] = {
    { EBL_OP_DTOR, 0x00ff0f30u }, { EBL_OP_CLONE, 0x00ff1480u }, { EBL_OP_READ, 0x00ff1320u } };

#define OP_ERROR(op)  (*(int32_t *)((uint8_t *)(op) + 0x10))
#define OP_SOURCE(op) (*(uintptr_t *)((uint8_t *)(op) + 0x60))
#define OP_SIZE(op)   (*(uint32_t *)((uint8_t *)(op) + 0x68))
#define OP_POS(op)    (*(uint32_t *)((uint8_t *)(op) + 0x6c))
#define NOT_IN_ARCHIVE ((uintptr_t)-1)

typedef struct PackFile {
    HANDLE file;
} PackFile;

typedef void *(*OpDtorFn)(void *op, uint32_t flags);
typedef void *(*OpCloneFn)(void *op);

static DeviceOpenFn orig_ebl_open;
static DeviceOpenFn orig_disk_open;
static OpDtorFn orig_op_dtor;
static OpCloneFn orig_op_clone;
static uintptr_t pack_op_vtable[1 + EBL_OP_SLOTS]; // [0] = RTTI locator (so dynamic_cast still works), then the slots
static int pack_ops_ready;
static volatile LONG ebl_calls, disk_calls, logged, redirected, redirect_failed;
static wchar_t pack_root[MAX_PATH];   // ends with a backslash

// DLEBL::DLEncryptedBinderLightObject (0xd8 bytes): +0x08 device, +0x10 BHD entry, +0x18 AES key (NULL = not
// encrypted), +0x20 SHA-256 (NULL = no check). Slot 1 = init(obj, const wchar_t *path, device) -> bool.
#define RVA_EBL_OBJ_VFTABLE 0x01293368u
#define EBL_OBJ_INIT        1
#define RVA_EBL_OBJ_INIT    0x00ff1860u
// TimeActResourceObject: slot 16 = FUN_140b2fa30(res, data, int size) -> bool, parses the TAE the FileLoader read.
// The resource path is a DLString at +0x80 (FileResourceObject ctor FUN_140af6a10).
#define RVA_TAE_RES_VFTABLE 0x011d93a8u
#define TAE_RES_PARSE       16
#define RVA_TAE_RES_PARSE   0x00b2fa30u
#define CRYPTO_LOG_LIMIT    400
#define TAE_LOG_LIMIT       600

typedef uint8_t (*EblObjInitFn)(void *obj, const wchar_t *path, void *device);
typedef uint8_t (*TaeParseFn)(void *res, const uint8_t *data, int32_t size);
static EblObjInitFn orig_ebl_obj_init;
static TaeParseFn orig_tae_parse;
static volatile LONG crypto_dropped, crypto_dropped_active, crypto_logged, tae_ok, tae_failed, tae_pack, tae_logged;

// Archive device object -> its mount name ("gamedata", "partslq", ...), learnt from the paths its Opens receive.
typedef struct DeviceName {
    void *device;
    wchar_t name[33];
} DeviceName;
static DeviceName device_names[16];
static int device_name_count;
static SRWLOCK device_names_lock = SRWLOCK_INIT;
static const wchar_t *const ebl_mounts[] = { L"gamedata", L"chrlq", L"partslq", L"objlq", L"maplq" };

static void log_open(const wchar_t *kind, void *device, DLString *path, void *a3, void *a4, void *alloc, uint64_t flag, void *result)
{
    if (!lr_config.log_opens || InterlockedIncrement(&logged) > DETAILED_LOG_LIMIT)
        return;
    uintptr_t result_vt = result ? *(uintptr_t *)result : 0;
    lr_log(L"%-7ls dev=%p a3=%p a4=%p alloc=%p flag=%llx -> %p (vt rva %llx) len=%llu \"%ls\"",
           kind, device, a3, a4, alloc, (unsigned long long)(flag & 0xff), result,
           (unsigned long long)(result_vt ? result_vt - lr_base : 0), (unsigned long long)path->size, dl_str(path));
}

// "device:/some/path" -> "<pack>\device\some\path" if that file exists in the pack.
static int pack_path_for(const wchar_t *virt, wchar_t *out, size_t out_len)
{
    if (!pack_root[0])
        return 0;
    const wchar_t *colon = wcschr(virt, L':');
    if (!colon || colon == virt || colon - virt > 32)
        return 0;
    // A drive letter ("C:\...") is a real path, not a device.
    if (colon - virt == 1)
        return 0;

    size_t n = (size_t)lstrlenW(pack_root);
    size_t dev_len = (size_t)(colon - virt);
    const wchar_t *rest = colon + 1;
    while (*rest == L'/' || *rest == L'\\')
        rest++;
    if (n + dev_len + 1 + (size_t)lstrlenW(rest) + 1 > out_len)
        return 0;

    memcpy(out, pack_root, n * sizeof(wchar_t));
    for (size_t i = 0; i < dev_len; i++)
        out[n++] = virt[i];
    out[n++] = L'\\';
    for (; *rest; rest++)
        out[n++] = *rest == L'/' ? L'\\' : *rest;
    out[n] = 0;

    DWORD attrs = GetFileAttributesW(out);
    return attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}

// Takes ownership of the handle (closes it on failure).
static PackFile *pack_file_from(HANDLE file)
{
    PackFile *pf = HeapAlloc(GetProcessHeap(), 0, sizeof *pf);
    if (pf)
        pf->file = file;
    else
        CloseHandle(file);
    return pf;
}

// Same contract as the archive's Read (FUN_140ff1320): -1 at end of file, 0 for an empty request, otherwise the
// number of bytes read; the position advances and the error field is set.
static int32_t pack_op_read(void *op, void *buffer, uint64_t length)
{
    PackFile *pf = (PackFile *)OP_SOURCE(op);
    uint32_t pos = OP_POS(op), size = OP_SIZE(op);
    if (pos >= size) {
        OP_ERROR(op) = 0;
        return -1;
    }
    if (length == 0) {
        OP_ERROR(op) = 0;
        return 0;
    }
    DWORD want = (DWORD)(length < size - pos ? length : size - pos), got = 0;
    OVERLAPPED at = { 0 };
    at.Offset = pos; // positional read: clones share nothing, and no file pointer to race on
    if (!ReadFile(pf->file, buffer, want, &got, &at)) {
        OP_ERROR(op) = -5;
        return -1;
    }
    OP_POS(op) = pos + got;
    OP_ERROR(op) = 0;
    return (int32_t)got;
}

static void *pack_op_clone(void *op)
{
    void *copy = orig_op_clone(op);
    if (!copy)
        return NULL;
    HANDLE dup = NULL;
    PackFile *pf = (PackFile *)OP_SOURCE(op);
    if (DuplicateHandle(GetCurrentProcess(), pf->file, GetCurrentProcess(), &dup, 0, FALSE, DUPLICATE_SAME_ACCESS) &&
        (pf = pack_file_from(dup)) != NULL) {
        OP_SOURCE(copy) = (uintptr_t)pf;
        *(uintptr_t **)copy = pack_op_vtable + 1;
    } else {
        OP_SOURCE(copy) = NOT_IN_ARCHIVE; // reads as a missing file rather than garbage from the .bdt
    }
    return copy;
}

static void *pack_op_dtor(void *op, uint32_t flags)
{
    PackFile *pf = (PackFile *)OP_SOURCE(op);
    if (pf && (uintptr_t)pf != NOT_IN_ARCHIVE) {
        CloseHandle(pf->file);
        HeapFree(GetProcessHeap(), 0, pf);
    }
    OP_SOURCE(op) = NOT_IN_ARCHIVE;
    return orig_op_dtor(op, flags);
}

// Builds our copy of the archive operator vtable after checking the slots we replace are where the decompile says.
static int build_pack_op_vtable(void)
{
    const uintptr_t *vt = (const uintptr_t *)(lr_base + RVA_EBL_OP_VFTABLE);
    for (size_t i = 0; i < sizeof ebl_op_expected / sizeof ebl_op_expected[0]; i++) {
        if (vt[ebl_op_expected[i][0]] != lr_base + ebl_op_expected[i][1]) {
            lr_log(L"archive file operator slot %u holds %llx, expected %x; archive redirection disabled (different game build?)",
                   ebl_op_expected[i][0], (unsigned long long)(vt[ebl_op_expected[i][0]] - lr_base), ebl_op_expected[i][1]);
            return 0;
        }
    }
    memcpy(pack_op_vtable, vt - 1, sizeof pack_op_vtable);
    orig_op_dtor = (OpDtorFn)vt[EBL_OP_DTOR];
    orig_op_clone = (OpCloneFn)vt[EBL_OP_CLONE];
    pack_op_vtable[1 + EBL_OP_DTOR] = (uintptr_t)pack_op_dtor;
    pack_op_vtable[1 + EBL_OP_CLONE] = (uintptr_t)pack_op_clone;
    pack_op_vtable[1 + EBL_OP_READ] = (uintptr_t)pack_op_read;
    return 1;
}

// Records "gamedata" for the device an Open of "gamedata:/..." went to (first sighting per device).
static void remember_device(void *device, const wchar_t *path)
{
    const wchar_t *colon = wcschr(path, L':');
    if (!device || !colon || colon - path < 2 || colon - path > 32)
        return;
    AcquireSRWLockShared(&device_names_lock);
    int known = 0;
    for (int i = 0; i < device_name_count && !known; i++)
        known = device_names[i].device == device;
    ReleaseSRWLockShared(&device_names_lock);
    if (known)
        return;
    AcquireSRWLockExclusive(&device_names_lock);
    for (int i = 0; i < device_name_count; i++)
        if (device_names[i].device == device)
            known = 1;
    if (!known && device_name_count < (int)(sizeof device_names / sizeof device_names[0])) {
        DeviceName *d = &device_names[device_name_count];
        d->device = device;
        lstrcpynW(d->name, path, (int)(colon - path) + 1);
        device_name_count++;
        lr_log(L"archive device %p = \"%ls\"", device, d->name);
    }
    ReleaseSRWLockExclusive(&device_names_lock);
}

static int device_name_of(void *device, wchar_t *out, size_t out_len)
{
    int found = 0;
    AcquireSRWLockShared(&device_names_lock);
    for (int i = 0; i < device_name_count; i++) {
        if (device_names[i].device == device) {
            lstrcpynW(out, device_names[i].name, (int)out_len);
            found = 1;
            break;
        }
    }
    ReleaseSRWLockShared(&device_names_lock);
    return found;
}

// "<mount>:" + the part of `path` from its colon on, then pack_path_for.
static int pack_path_on(const wchar_t *mount, const wchar_t *colon, wchar_t *out, size_t out_len)
{
    wchar_t virt[MAX_PATH * 2];
    size_t m = (size_t)lstrlenW(mount), r = (size_t)lstrlenW(colon);
    if (m + r + 1 > sizeof virt / sizeof virt[0])
        return 0;
    memcpy(virt, mount, m * sizeof(wchar_t));
    memcpy(virt + m, colon, (r + 1) * sizeof(wchar_t));
    return pack_path_for(virt, out, out_len);
}

// Will an Open of `path` on `device` be served from the pack? The device's mount name comes from the Opens seen so
// far; before the first Open on a device, the path's own prefix and then the archive mount names are tried.
static int pack_serves(void *device, const wchar_t *path, wchar_t *out, size_t out_len)
{
    const wchar_t *colon = path ? wcschr(path, L':') : NULL;
    if (!colon || !pack_root[0])
        return 0;
    wchar_t mount[33];
    if (device && device_name_of(device, mount, sizeof mount / sizeof mount[0]))
        return pack_path_on(mount, colon, out, out_len);
    if (pack_path_for(path, out, out_len))
        return 1;
    for (size_t i = 0; i < sizeof ebl_mounts / sizeof ebl_mounts[0]; i++)
        if (pack_path_on(ebl_mounts[i], colon, out, out_len))
            return 1;
    return 0;
}

// DLEncryptedBinderLightObject init. For a file the pack serves, report "not in this archive" so the factory
// (FUN_140ff0ac0) frees the object and the transfer task runs without decryption / SHA check, exactly like a new
// pack-only file. The BHD's key and hash describe the .bdt bytes, which the game never reads for this file.
static uint8_t ebl_obj_init_hook(void *obj, const wchar_t *path, void *device)
{
    uint8_t ok = orig_ebl_obj_init(obj, path, device);
    if (!ok || !lr_config.archive_redirect || !lr_config.archive_crypto_fix || !pack_ops_ready)
        return ok;
    wchar_t real[MAX_PATH * 2];
    if (!pack_serves(device, path, real, sizeof real / sizeof real[0]))
        return ok;
    int aes = *(void **)((uint8_t *)obj + 0x18) != NULL, sha = *(void **)((uint8_t *)obj + 0x20) != NULL;
    InterlockedIncrement(&crypto_dropped);
    if (aes || sha)
        InterlockedIncrement(&crypto_dropped_active);
    if (InterlockedIncrement(&crypto_logged) <= CRYPTO_LOG_LIMIT)
        lr_log(L"ARCHIVE-CRYPTO-OFF \"%ls\": archive entry has AES=%ls SHA=%ls; pack file \"%ls\" is read as-is (no decrypt, no hash check)",
               path, aes ? L"yes" : L"no", sha ? L"yes" : L"no", real);
    return 0;
}

// Logs what TimeActResourceObject actually got for TAEs the pack serves (and any TAE that fails to parse):
// size, the first 4 bytes (a good TAE starts with "TAE ") and the parser's verdict.
static uint8_t tae_parse_hook(void *res, const uint8_t *data, int32_t size)
{
    uint8_t ok = orig_tae_parse(res, data, size);
    InterlockedIncrement(ok ? &tae_ok : &tae_failed);
    const DLString *name = (const DLString *)((const uint8_t *)res + 0x80);
    const wchar_t *path = name->size ? dl_str(name) : L"";
    wchar_t real[MAX_PATH * 2];
    int from_pack = path[0] && pack_serves(NULL, path, real, sizeof real / sizeof real[0]);
    if (from_pack)
        InterlockedIncrement(&tae_pack);
    if ((from_pack || !ok) && InterlockedIncrement(&tae_logged) <= TAE_LOG_LIMIT) {
        wchar_t magic[5] = L"----";
        for (int i = 0; data && size >= 4 && i < 4; i++)
            magic[i] = data[i] >= 0x20 && data[i] < 0x7f ? (wchar_t)data[i] : L'.';
        lr_log(L"TAE-CHECK \"%ls\" %d bytes magic=\"%ls\" parse=%ls%ls", path, size, magic,
               ok ? L"OK" : L"FAILED", from_pack ? L" (pack file)" : L" (archive file)");
    }
    return ok;
}

// Lets the archive device build its own operator, then points it at the pack file.
static void *try_redirect(void *device, DLString *path, void *a3, void *a4, void *alloc, uint64_t flag)
{
    wchar_t real[MAX_PATH * 2];
    if (!lr_config.archive_redirect || !pack_ops_ready || !pack_path_for(dl_str(path), real, sizeof real / sizeof real[0]))
        return NULL;

    HANDLE file = CreateFileW(real, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    LARGE_INTEGER size;
    if (file == INVALID_HANDLE_VALUE || !GetFileSizeEx(file, &size) || size.QuadPart > 0x7fffffff) {
        if (file != INVALID_HANDLE_VALUE)
            CloseHandle(file);
        InterlockedIncrement(&redirect_failed);
        lr_log(L"REDIRECT-FAILED archive \"%ls\" -> \"%ls\" (can't open or too large; using original)", dl_str(path), real);
        return NULL;
    }
    void *op = orig_ebl_open(device, path, a3, a4, alloc, flag);
    if (!op) {
        CloseHandle(file);
        InterlockedIncrement(&redirect_failed);
        return NULL;
    }
    PackFile *pf = pack_file_from(file);
    if (!pf) {
        InterlockedIncrement(&redirect_failed);
        return op;
    }
    int in_archive = OP_SOURCE(op) != NOT_IN_ARCHIVE;
    OP_SOURCE(op) = (uintptr_t)pf;
    OP_SIZE(op) = (uint32_t)size.QuadPart;
    OP_POS(op) = 0;
    OP_ERROR(op) = 0;
    *(uintptr_t **)op = pack_op_vtable + 1;
    InterlockedIncrement(&redirected);
    lr_log(L"REDIRECT archive \"%ls\" -> \"%ls\" (%lld bytes, %ls)", dl_str(path), real, size.QuadPart,
           in_archive ? L"replaces an archive file" : L"new file");
    return op;
}

static void *ebl_open_hook(void *device, DLString *path, void *a3, void *a4, void *alloc, uint64_t flag)
{
    InterlockedIncrement(&ebl_calls);
    remember_device(device, dl_str(path));
    void *result = try_redirect(device, path, a3, a4, alloc, flag);
    if (!result)
        result = orig_ebl_open(device, path, a3, a4, alloc, flag);
    log_open(L"archive", device, path, a3, a4, alloc, flag, result);
    return result;
}

static void *disk_open_hook(void *device, DLString *path, void *a3, void *a4, void *alloc, uint64_t flag)
{
    InterlockedIncrement(&disk_calls);
    void *result = orig_disk_open(device, path, a3, a4, alloc, flag);
    log_open(L"disk", device, path, a3, a4, alloc, flag, result);
    return result;
}

// Swaps one vtable slot after checking it still points where the decompile says it does,
// so a different game build fails safe instead of crashing. Vtables live in .rdata, which SteamStub doesn't encrypt,
// so this works from DllMain.
int lr_patch_vtable(const wchar_t *name, uint32_t vt_rva, int index, uint32_t expected_rva, void *hook, void **orig)
{
    uintptr_t *slot = (uintptr_t *)(lr_base + vt_rva) + index;
    if (*slot != lr_base + expected_rva) {
        lr_log(L"%ls: vtable slot holds %llx, expected %llx; not hooking (different game build?)",
               name, (unsigned long long)(*slot - lr_base), (unsigned long long)expected_rva);
        return 0;
    }
    DWORD old;
    if (!VirtualProtect(slot, sizeof *slot, PAGE_READWRITE, &old)) {
        lr_log(L"%ls: VirtualProtect failed (%lu)", name, GetLastError());
        return 0;
    }
    *orig = (void *)*slot;
    *slot = (uintptr_t)hook;
    VirtualProtect(slot, sizeof *slot, old, &old);
    lr_log(L"%ls: hooked vtable rva %x slot %d", name, vt_rva, index);
    return 1;
}

void lr_vfs_install(const wchar_t *pack)
{
    if (pack && pack[0]) {
        lstrcpynW(pack_root, pack, MAX_PATH - 2);
        size_t n = (size_t)lstrlenW(pack_root);
        if (pack_root[n - 1] != L'\\')
            lstrcatW(pack_root, L"\\");
        DWORD attrs = GetFileAttributesW(pack_root);
        lr_log(L"content pack: %ls (%ls)", pack_root, attrs != INVALID_FILE_ATTRIBUTES ? L"found" : L"MISSING - nothing will be redirected");
    } else {
        lr_log(L"no content pack configured; observing only");
    }
    pack_ops_ready = build_pack_op_vtable();
    lr_patch_vtable(L"DLEncryptedBinderLightFileDevice Open", RVA_EBL_VFTABLE, VT_SLOT_OPEN, RVA_EBL_OPEN,
                    (void *)ebl_open_hook, (void **)&orig_ebl_open);
    lr_patch_vtable(L"MicrosoftDiskFileDevice Open", RVA_DISK_VFTABLE, VT_SLOT_OPEN, RVA_DISK_OPEN,
                    (void *)disk_open_hook, (void **)&orig_disk_open);
    lr_patch_vtable(L"DLEncryptedBinderLightObject init (archive AES/SHA for pack files)", RVA_EBL_OBJ_VFTABLE,
                    EBL_OBJ_INIT, RVA_EBL_OBJ_INIT, (void *)ebl_obj_init_hook, (void **)&orig_ebl_obj_init);
    lr_patch_vtable(L"TimeActResourceObject parse (TAE-CHECK log)", RVA_TAE_RES_VFTABLE, TAE_RES_PARSE,
                    RVA_TAE_RES_PARSE, (void *)tae_parse_hook, (void **)&orig_tae_parse);
    lr_log(L"archive crypto fix: %ls", lr_config.archive_crypto_fix ? L"ON (pack files skip the archive's AES/SHA step)"
                                                                     : L"OFF (archive_crypto_fix=0: old behaviour, pack files replacing AES archive files get garbled)");
}

void lr_vfs_report(void)
{
    lr_log(L"totals: archive opens=%ld disk opens=%ld redirected=%ld redirect failures=%ld",
           ebl_calls, disk_calls, redirected, redirect_failed);
    lr_log(L"totals: archive crypto dropped for pack files=%ld (with AES/SHA=%ld); TAE parses ok=%ld failed=%ld (pack-served=%ld)",
           crypto_dropped, crypto_dropped_active, tae_ok, tae_failed, tae_pack);
}
