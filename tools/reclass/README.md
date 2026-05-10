# ReClass.NET projects

Saved `.rcnet` struct definitions for the in-memory layouts we map
interactively in ReClass.NET. Commit the `.rcnet` files here so future
sessions don't have to remap the same struct.

## How to use

1. Launch `%LOCALAPPDATA%\Programs\ReClass.NET\ReClass.NET_Launcher.exe`
   **as Administrator** (needed to attach to nba2k16.exe).
2. `Process → Attach → nba2k16.exe`.
3. Paste a known struct pointer in the address bar (e.g. the active
   MyPlayer ptr from the trainer log, like `0x7FF6E2F73F90`).
4. Click bytes to type them (Int32, Float, UTF-16, Pointer, etc.) and
   name the fields. Pointer fields auto-resolve to nested structs.
5. `File → Save` into this folder. Use the name of the struct as the
   filename, e.g. `player-struct.rcnet`.

## What to map (in priority order)

- **MyPlayer struct** — extend what `GameOffsets` already knows. Confirm
  the +0x3C4 ratings region, the +0x419 badge region, the +0x80 phys
  sub-buffer. Find the animation slot block (probably near badges).
- **Phys sub-buffer** (via player+0x80) — height/wingspan are known;
  what else lives in there?
- **MyCareer save struct** (eventually) — backing store for VC / skill
  points / contract / endorsement. Requires the MyCareer resolver
  pointer (Phase 7).

## Convention

- One `.rcnet` per logical struct. Don't mash multiple structs into one
  project.
- When you find a field worth pinning, add a corresponding constant in
  `GameOffsets` so the C# trainer can use it.
