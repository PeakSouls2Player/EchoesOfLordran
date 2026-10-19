#include "lordran.h"

#include <stdarg.h>
#include <stdio.h>

static HANDLE log_file = INVALID_HANDLE_VALUE;
static CRITICAL_SECTION log_lock;

void lr_log_open(const wchar_t *path)
{
    InitializeCriticalSection(&log_lock);
    log_file = CreateFileW(path, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
}

void lr_log(const wchar_t *fmt, ...)
{
    if (log_file == INVALID_HANDLE_VALUE)
        return;

    wchar_t wide[2048];
    SYSTEMTIME t;
    GetLocalTime(&t);
    int n = swprintf(wide, 2048, L"%02u:%02u:%02u.%03u [%5lu] ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, GetCurrentThreadId());
    va_list args;
    va_start(args, fmt);
    int m = vswprintf(wide + n, 2048 - n - 2, fmt, args);
    va_end(args);
    if (m < 0)
        m = 0;
    n += m;
    wide[n++] = L'\r';
    wide[n++] = L'\n';

    char utf8[6144];
    int bytes = WideCharToMultiByte(CP_UTF8, 0, wide, n, utf8, sizeof utf8, NULL, NULL);
    EnterCriticalSection(&log_lock);
    DWORD written;
    WriteFile(log_file, utf8, (DWORD)bytes, &written, NULL);
    LeaveCriticalSection(&log_lock);
}

void lr_log_close(void)
{
    if (log_file != INVALID_HANDLE_VALUE) {
        CloseHandle(log_file);
        log_file = INVALID_HANDLE_VALUE;
    }
}
