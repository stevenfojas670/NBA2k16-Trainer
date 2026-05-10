# NBA 2K16 Trainer

A small Windows trainer for NBA 2K16 that attaches to a running `nba2k16.exe` and
modifies gameplay constants in memory. Currently scoped to height-clamp editing.

## ⚠️ Offline single-player only

This trainer modifies code and data inside the live game process. **Do not use it
in any online mode.** 2K's anti-cheat may eventually flag and ban accounts that
play online with a modified process. Safe contexts:

- MyCareer (offline)
- Play Now (offline)
- Create Player

The trainer shows a disclaimer on first launch — accepting it means you understand
this constraint.

## Features (v1.0)

- **Auto-attach.** Polls for `nba2k16.exe` once per second and reports status.
- **Read-back.** Shows the live values of the hard min/max height clamps.
- **Hard height clamp editor.** Raises the global maximum (default 231.20 cm /
  ~7'7") and lowers the minimum (default 137.00 cm / ~4'6"). Apply / Restore
  buttons round-trip cleanly.
- **Disable per-position clamp.** Patches `cmp eax, 02` → `cmp eax, FF` in both
  `Player::GetHeight` and `Player::SetHeight` so the Create Player editor
  accepts any height for any position. Original bytes are captured on apply and
  restored on revert.
- **F1 hotkey.** Toggles the per-position clamp without alt-tabbing.
- **Settings persisted** to `%AppData%\NBA2K16Trainer\settings.json`.
  Optionally re-applies on attach.
- **Activity log** showing every memory write.

## Roadmap

This trainer is being built out in phases. v1.0 (above) is the foundation.
Subsequent phases progressively expand the trainer into a full MyCareer
editor. Items below are aspirational and shipped incrementally:

| Phase | Status | Scope |
|---|---|---|
| 1   | ✅ Shipped     | Height clamps (global + per-position). |
| 1.5 | 🚧 On `dev`    | Live MyPlayer profile + ratings editing via an AOB hook at `nba2k16.exe+0x4F56BF`. |
| 2   | 🔜 Next        | Cap removal — hard 25-99 rating clamp, position-based attribute caps, archetype caps, height-vs-attribute soft caps. Save survives reload. Badges UI. Cheat-registry refactor. |
| 3   | 🔜             | Roster editing (every player on every team) + force trades (move any player to any team). |
| 4   | 🔜             | Live match state — quarter, clocks, scores, fouls, teammate grade, MyPlayer in-game stats. |
| 5   | 🔜             | Gameplay sliders (~50 user/CPU floats) + cosmetics (shoes, accessories, gear). |
| 6   | 🔜             | Tendencies, hot zones, signature animations, appearance. |
| 7   | 🔜             | MyCareer core — VC, skill points, rep, XP, endorsements, contract value, trade demand. |
| 8   | 🔜             | Always-on gameplay code patches — always-green release, no fatigue, no fouls, no injuries, etc. |
| 9   | 🔜             | QoL — hotkeys, cheat profiles, live readback panel, persistent log, automatic mode detection. |

Scope is intentionally limited to MyCareer and modes that affect it.
MyGM owner-mode, MyLeague league management, MyTeam, Pro-Am, and other
online modes remain explicitly out of scope — see disclaimer above.

## Tooling

The trainer is built using a small Claude-Code-driven reverse-engineering loop.

**Static analysis** — understand the binary before touching live memory:

- **[Steamless](https://github.com/atom0s/Steamless)** — removes the
  SteamStub DRM wrapper from `nba2k16.exe`. Without it the `.text` section
  is encrypted on disk and Ghidra discovers almost nothing.
- **[Ghidra](https://github.com/NationalSecurityAgency/ghidra)** — NSA's
  open-source disassembler/decompiler. Used to statically analyze the
  unpacked `nba2k16.exe` (~61,700 functions). Provides the xref graph that
  drives every cap-removal patch and struct-layout discovery.
- **[LaurieWired/GhidraMCP](https://github.com/LaurieWired/GhidraMCP)** —
  MCP server that exposes Ghidra's decompiler, xref lookup, function
  search, and renaming to any MCP-aware client. Lets Claude Code drive
  Ghidra without alt-tabbing.

**Live process analysis** — validate against the running game:

- **[Cheat Engine](https://www.cheatengine.org/)** — the canonical memory
  editor for Windows games. Used to scan for values, set breakpoints,
  inspect structs, and prototype Auto Assembler / Lua scripts before
  hardcoding them into the C# trainer.
- **[miscusi-peek/cheatengine-mcp-bridge](https://github.com/miscusi-peek/cheatengine-mcp-bridge)** —
  MCP bridge over Cheat Engine. Same read/write/AOB/breakpoint surface,
  driven from Claude Code instead of the CE GUI. Used to verify hook
  installations and validate struct offsets in-loop.
- **[Frida](https://frida.re/)** — dynamic instrumentation. JavaScript
  hooks via `Interceptor.attach`, hot-reload during a single game session.
  Used for runtime call tracing where Cheat Engine breakpoints would
  crash the game on hot 60 Hz code. Scripts live in `tools/frida-scripts/`.
- **[ReClass.NET](https://github.com/ReClassNET/ReClass.NET)** — visual
  struct mapper. Attach to nba2k16.exe, paste a known pointer, click
  bytes to type and name fields. Used to extend the `GameOffsets` map
  without xref-walking through Ghidra. Projects saved in `tools/reclass/`.

**Agent driver:**

- **[Claude Code](https://claude.com/claude-code)** — the agent driving
  the loop. Reads decompiled functions, traces xrefs, designs patches,
  writes the C# implementation, runs builds, and iterates.

The combination means the project's reverse-engineering output (offsets,
patch sites, struct layouts) lives in code rather than scattered notes,
and the trainer can grow without losing context about *why* each address
matters. See `tools/README.md` for the per-tool workflow rules.

## Build

Requirements:
- Windows 10/11
- Visual Studio 2022 (17.x) **or** the .NET 10 SDK
- The NBA 2K16 PC game (64-bit)

```powershell
dotnet build NBA2K16Trainer.csproj -c Release
```

Output ends up in `bin\x64\Release\net10.0-windows\NBA2K16Trainer.exe`.
The project targets `x64` explicitly because NBA 2K16 is 64-bit and
`Process.MainModule` will throw if bitness disagrees.

## Run

1. Launch NBA 2K16.
2. Right-click `NBA2K16Trainer.exe` → **Run as administrator**.
   (The embedded manifest requests this automatically when launched normally,
   but admin is required — `OpenProcess(PROCESS_ALL_ACCESS)` fails with
   ERROR_ACCESS_DENIED otherwise.)
3. Accept the disclaimer.
4. The status banner turns green when the trainer attaches.
5. Set your desired max/min and click **Apply**, or toggle the per-position
   clamp checkbox.

## Reverse-engineering notes

All findings are documented in the project handoff brief. Key offsets within
`nba2k16.exe`:

| Offset | What | Default |
|---|---|---|
| `+0x1FEA3F8` | Hard max height float | `231.20f` |
| `+0x1DC6A5C` | Hard min height float | `137.00f` |
| `+0xA3FB12` | `cmp eax, 02` in `SetHeight` (per-position gate) | `83 F8 02` |
| `+0xA30F42` | `cmp eax, 02` in `GetHeight` (per-position gate) | `83 F8 02` |

Pointer-chain work for per-player attribute editing (Tier 4 in the brief) is
out of scope for v1.0.

## Limitations

- Does not currently sync the cached/derived height at `Player + 0x4C` — if a
  rendered character looks unchanged after a height edit, that's why. Adding
  this requires resolving a pointer chain to the current player record.
- Does not edit other attributes (weight, wingspan, ratings). Their setter and
  clamp addresses are in the brief but disassembling each clamp constant is
  separate work.

## License

Personal-use trainer. Not affiliated with 2K Games or Visual Concepts.
