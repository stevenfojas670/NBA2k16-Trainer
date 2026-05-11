// Sanity probe: hook a guaranteed-hot Win32 API and verify the send() pipe
// reaches the Python driver. PeekMessageW is called by every Win32 game's
// main loop every frame, so this fires constantly regardless of game state.
//
//     python tools\frida-scripts\verify-hook.py tools\frida-scripts\probe-hot-path.js 3
//
// If you see frame=60, 120, 180 ... the toolchain is fully working.

'use strict';

const target = Process.getModuleByName('user32.dll').getExportByName('PeekMessageW');
console.log(`[+] Hooking user32!PeekMessageW at ${target}`);

let frame = 0;
const LOG_EVERY = 60;

Interceptor.attach(target, {
    onEnter(args) {
        if (frame++ % LOG_EVERY !== 0) return;
        send({ probe: 'PeekMessageW', frame });
    },
});
