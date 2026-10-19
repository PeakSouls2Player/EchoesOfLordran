// OS-level redirection for loose files in DS2's game folder (title:), e.g. enc_regulation.bnd.dcx.
// DS2's disk device resolves "title:/x" to "<game>\x" *in the caller's string* and later code relies on that,
// so swapping paths at the device level breaks loading. Instead we let the game do everything as normal and
// substitute the file at the Win32 layer: "<game>\x" -> "<pack>\title\x" when the pack has it. Every API that
// can open a file or report its size is covered, so size and contents always come from the same file.
// Only read access is redirected; anything opened for writing goes to the real path.
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

typedef HANDLE(WINAPI *CreateFileWFn)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
typedef HANDLE(WINAPI *CreateFileAFn)(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
typedef DWORD(WINAPI *GetFileAttributesWFn)(LPCWSTR);
typedef BOOL(WINAPI *GetFileAttributesExWFn)(LPCWSTR, GET_FILEEX_INFO_LEVELS, LPVOID);
typedef HANDLE(WINAPI *FindFirstFileWFn)(LPCWSTR, LPWIN32_FIND_DATAW);
typedef HANDLE(WINAPI *FindFirstFileExWFn)(LPCWSTR, FINDEX_INFO_LEVELS, LPVOID, FINDEX_SEARCH_OPS, LPVOID, DWORD);

static CreateFileWFn orig_CreateFileW;
static CreateFileAFn orig_CreateFileA;
static GetFileAttributesWFn orig_GetFileAttributesW;
static GetFileAttributesExWFn orig_GetFileAttributesExW;
static FindFirstFileWFn orig_FindFirstFileW;
static FindFirstFileExWFn orig_FindFirstFileExW;

static wchar_t game_dir[MAX_PATH];    // "<game>\"
static size_t game_dir_len;
static wchar_t title_root[MAX_PATH];  // "<pack>\title\"
static volatile LONG os_redirects, traced;

// Paths containing this (case-insensitive) are traced on every API call, mapped or not.
static const wchar_t *trace_substring = L"regulation";

static int contains_ci(const wchar_t *hay, const wchar_t *needle)
{
    if (!hay)
        return 0;
    size_t n = (size_t)lstrlenW(needle);
    for (; *hay; hay++) {
        size_t i = 0;
        while (i < n && hay[i] && towlower(hay[i]) == towlower(needle[i]))
            i++;
        if (i == n)
            return 1;
    }
    return 0;
}

// "<game>\rel" (any spelling: relative, mixed slashes, ..) -> "<pack>\title\rel" if that file exists in the pack.
static int map_path(LPCWSTR name, wchar_t *out, size_t out_len)
{
    if (!name || !title_root[0] || !game_dir_len)
        return 0;
    wchar_t full[MAX_PATH * 2];
    DWORD n = GetFullPathNameW(name, MAX_PATH * 2, full, NULL);
    if (n == 0 || n >= MAX_PATH * 2)
        return 0;
    if (n <= game_dir_len || CompareStringOrdinal(full, (int)game_dir_len, game_dir, (int)game_dir_len, TRUE) != CSTR_EQUAL)
        return 0;
    const wchar_t *rel = full + game_dir_len;
    size_t root = (size_t)lstrlenW(title_root);
    if (root + (size_t)lstrlenW(rel) + 1 > out_len)
        return 0;
    memcpy(out, title_root, root * sizeof(wchar_t));
    lstrcpyW(out + root, rel);
    DWORD attrs = orig_GetFileAttributesW(out);
    return attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}

static void trace(const wchar_t *api, LPCWSTR name, int mapped, const wchar_t *alt)
{
    if (contains_ci(name, trace_substring) && InterlockedIncrement(&traced) <= 200)
        lr_log(L"TRACE %ls \"%ls\"%ls%ls", api, name, mapped ? L" -> " : L" (not redirected)", mapped ? alt : L"");
}

static HANDLE WINAPI hk_CreateFileW(LPCWSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sa,
                                    DWORD disposition, DWORD flags, HANDLE templ)
{
    wchar_t alt[MAX_PATH * 2];
    int reading = !(access & (GENERIC_WRITE | GENERIC_ALL | FILE_WRITE_DATA | FILE_APPEND_DATA | DELETE)) &&
                  (disposition == OPEN_EXISTING || disposition == OPEN_ALWAYS);
    int mapped = reading && map_path(name, alt, sizeof alt / sizeof alt[0]);
    trace(L"CreateFileW", name, mapped, alt);
    if (mapped) {
        if (InterlockedIncrement(&os_redirects) <= 2000)
            lr_log(L"OS-REDIRECT CreateFileW \"%ls\" -> \"%ls\"", name, alt);
        return orig_CreateFileW(alt, access, share, sa, disposition, flags, templ);
    }
    return orig_CreateFileW(name, access, share, sa, disposition, flags, templ);
}

static HANDLE WINAPI hk_CreateFileA(LPCSTR name, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sa,
                                    DWORD disposition, DWORD flags, HANDLE templ)
{
    wchar_t wide[MAX_PATH * 2], alt[MAX_PATH * 2];
    int reading = !(access & (GENERIC_WRITE | GENERIC_ALL | FILE_WRITE_DATA | FILE_APPEND_DATA | DELETE)) &&
                  (disposition == OPEN_EXISTING || disposition == OPEN_ALWAYS);
    if (name && MultiByteToWideChar(CP_ACP, 0, name, -1, wide, MAX_PATH * 2) > 0) {
        int mapped = reading && map_path(wide, alt, sizeof alt / sizeof alt[0]);
        trace(L"CreateFileA", wide, mapped, alt);
        if (mapped) {
            InterlockedIncrement(&os_redirects);
            lr_log(L"OS-REDIRECT CreateFileA \"%ls\" -> \"%ls\"", wide, alt);
            return orig_CreateFileW(alt, access, share, sa, disposition, flags, templ);
        }
    }
    return orig_CreateFileA(name, access, share, sa, disposition, flags, templ);
}

static DWORD WINAPI hk_GetFileAttributesW(LPCWSTR name)
{
    wchar_t alt[MAX_PATH * 2];
    int mapped = map_path(name, alt, sizeof alt / sizeof alt[0]);
    trace(L"GetFileAttributesW", name, mapped, alt);
    return orig_GetFileAttributesW(mapped ? alt : name);
}

static BOOL WINAPI hk_GetFileAttributesExW(LPCWSTR name, GET_FILEEX_INFO_LEVELS level, LPVOID info)
{
    wchar_t alt[MAX_PATH * 2];
    int mapped = map_path(name, alt, sizeof alt / sizeof alt[0]);
    trace(L"GetFileAttributesExW", name, mapped, alt);
    return orig_GetFileAttributesExW(mapped ? alt : name, level, info);
}

static HANDLE WINAPI hk_FindFirstFileW(LPCWSTR name, LPWIN32_FIND_DATAW data)
{
    wchar_t alt[MAX_PATH * 2];
    // Only exact-file lookups are redirected; wildcard searches enumerate the real folder.
    int mapped = name && !wcschr(name, L'*') && !wcschr(name, L'?') && map_path(name, alt, sizeof alt / sizeof alt[0]);
    trace(L"FindFirstFileW", name, mapped, alt);
    return orig_FindFirstFileW(mapped ? alt : name, data);
}

static HANDLE WINAPI hk_FindFirstFileExW(LPCWSTR name, FINDEX_INFO_LEVELS level, LPVOID data, FINDEX_SEARCH_OPS op,
                                         LPVOID filter, DWORD flags)
{
    wchar_t alt[MAX_PATH * 2];
    int mapped = name && !wcschr(name, L'*') && !wcschr(name, L'?') && map_path(name, alt, sizeof alt / sizeof alt[0]);
    trace(L"FindFirstFileExW", name, mapped, alt);
    return orig_FindFirstFileExW(mapped ? alt : name, level, data, op, filter, flags);
}

static int hook(LPCWSTR module, LPCSTR proc, void *detour, void **original)
{
    MH_STATUS s = MH_CreateHookApi(module, proc, detour, original);
    if (s != MH_OK) {
        lr_log(L"MinHook: hooking %hs failed (%d)", proc, (int)s);
        return 0;
    }
    return 1;
}

void lr_winfs_install(const wchar_t *pack, const wchar_t *game)
{
    if (!pack || !pack[0])
        return;
    lstrcpynW(title_root, pack, MAX_PATH - 8);
    size_t n = (size_t)lstrlenW(title_root);
    if (n && title_root[n - 1] != L'\\')
        lstrcatW(title_root, L"\\");
    lstrcatW(title_root, L"title\\");

    // Canonical form of the game folder, so it compares equal to canonicalised request paths.
    if (!GetFullPathNameW(game, MAX_PATH, game_dir, NULL))
        lstrcpynW(game_dir, game, MAX_PATH);
    game_dir_len = (size_t)lstrlenW(game_dir);

    if (MH_Initialize() != MH_OK) {
        lr_log(L"MinHook: initialise failed; loose-file redirection disabled");
        return;
    }
    // GetFileAttributesW first: map_path calls the original.
    hook(L"kernelbase", "GetFileAttributesW", (void *)hk_GetFileAttributesW, (void **)&orig_GetFileAttributesW);
    hook(L"kernelbase", "GetFileAttributesExW", (void *)hk_GetFileAttributesExW, (void **)&orig_GetFileAttributesExW);
    hook(L"kernelbase", "FindFirstFileW", (void *)hk_FindFirstFileW, (void **)&orig_FindFirstFileW);
    hook(L"kernelbase", "FindFirstFileExW", (void *)hk_FindFirstFileExW, (void **)&orig_FindFirstFileExW);
    hook(L"kernelbase", "CreateFileW", (void *)hk_CreateFileW, (void **)&orig_CreateFileW);
    hook(L"kernelbase", "CreateFileA", (void *)hk_CreateFileA, (void **)&orig_CreateFileA);
    MH_STATUS s = MH_EnableHook(MH_ALL_HOOKS);
    lr_log(L"loose-file redirection: %ls -> %ls (%ls)", game_dir, title_root, s == MH_OK ? L"enabled" : L"FAILED");
}

void lr_winfs_report(void)
{
    lr_log(L"loose-file redirects: %ld", os_redirects);
}
