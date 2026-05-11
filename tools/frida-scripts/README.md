# Frida scripts

JavaScript hook scripts for runtime instrumentation of `nba2k16.exe`.

## Running a script

```powershell
# 1. Make sure NBA 2K16 is running.
# 2. Attach + load script (use the exe name; Frida resolves the PID for you):
frida -n nba2k16.exe -l trace-template.js
```

The session stays attached — edit the JS file and save to hot-reload.
`Ctrl-D` (or `quit`) ends the session and unhooks cleanly.

## Pinned versions

We tested against:

- `frida` 17.9.7
- `frida-tools` 14.8.2

If a later version breaks a script, pin via
`python -m pip install "frida==17.9.7" "frida-tools==14.8.2"`.

## Don'ts

- Don't attach Frida to an address where Cheat Engine has an active BP
  (hardware or software). Both patch the prologue and the second one
  clobbers the first. Use CE for discovery, Frida for instrumentation.
- Don't hook the rating-rebuild hot path with a heavy `console.log` —
  60 Hz × verbose logging will stall the game. Use `send()` with small
  payloads or buffer locally and flush on shutdown.

## Frida 17 API notes

The Module class lost its static `getExportByName(mod, fn)` in Frida 17.
Use the instance API instead:

```js
const target = Process.getModuleByName('user32.dll').getExportByName('PeekMessageW');
```

For an address inside the game by module-relative offset:

```js
const mod    = Process.findModuleByName('nba2k16.exe');
const target = mod.base.add(0x4F56BF);
```

## Scripts in this folder

- `trace-template.js` — starter template. Hooks a single address by
  module-relative offset, logs general-purpose registers on entry. Copy
  and adapt for a specific function.
- `probe-hot-path.js` — sanity probe. Hooks `user32!PeekMessageW` (called
  by every Win32 game's message pump) to validate the `send()` pipe
  end-to-end without depending on game state.
- `verify-hook.py` — non-interactive Python driver. Loads a JS script,
  injects, collects messages for N seconds, then detaches. Use this when
  you want to confirm a hook works without the Frida REPL. Default
  target `nba2k16.exe`, default duration 6 s.

Run the sanity probe with:

```powershell
python tools\frida-scripts\verify-hook.py tools\frida-scripts\probe-hot-path.js 4
```
