// Regulation load check. Wraps the regulation resource's load slot (FUN_1402df8f0) and logs what the game made of the
// file: DS2 CWC-authenticates enc_regulation.bnd.dcx and silently ignores it when the tag doesn't match, which later
// shows up as a null param row and a crash at DarkSoulsII.exe+0x265b3e. A "BND4" line here means the game accepted it.
#include "lordran.h"

typedef uint8_t (*RegulationLoadFn)(void *self, const uint8_t *data, uint64_t size);

static RegulationLoadFn orig_load;
static wchar_t dump_path[MAX_PATH];

static uint8_t load_hook(void *self, const uint8_t *data, uint64_t size)
{
    uint8_t result = orig_load(self, data, size);

    uint32_t decoded_size = *(uint32_t *)((uint8_t *)self + 0xd4);
    const uint8_t *bnd = *(const uint8_t *const *)((uint8_t *)self + 0xd8);
    if (bnd && decoded_size >= 0x40 && memcmp(bnd, "BND4", 4) == 0) {
        lr_log(L"regulation: file %llu bytes -> BND4 %u bytes, %u params (accepted)", (unsigned long long)size, decoded_size,
               *(const uint32_t *)(bnd + 0x0c));
        if (lr_config.dump_regulation) {
            HANDLE f = CreateFileW(dump_path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
            DWORD written = 0;
            if (f != INVALID_HANDLE_VALUE) {
                WriteFile(f, bnd, decoded_size, &written, NULL);
                CloseHandle(f);
            }
            lr_log(L"regulation: dumped %lu bytes to %ls", written, dump_path);
        }
    } else {
        lr_log(L"regulation: file %llu bytes was NOT accepted (decoded %p, %u bytes). Bad CWC tag or DCX?",
               (unsigned long long)size, (const void *)bnd, decoded_size);
    }
    return result;
}

void lr_regulation_install(const wchar_t *lordran_dir)
{
    lstrcpynW(dump_path, lordran_dir, MAX_PATH - 32);
    lstrcatW(dump_path, L"regulation-loaded.bnd");
    lr_patch_vtable(L"regulation resource load", RVA_REGULATION_VFTABLE, VT_SLOT_REGULATION_LOAD, RVA_REGULATION_LOAD,
                    (void *)load_hook, (void **)&orig_load);
}
