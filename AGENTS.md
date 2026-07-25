# AGENTS.md — working guide for the Wreckfest 2 Save Editor

Read this first. It's the operating manual for anyone (human or AI) working in this repo.

## What this project is
A **Windows desktop app (C# / .NET 8 / WPF)** that makes tuning a Wreckfest 2 career profile
easier, and supports **exporting / importing tuning presets**. Most work happens in the `Wf2Core`
library and is driven by tests, not the GUI.

**Scope:** we do NOT try to exceed the game's own limits — every value written must be one the game
can legally produce (`.ctms` files give the legal ranges). Editing installed **parts** is a
secondary goal.

- **START HERE:** [README.md](README.md) — scope, status, gotchas
- **Authoritative format spec:** [docs/format.md](docs/format.md) — if anything contradicts it, it wins
- Tuning parameter names/units: [docs/PARAM_MAP.md](docs/PARAM_MAP.md)
- WARNING: [PLAN.md](docs/archive/PLAN.md) and [PROGRESS.md](docs/archive/PROGRESS.md) are **largely stale** (pre-2026-07-22) — history only

## ⛔ Safety rules — do not violate
These protect a real, in-use save file. Treat them as hard constraints.

1. **Never write to the live save while Wreckfest 2 is running.** (Steam itself running is fine —
   what matters is writing BOTH My Games and the userdata mirror so Steam doesn't re-sync the old
   file back over the edit.)
2. **Always back up before any write.** Timestamped copy to
   `C:\Users\Jonathan\Documents\Wreckfest2 Backups\`. No exceptions, including tests that
   touch real files.
3. **Do RE and tests on *copies*,** never the live file. The live save is the oracle, not a
   scratchpad.
4. **The live file changes on its own** (game/Steam touch it). Re-read before diffing; never
   assume a cached copy is current.
5. Don't try to defeat or spoof Steam Cloud — let Steam re-hash via `remotecache.vdf` on next
   launch. If a checksum inside the file must be recomputed, that's the game's format, not
   Steam's (see Milestone 0).

## Key paths
| What | Path |
|---|---|
| **Save file the game actually reads** | `C:\Users\<user>\Documents\My Games\Wreckfest 2\<steamid>\savegame\profile.sgfi` |
| Steam cloud **mirror** (not the target) | `...\Steam\userdata\66021486\1203190\remote\profile.sgfi` — write this too on deploy, or Steam re-syncs the old file back |
| Steam cloud manifest | `...\userdata\66021486\1203190\remotecache.vdf` (SHA-1 + size per file) |
| Game install | `C:\Program Files (x86)\Steam\steamapps\common\Wreckfest 2\` |
| **Part catalog** | `<install>\data\vehicle\{carNN,shared}\part\...\*.upgr` — loose files, ~1734. NOT the `data*.rpck` archives |
| Tuning definitions (legal ranges) | `<install>\data\vehicle\shared\part\tuning\*.ctms` |
| Local settings (non-cloud) | `C:\Users\Jonathan\AppData\Local\Wreckfest 2\` |
| Backups | `C:\Users\Jonathan\Documents\Wreckfest2 Backups\` |

Steam App ID **1203190**; userdata account **66021486**. Don't hardcode these in `Wf2Core` —
put them in config with auto-detection (the parser must stay account-agnostic for the
"generalize later" goal).

## Format cheat-sheet — full spec in [docs/format.md](docs/format.md)
- Every bbag file (`.sgfi`, `.upgr`, `.ctms`) = 20-byte header + **LZ4** block. Tags are reversed
  FourCC (`ifgs`=sgfi, `racc`=ccar, `smtc`=ctms). Little-endian throughout.
- Header `0x0C` = compressed length; **`0x10` = CRC-32C of the DECOMPRESSED payload — MUST be
  recomputed on write.** (Older notes calling it "cosmetic" are WRONG.)
- The decompressed tree is a **chain of chunks**: `[chunk][u32 CRC of chunk][u32 lenOfNext]...`
  Four integrity layers must all be recomputed on write. Wrong CRC → game **loads but strips all
  cars**. Wrong length → `Fatal Error: FS: Failed to Read 4 bytes...`.
- Tuning lives in `atvc` nodes: `[kind=2][count]` + 12-byte records `[paramIndex][aux][f32 value]`.
  Values are **physical, SI units** — not normalized, not the number shown in the UI.
- **NEVER patch bytes inside a compressed stream** — LZ4 literals are back-reference sources;
  patching 3 bytes silently corrupted 13. Always decompress -> edit -> recompress.

## Build & test
```powershell
# from repo root, once the solution exists
dotnet restore
dotnet build
dotnet test                      # runs Wf2Core.Tests — the acceptance gates
dotnet run --project Wf2Cli -- <args>     # headless RE / round-trip harness
dotnet run --project Wf2App               # launch the GUI
# ship build:
dotnet publish Wf2App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```
**Round-trip testing:** a parse->serialize round-trip is byte-identical ONLY if the compressed
payload is preserved verbatim. Once a writer **recompresses**, output bytes legitimately differ from
the game's (our LZ4 is not theirs) — verified fine in-game. So assert on **decoded logical content +
all CRCs valid**, not raw bytes, for any recompressing path.

## Workflow (every work session)
1. Read **PROGRESS.md** → "Open questions / blockers" and the test checklist. Pick the lowest
   unfinished test.
2. Work **test-first** against the acceptance tests in PLAN.md. Automated tests before
   empirical gates.
3. For **empirical gates** (A.3, B.3, C.1, C.4): the human must run the game. Prepare the
   exact steps, tell them precisely what to change and save, then diff the resulting file.
   Minimize the number of in-game sessions (batch requests) — this was a stated preference.
4. **Update PROGRESS.md**: flip test/milestone status, add a dated Changelog line, log any new
   fixture in the fixtures table, update blockers.
5. Never mark an empirical gate ✅ without a real in-game confirmation + a verified
   backup-restore.

## Conventions
- C# nullable enabled, `dotnet format` clean, warnings-as-errors in `Wf2Core`.
- Parser is **allocation-aware** but clarity first; correctness (byte-identical) over speed.
- No parsing logic in `Wf2App` — GUI calls `Wf2Core` only.
- Commit messages reference the milestone/test (e.g. `A.1: byte-identical serializer`).
- New binary findings go in `docs/format.md` with offsets and a hex example.

## Handy PowerShell (RE)
```powershell
# hex a slice
Format-Hex -Path .\fixtures\profile.sgfi -Count 64
# byte-diff two saves (offsets that changed)
$a = [IO.File]::ReadAllBytes("before.sgfi"); $b = [IO.File]::ReadAllBytes("after.sgfi")
0..([Math]::Min($a.Length,$b.Length)-1) | Where-Object { $a[$_] -ne $b[$_] } |
  ForEach-Object { "{0:X4}: {1:X2} -> {2:X2}" -f $_, $a[$_], $b[$_] }
```
