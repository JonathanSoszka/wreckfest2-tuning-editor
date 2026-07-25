# Wreckfest 2 Tuning Editor

A small Windows desktop app for editing the tuning presets in your **Wreckfest 2** career
profile — adjust setups with sliders, and **export / import / duplicate** presets so you can
reuse a good tune across cars, back it up, or share it with a friend.

Everything it writes stays **within the game's own limits** — it never produces a value the game
itself couldn't. It's an unofficial community tool, not affiliated with Bugbear or THQ Nordic.

## What it does

- Browse every car in your profile and each car's tuning presets.
- Edit tuning values with sliders that snap to the game's own legal steps.
- **Create** a fresh preset (game defaults)
= **duplicate** existing presets
- **Import & Export** presets from a `.json` file
- **Writes safely** by backing up your profile before any
## Download & run

1. Grab `Wf2App.exe` from the [latest release](../../releases/latest).
2. Double-click it. **No .NET install needed** — it's a self-contained build.

Windows SmartScreen may warn about an unsigned app the first time (Expand → *More info* → *Run
anyway*). The app finds your profile automatically.

## Safety — please read once

- **Close Wreckfest 2 before saving.** Never write to the live profile while the game is running;
  the app warns you if it detects the game or Steam running.
- **Your profile is backed up automatically** before each write, to
  `Documents\Wreckfest2 Backups\`. If anything ever looks wrong in-game, restore the most recent
  backup.
- This edits your real career profile. It only writes in-game-legal values, but as with any save
  editor, keep the backups until you're comfortable.

Want to share a save (e.g. for a bug report) without leaking your Steam/online ids? Run
`wf2 scrub <in.sgfi> <out.sgfi>` (from `Wf2Cli`) to anonymize it first.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build
dotnet test                     # Wf2Core.Tests

# run the GUI
dotnet run --project Wf2App

# build the standalone single-file exe
dotnet publish Wf2App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

## License

[MIT](LICENSE) © 2026 Jonathan Soszka.

---

# Developer notes — the save format

The rest of this document is the reverse-engineering reference for anyone (human or AI) working on
the format. If you just want to use the app, you're done above.

## Scope

**Goal:** make tuning a Wreckfest 2 career profile *easier*, and support **exporting / importing
tuning presets** (save a tune, reuse it on another car, back it up, share it).

**Explicitly OUT of scope:** exceeding the game's own limits. Every value we write must be one the
game itself can legally produce. `.ctms` files define the legal min/max for every tunable parameter
and should be used to **validate** imports. (Earlier drafts of `docs/archive/PLAN.md`/`AGENTS.md` described a
"beyond what the game allows" goal — that was wrong and has been corrected.)

Secondary/original ambition: editing installed **upgrade parts**. Still of interest, but tuning
presets are the active goal.

Personal-first, but `Wf2Core` is written to be account-agnostic and reusable.

## Status

**The save format is fully solved, and reading *and writing* both work.** A save rebuilt entirely
by our own code loaded in-game with all 21 cars and every preset intact, and the edited value applied.

| Area | State |
|---|---|
| Container, LZ4, CRC-32C | ✅ solved and verified |
| Chunk chain + all 4 integrity layers | ✅ solved; write verified in-game |
| Cars / presets / tuning records | ✅ decoded (`atvc`, 12-byte records) |
| Multi-block payloads (cars > 64 KiB) | ✅ solved — read **and** write verified in-game |
| Tuning parameter names + units | ✅ 37/40 indices mapped (`docs/PARAM_MAP.md`); 51/52/57/58/59 need in-game ID |
| aux → value arithmetic | ✅ solved 2026-07-23 — `value = min + aux × (max − min) / steps` (`docs/PARAM_MAP.md`) |
| Empirical stored-unit ranges | ✅ `wf2 calibrate` over 241 presets → `Wf2Core/ParamRanges.cs` |
| **Variable-size writes** | ✅ verified in-game (2026-07-22) |
| Preset export + import (Tier 1 & 2) | ✅ verified in-game 2026-07-22 (`Wf2Core/PresetIo.cs`) |
| Preset duplicate / create | ✅ verified in-game 2026-07-23 |
| Range validation | ✅ exact per-parameter ranges from `.ctms` where known, empirical otherwise |
| GUI (`Wf2App`) | ✅ browse, slider-edit, create, duplicate, export, import-as-new (`docs/PLAN_gui.md`) |

**No known format gaps remain.** Remaining work is feature work, not research.

## Documentation map

| Doc | What it is |
|---|---|
| `docs/format.md` | **Authoritative** binary format spec |
| `docs/PARAM_MAP.md` | tuning `paramIndex` → name, units, ranges, and the aux→value arithmetic |
| `docs/PLAN_presets.md` | plan for the preset export/import feature |
| `docs/PLAN_gui.md` | plan for the desktop GUI |
| `AGENTS.md` | working conventions |
| `docs/archive/` | historical docs — superseded, kept for provenance ([index](docs/archive/README.md)) |

If a doc contradicts `docs/format.md`, **`format.md` wins.**

## Safety rules (for the code)

1. **Never write to the live save while Wreckfest 2 is running.**
2. **Always back up first** — timestamped copy in `Documents\Wreckfest2 Backups\`.
3. **The game reads `Documents\My Games\Wreckfest 2\<steamid>\savegame\profile.sgfi`.**
   The Steam `userdata\...\remote\` copy is only the **cloud mirror** — writing there alone does
   nothing useful, and Steam will overwrite it from My Games. When deploying, write **both** so
   Steam doesn't re-sync the old file back.
4. Do RE and tests on **copies**. The live save is the oracle, not a scratchpad.

## Gotchas that cost us days

- **Header `0x10` is a real CRC-32C** of the decompressed payload. Older docs claim it is "cosmetic
  / not validated by the game" — that is **false** and was the single most expensive mistake here.
- **Never patch bytes inside a compressed LZ4 stream.** Literals are reused as back-reference
  sources by later matches; patching 3 literal bytes silently corrupted 13 decoded bytes and
  invalidated several experiments. Always decompress → edit → recompress.
- **The part catalog is loose `.upgr` files**, not the `data*.rpck` archives. No archive parsing needed.
- Getting a chunk CRC wrong makes the game **load but silently strip** all cars/tuning. Getting a
  length wrong causes `Fatal Error: FS: Failed to Read 4 bytes...`. The symptom tells you which.
- Values in presets are **physical, in SI units** (ride height in metres, springs in N/m), not
  normalized and not the numbers shown in the UI — and the UI can switch metric/imperial.
- **The cars payload spans multiple LZ4 blocks.** Reading only the container gives a
  complete-looking 64 KiB prefix *whose CRC validates*, silently dropping every car past it. Blocks
  must be decoded with the previous output as a **dictionary** — never by concatenating compressed
  bytes. This cost a long detour into a phantom "the game is truncating your save" theory; it was
  the reader at fault, not the game.

## Build & test

```powershell
dotnet build
dotnet test                                # Wf2Core.Tests
dotnet run --project Wf2Cli -- tuning "<install>\data\vehicle\shared\part\tuning"
```

## Layout

Each project has its own README with details and rules.

| Project | Purpose | State |
|---|---|---|
| [`Wf2Core`](Wf2Core/README.md) | all format logic — container, CRC, chunk chain, `.ctms`, catalog. No UI. | active |
| [`Wf2Cli`](Wf2Cli/README.md) | headless RE + verification harness (`cars`, `settune`, `tuning`, `parts`, `hexdiff`, `scrub`, …) | active |
| [`Wf2Core.Tests`](Wf2Core.Tests/README.md) | xUnit acceptance tests + real-save fixtures (anonymized) | active |
| [`Wf2App`](Wf2App/README.md) | WPF GUI — browse, slider-edit, create, duplicate, export, import presets | active |
