# Cheat Engine scripts

Lua + Auto Assembler snippets we author during discovery, before porting to
C# in the main trainer. The full mapping table (`NBA2K16.CT`) lives
elsewhere (in `%USERPROFILE%\Documents\My Cheat Tables\`); this folder is
for *our* prototypes — small, focused, easy to paste into a CE script
window.

## Convention

- One `.lua` or `.asm` file per discovery question. Name it after what it
  investigates, not how (e.g. `find-archetype-write-site.asm`, not
  `aob-scan.asm`).
- Top of every file: a comment header with date + goal + result. If a
  script's question has been answered and the result ported to C#, leave
  the header as a breadcrumb.
- Self-contained: the script should be pasteable into a fresh CE session
  with `nba2k16.exe` attached, with no external state.

## Workflow rule

**CE first, C# second.** If you're about to add a new hook or struct
offset to the trainer, prototype it here first. Validate that the bytes
do what you think they do *before* writing the C# patch site.
