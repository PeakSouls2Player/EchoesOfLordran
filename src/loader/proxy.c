// dinput8.dll proxy: DS2 imports dinput8.dll, and Windows looks in the game folder first, so this
// DLL loads before any game code runs. Every export forwards to the real System32 dinput8.dll.
#include "lordran.h"

static HMODULE real_dinput8;


static FARPROC real_proc(const char *name)
{
    if (!real_dinput8) {
        wchar_t path[MAX_PATH];
        UINT n = GetSystemDirectoryW(path, MAX_PATH);
        if (n == 0 || n > MAX_PATH - 14)
            return NULL;
        lstrcatW(path, L"\\dinput8.dll");
        real_dinput8 = LoadLibraryW(path);
    }
    return real_dinput8 ? GetProcAddress(real_dinput8, name) : NULL;
}

typedef HRESULT(WINAPI *DirectInput8CreateFn)(HINSTANCE, DWORD, const void *, void **, void *);
typedef HRESULT(WINAPI *NoArgsFn)(void);
typedef HRESULT(WINAPI *GetClassObjectFn)(const void *, const void *, void **);
typedef const void *(WINAPI *GetDataFormatFn)(void);

HRESULT WINAPI DirectInput8Create(HINSTANCE inst, DWORD version, const void *riid, void **out, void *outer)
{
    // The game creates its input devices after every static import (xinput1_3.dll too) is loaded.
    if (lr_config.autopilot)
        lr_autopilot_hook();
    DirectInput8CreateFn f = (DirectInput8CreateFn)real_proc("DirectInput8Create");
    HRESULT r = f ? f(inst, version, riid, out, outer) : E_FAIL;
    // The keyboard hooks are always installed: the keyboard poll is the per-frame game-thread hook for entry.c; they only
    // touch input when autopilot=1.
    if (r >= 0 && out)
        lr_autopilot_dinput(*out);
    return r;
}

HRESULT WINAPI DllCanUnloadNow(void)
{
    NoArgsFn f = (NoArgsFn)real_proc("DllCanUnloadNow");
    return f ? f() : S_FALSE;
}

HRESULT WINAPI DllGetClassObject(const void *clsid, const void *riid, void **out)
{
    GetClassObjectFn f = (GetClassObjectFn)real_proc("DllGetClassObject");
    return f ? f(clsid, riid, out) : E_FAIL;
}

HRESULT WINAPI DllRegisterServer(void)
{
    NoArgsFn f = (NoArgsFn)real_proc("DllRegisterServer");
    return f ? f() : E_FAIL;
}

HRESULT WINAPI DllUnregisterServer(void)
{
    NoArgsFn f = (NoArgsFn)real_proc("DllUnregisterServer");
    return f ? f() : E_FAIL;
}

const void *WINAPI GetdfDIJoystick(void)
{
    GetDataFormatFn f = (GetDataFormatFn)real_proc("GetdfDIJoystick");
    return f ? f() : NULL;
}
