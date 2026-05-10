// Starter template for hooking an instruction by module-relative offset
// and logging the general-purpose register state on entry. Copy this
// file, change MODULE_OFFSET, and run:
//
//     frida -n nba2k16.exe -l <your-copy>.js
//
// Hot-reload: edit and save while attached; Frida reapplies automatically.

'use strict';

// Module-relative offset of the instruction to hook. Example: 0x4F56BF is
// our existing AOB hook site (mov [rdx+0x84], ax in the per-position
// physical-attribute setter). Replace with the offset you're investigating.
const MODULE_OFFSET = 0x4F56BF;

const mod = Process.findModuleByName('nba2k16.exe');
if (!mod) {
    throw new Error('nba2k16.exe is not loaded in this process');
}

const target = mod.base.add(MODULE_OFFSET);
console.log(`[+] Hooking nba2k16.exe+0x${MODULE_OFFSET.toString(16)} at ${target}`);

// Throttle so we don't drown a 60Hz hot path in log spam.
let frame = 0;
const LOG_EVERY = 60; // log once per second at 60 Hz

Interceptor.attach(target, {
    onEnter(args) {
        if (frame++ % LOG_EVERY !== 0) return;
        const ctx = this.context;
        send({
            site: `+0x${MODULE_OFFSET.toString(16)}`,
            frame,
            // General-purpose regs that tend to hold the interesting pointers:
            rax: ctx.rax.toString(),
            rbx: ctx.rbx.toString(),
            rcx: ctx.rcx.toString(),
            rdx: ctx.rdx.toString(),
            rdi: ctx.rdi.toString(), // active player ptr at our existing hook
            rsi: ctx.rsi.toString(),
            r8:  ctx.r8.toString(),
            r9:  ctx.r9.toString(),
        });
    },
});

console.log('[+] Hook installed; Ctrl-D or `quit` to unhook.');
