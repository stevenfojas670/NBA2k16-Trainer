# Tooling

Scripts and configs for the external RE tools the trainer's discovery loop
relies on. Tool binaries themselves live on the user's machine (not in the
repo) so this tree stays text-only.

## Tool-to-job map

| Job | Tool | Folder |
|---|---|---|
| Map an unknown struct visually (find field offsets) | **ReClass.NET** | `reclass/` |
| Trace a hot function's args / register state at runtime | **Frida** | `frida-scripts/` |
| Prototype a hook or memory walker before porting to C# | **Cheat Engine** (Lua + AA) | `ce-scripts/` |
| Static decompile / xref walking | **Ghidra MCP** | (uses live MCP, no scripts) |
| Live memory read/write, AOB scan, BPs | **Cheat Engine MCP** | (uses live MCP, no scripts) |
| Retroactive "who wrote this byte" | **WinDbg Preview + TTD** *(not installed yet)* | — |
| Modify the rendering pipeline (shaders, FOV, overlays) | **3DMigoto + Kiero + RenderDoc** *(not installed yet)* | — |

## Where the tools live

- **Frida** — installed via `python -m pip install frida frida-tools`. Run
  scripts from this repo: `frida -n nba2k16.exe -l frida-scripts/<script>.js`.
- **ReClass.NET** — portable build at
  `%LOCALAPPDATA%\Programs\ReClass.NET\` (run `ReClass.NET_Launcher.exe`).
  Must launch as Administrator to attach to nba2k16.exe.
- **Cheat Engine** — pre-existing install. `.CT` files live outside this
  repo (in `%USERPROFILE%\Documents\My Cheat Tables\`); snippets we author
  belong in `ce-scripts/` here.

## Workflow rules of thumb

- **CE first, C# second.** Validate a new hook or offset in CE Lua / AA before
  hardcoding it into the trainer. Saves rebuild cycles.
- **Don't overlap Frida + CE BPs on the same address.** They both patch the
  prologue; one will clobber the other. CE for discovery, Frida for
  instrumentation, never both at the same byte.
- **ReClass.NET needs Admin** to attach if nba2k16 runs elevated.
- **Frida hot-reloads scripts.** Edit a JS file while a session is attached
  and it reapplies — much faster than the trainer's edit-rebuild cycle.
