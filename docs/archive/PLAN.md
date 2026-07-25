# Wreckfest 2 Save Editor — Plan

> ## ⚠️ LARGELY STALE — kept for history. See `README.md` and `docs/format.md`.
> This file predates the format being solved (2026-07-22). Specifically WRONG below:
> * the **goal** (this project does NOT aim to exceed the game's limits — see README scope),
> * the **save path** (the game reads `Documents\My Games\...`, not the Steam `userdata` mirror),
> * the **part catalog** source (loose `.upgr` files, not `data*.rpck`),
> * **Milestone 0.2's conclusion that `0x10` is cosmetic** — it is a real CRC-32C and MUST be
>   recomputed. That false conclusion cost this project several days.

> Status is tracked in [PROGRESS.md](PROGRESS.md). This file is the **spec** (relatively
> stable); PROGRESS.md is the **living checklist**. Working conventions live in
> [AGENTS.md](AGENTS.md).

## Goal
A Windows desktop app that lets you edit a Wreckfest 2 career profile — **installed
upgrade parts (Part 1)** and **tuning presets (Part 2)** — far more easily than the in-game
UI allows.

Scope: **personal-first**, but the format code is built as a reusable library so it can be
generalized to other players/cars/game versions later.

---

## Tech stack
- **C# on .NET 8 (LTS)**
- **WPF** GUI (native Windows; swap to Avalonia only if cross-platform is ever needed —
  core is UI-agnostic so this is a leaf change)
- **xUnit** for automated tests (the round-trip gates live here)
- Binary work via `BinaryReader`/`BinaryWriter`, `Span<byte>`, `BinaryPrimitives`
- Distribution: `dotnet publish` → **self-contained single-file `win-x64` exe**

### Solution layout
| Project | Type | Purpose |
|---|---|---|
| `Wf2Core` | class lib | `profile.sgfi` parse/serialize, `.rpck` catalog, safe-write pipeline. **No UI.** All RE lives here. |
| `Wf2App` | WPF app | GUI on top of the core |
| `Wf2Cli` | console | Headless round-trip / RE harness (fast iteration without the GUI) |
| `Wf2Core.Tests` | xUnit | Automated acceptance tests + save fixtures |

---

## Key facts (environment)
- Game: **Wreckfest 2**, Steam **App ID 1203190**
- Install: `C:\Program Files (x86)\Steam\steamapps\common\Wreckfest 2\`
- Part/tuning data archives in install dir: `data00.rpck`, `data01.rpck`, `data02.rpck`
- **Save file:** `C:\Program Files (x86)\Steam\userdata\66021486\1203190\remote\profile.sgfi`
  (~7.3 KB; holds parts **and** tuning, per car)
- Sibling cloud files: `settings.sets`, `controllers.riub`, `livery_designs\*.uldd`
- Local (non-cloud) settings: `C:\Users\Jonathan\AppData\Local\Wreckfest 2\`
- Steam cloud manifest: `...\userdata\66021486\1203190\remotecache.vdf` — stores **SHA-1 +
  size** per file; Steam re-hashes on launch to detect local changes.
- Backups so far: `C:\Users\Jonathan\Documents\Wreckfest2 Backups\`

## Format knowledge so far (`profile.sgfi`)
- Proprietary Bugbear **binary node tree**; section tags are **reversed FourCC**
  (`ifgs`=`sgfi`, `forp`=`prof`, `ubas`, `urve`, …).
- **Part paths are prefix-compressed**: all share `vehicle/shared/part/`; each entry stores
  only the differing suffix (e.g. `.../engine/air_filter/sport`). Slots can be `stock`.
- Header (little-endian):
  - `0x0c` int32 = **payload length** ( = fileLength − header constant ). Updates on save.
  - `0x10` 4 bytes = **CRC-32C of the DECOMPRESSED payload. MUST be recomputed on write.**
    (Milestone 0.2 originally concluded "not validated / cosmetic" — that was WRONG. The probe that
    "loaded fine" was testing the Steam cloud mirror, not the file the game actually reads.)
- Tuning values are **float32 blocks** located after each car's part list (offsets/scale not
  yet mapped → Part 2).

## Safety rules (non-negotiable)
1. **Never** write the live save while `Wreckfest2.exe` **or** Steam is running.
2. **Always** create a timestamped backup before any write.
3. Prefer editing a copy + explicit "apply" step; keep a one-click "restore last backup".
4. Let Steam re-hash via `remotecache.vdf` on next launch (don't fight cloud sync).

---

## Phases, milestones & acceptance tests

Each milestone is **done** only when its automated tests are green **and** its empirical
in-game gate has been confirmed once (with a verified backup-restore afterward).

### Milestone 0 — Header field identified *(gate before any writing)*
- **Test 0.1 — Length field (auto):** int32 at `0x0c` == `fileLength − headerConstant` for
  100% of fixtures.
- **Test 0.2 — Checksum semantics (empirical+auto):** flip one non-header content byte,
  leave `0x10` untouched, load in-game.
  - ~~Loads fine → `0x10` not validated → "cosmetic"~~ **WRONG — see banner. `0x10` is a CRC-32C.**
  - Rejected/reset → `0x10` is validated → derive algorithm, add **Test 0.3**:
    `ComputeHeader(bytes) == originalHeaderValue` for 100% of fixtures.

### Milestone A — Safe byte-identical round-trip
- **Test A.1 — Round-trip identity (auto):** `Serialize(Parse(bytes)).SequenceEqual(bytes)`
  byte-for-byte, every fixture, no tolerance.
- **Test A.2 — Safety interlocks (auto):** write blocked (throws, file untouched) when the
  "game/Steam running" flag is set; every write produces a backup equal to the pre-write file.
- **Test A.3 — Game acceptance (empirical GATE):** app loads real save, writes it back
  unchanged (checksum recomputed if 0.2 requires); game boots with profile/cars/parts/
  credits/progress identical — no reset, no corrupt-save prompt.

### Milestone B — Edit upgraded parts
- **Test B.1 — Catalog sanity (auto):** every fitted part string in fixtures resolves to a
  catalog entry; 0 unresolved.
- **Test B.2 — Targeted-edit round-trip (auto):** change exactly one slot; re-parse; diff
  touches only that node + header fields.
- **Test B.3 — In-game verification (empirical GATE):** change two slots in the app (one
  swap, one → `stock`), launch game; both show exactly as set, car drives fine, nothing else
  changed; backup restores original.

### Milestone C — Edit tuning presets
- **Test C.1 — Mapping locked (empirical):** for each mapped slider, set a known in-game
  value + save; diff pins `(slider, offset, min, max, unit)`; decoded value matches in-game
  reading within display rounding.
- **Test C.2 — Encode/decode inverse (auto):** `Decode(Encode(v)) ≈ v` across full range;
  out-of-range input clamps to recorded min/max.
- **Test C.3 — Isolated-write round-trip (auto):** change one slider; diff confined to that
  float + header.
- **Test C.4 — In-game verification (empirical GATE):** set a distinctive tuning value in the
  app, launch game; tuning screen shows that value.

---

## Cross-cutting
- **Testing corpus:** collect before/after save snapshots as regression fixtures in
  `Wf2Core.Tests/fixtures/`.
- **Catalog extraction (Phase 1):** primary = parse `data*.rpck`; fast fallback = harvest
  plaintext part-path strings from the archives/exe to bootstrap the catalog.
- **Docs:** grow a short format spec (`docs/format.md`) as the tree is decoded.

## Risks
- **Header checksum (0.2)** — if hard-validated and nontrivial, write support hinges on
  reproducing it. Investigated first, cheaply.
- **`.rpck` format** — may be its own RE effort; string-harvest fallback de-risks Phase 1.
- **Game updates** may shift formats/part lists; config-driven + catalog-from-data absorbs most.

## First action
Milestone 0.1 + A.1 — scaffold the solution, build the parser + byte-identical serializer,
and settle the `0x10` header question (needs one tiny in-game change to diff).
