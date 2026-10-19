// Function tracing for reverse engineering (autopilot command "trace RVA [MAX]"): hooks exe+RVA and logs the first MAX
// calls with the four register arguments (rcx, rdx, r8, r9) and the caller's address, then runs the original.
// The stubs are assembly so that stack arguments (5th and later) and xmm0-3 reach the original untouched: they save the
// argument registers, call trace_hit, restore everything and tail-jump to MinHook's trampoline.
#include "lordran.h"
#include "../../tools/minhook/include/MinHook.h"

#define TRACE_SLOTS 8

void *trace_orig[TRACE_SLOTS];
static uintptr_t trace_rva[TRACE_SLOTS];
static volatile LONG trace_calls[TRACE_SLOTS];
static LONG trace_max[TRACE_SLOTS];
static int trace_used;

void trace_hit(int slot, uintptr_t a, uintptr_t b, uintptr_t c, uintptr_t d, uintptr_t ret)
{
    LONG n = InterlockedIncrement(&trace_calls[slot]);
    if (n > trace_max[slot])
        return;
    uintptr_t from = ret - lr_base;
    lr_log(L"TRACE %llX #%ld (%p, %p, %p, %p) from exe+%llX%ls", (unsigned long long)trace_rva[slot], n, (void *)a,
           (void *)b, (void *)c, (void *)d, (unsigned long long)from, n == trace_max[slot] ? L" (last logged)" : L"");
}

__asm__(
    ".intel_syntax noprefix\n"
    ".macro TRACE_STUB n\n"
    ".globl trace_stub\\n\n"
    "trace_stub\\n:\n"
    "    push rcx\n"
    "    push rdx\n"
    "    push r8\n"
    "    push r9\n"
    "    sub rsp, 0x78\n"                 // 0x20 shadow + 2 stack args + 4 saved xmm; leaves rsp 16-aligned
    "    movdqu [rsp+0x30], xmm0\n"
    "    movdqu [rsp+0x40], xmm1\n"
    "    movdqu [rsp+0x50], xmm2\n"
    "    movdqu [rsp+0x60], xmm3\n"
    "    mov rax, [rsp+0x98]\n"           // return address
    "    mov [rsp+0x28], rax\n"
    "    mov rax, [rsp+0x78]\n"           // original r9
    "    mov [rsp+0x20], rax\n"
    "    mov r9, [rsp+0x80]\n"            // original r8
    "    mov r8, [rsp+0x88]\n"            // original rdx
    "    mov rdx, [rsp+0x90]\n"           // original rcx
    "    mov ecx, \\n\n"
    "    call trace_hit\n"
    "    movdqu xmm0, [rsp+0x30]\n"
    "    movdqu xmm1, [rsp+0x40]\n"
    "    movdqu xmm2, [rsp+0x50]\n"
    "    movdqu xmm3, [rsp+0x60]\n"
    "    add rsp, 0x78\n"
    "    pop r9\n"
    "    pop r8\n"
    "    pop rdx\n"
    "    pop rcx\n"
    "    jmp qword ptr [rip + trace_orig + 8*\\n]\n"
    ".endm\n"
    "TRACE_STUB 0\n"
    "TRACE_STUB 1\n"
    "TRACE_STUB 2\n"
    "TRACE_STUB 3\n"
    "TRACE_STUB 4\n"
    "TRACE_STUB 5\n"
    "TRACE_STUB 6\n"
    "TRACE_STUB 7\n"
    ".att_syntax prefix\n");

void trace_stub0(void), trace_stub1(void), trace_stub2(void), trace_stub3(void);
void trace_stub4(void), trace_stub5(void), trace_stub6(void), trace_stub7(void);
static void (*const trace_stubs[TRACE_SLOTS])(void) = {
    trace_stub0, trace_stub1, trace_stub2, trace_stub3, trace_stub4, trace_stub5, trace_stub6, trace_stub7,
};

// Installs a trace on exe+rva. Returns the slot, or -1.
int lr_trace(uintptr_t rva, int max)
{
    if (trace_used >= TRACE_SLOTS) {
        lr_log(L"TRACE: all %d slots in use", TRACE_SLOTS);
        return -1;
    }
    MH_STATUS s = MH_Initialize();
    if (s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED)
        return -1;
    int slot = trace_used;
    trace_rva[slot] = rva;
    trace_max[slot] = max > 0 ? max : 30;
    void *target = (void *)(lr_base + rva);
    if ((s = MH_CreateHook(target, (void *)trace_stubs[slot], &trace_orig[slot])) != MH_OK ||
        (s = MH_EnableHook(target)) != MH_OK) {
        lr_log(L"TRACE %llX: hook failed (%d)", (unsigned long long)rva, (int)s);
        return -1;
    }
    trace_used++;
    lr_log(L"TRACE %llX: slot %d, logging the first %ld calls", (unsigned long long)rva, slot, trace_max[slot]);
    return slot;
}
