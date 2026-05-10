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
