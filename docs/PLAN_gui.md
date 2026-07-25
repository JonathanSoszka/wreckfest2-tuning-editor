# Plan — Desktop GUI (`Wf2App`)

**For review before implementing.**

The end-user face of the project. Today `Wf2App` is a ~12-line skeleton; everything so far has been
CLI + core. The format is solved and `PresetIo` is verified in-game, so this is UI-binding work, not
research.

## Decisions taken (2026-07-22)

1. **Write model: direct write, with mandatory backup.** One click applies to the live save. The app
   backs up first, refuses while Wreckfest 2 **or Steam** is running, and writes **both** the My
   Games save and the Steam `userdata` mirror. Removing the deploy-batch friction is the whole point
   of the GUI; the safety rails below are what make that safe.
2. **v1 scope: preset manager only.** Browse cars → presets → values (with names and units), and
   export / import / copy tunes. **No per-slider editing in v1** — that would lean on the
   `paramIndex` → `.ctms` binding, which is inferred, not proven. v1 rides entirely on the verified
   `PresetIo` path. Slider editing is a v2 built after calibration (see §7).

## The one rule this project already has

**No format logic in `Wf2App`.** No `BinaryReader`, no CRC, no LZ4, no offsets. The UI binds to
`Wf2Core` and nothing else. The format was expensive to work out; keeping it in one tested library is
what stops a UI shortcut from corrupting a save. Anything binary you reach for belongs in `Wf2Core`
with a test.

---

## 1. What v1 does

```
┌ Save: Documents\My Games\...\profile.sgfi          [Reload] [Open other...] ┐
│                                                                             │
│ Cars                    │  Hurricane — Preset 2                             │
│  ▸ RoadSlayer           │  14 stored values                                │
│  ▾ Hurricane            │                                                   │
│      Default Preset  0  │   Braking Balance        52 %                     │
│      Hybrid_        25  │   Differential - power   60                       │
│      Preset 2       14 ◀│   Springs - front        56.8 kN/m                │
│      Mora Raceway   18  │   parameter 51           750       (unmapped)     │
│  ▸ Jackal               │   …                                               │
│                         │                                                   │
│                         │  [Export tune…]   [Import tune…]   [Copy from ▾]  │
└─────────────────────────┴───────────────────────────────────────────────────┘
```

- **Tree**: cars, each expanding to its presets with a stored-value count. Read from
  `SaveFile.Cars`.
- **Detail**: the selected preset's values, shown through `ParamMap` — real name, value rendered in
  the UI's unit (`ParamInfo.Display`). Unmapped indices show as `parameter N` with an "unmapped" tag.
  Purely a viewer in v1; values are not editable here.
- **Export tune…** → file dialog → `PresetIo.ToJson(PresetIo.Export(...))`.
- **Import tune…** → pick a `.json` → **preview dialog** (the `ImportPlan`: applied / skipped /
  warnings) → Apply.
- **Copy from ▾** → pick another car+preset in the same save as the source, same preview dialog. This
  is import without the file round-trip (export in memory, plan, preview, apply).

## 2. The Apply dialog (the only write path)

Every write — imported file or in-save copy — goes through one confirmation dialog. Nothing writes
the live save without it.

```
Apply “Hybrid_” to Hurricane / Preset 2 ?

  10 values will change:
     Braking Balance      35 %   →  52 %
     Springs - front      38.6   →  56.8 kN/m
     …
  4 already match.
  11 skipped — Preset 2 has these sliders at default, so there is no
     record to overwrite. (Set them in-game once, then re-import.)

  ⚠ Cross-car import: tune came from a different chassis.        [only when true]

  Backup → Documents\Wreckfest2 Backups\profile.<stamp>.bak.sgfi
  Writes → My Games save  +  Steam userdata mirror

              [ Cancel ]   [ Apply and back up ]
```

This is the GUI form of `wf2 preset import --dry-run`: the same `ImportPlan`, rendered.

## 3. Safety pipeline (non-negotiable)

In order, on Apply:

1. **Game/Steam running?** → `IsGameOrSteamRunning()`. If yes, **warn** (Yes/No) that the game can
   overwrite the edit and Steam Cloud can re-sync a stale copy — and let the user proceed at their own
   risk (`WriteAllMirrors(..., force: true)`). The core still refuses by default; only the GUI opts to
   override, per the user's request. Changed from a hard refusal on 2026-07-23.
2. **Back up** the current live save, timestamped, to `Documents\Wreckfest2 Backups\`
   (`SaveWriter.DefaultBackupDir`). Never overwrite an existing backup.
3. **Write both mirrors** — My Games **and** the `userdata\...\remote\` copy. Writing only one lets
   Steam Cloud re-sync the stale file back over the edit. This bit us during development; it is not
   optional.
4. Report what was written and where the backup went.

## 4. Path discovery

All three paths auto-detect on this machine, so v1 detects and shows them, with a manual override:

| Path | How | Verified present |
|---|---|---|
| My Games save | `Documents\My Games\Wreckfest 2\<profileId>\savegame\profile.sgfi` — single profile dir | ✅ |
| userdata mirror | `<Steam>\userdata\<accountId>\1203190\remote\profile.sgfi` | ✅ |
| Steam root | `libraryfolders.vdf` if the default path is absent | ✅ default present |

If discovery finds zero or several candidates, ask; never guess silently. `.ctms` schemas are not
needed in v1 (no range enforcement without slider editing) but the same game-install discovery will
find them for v2.

## 5. Core changes needed (`Wf2Core`, all tested)

v1 is mostly binding, but it needs two additions so `Wf2App` can honour "no format logic here":

- **`SaveWriter.WriteAllMirrors(...)`** — back up once, then write a *set* of target paths
  (My Games + userdata). Today `Write` takes a single target; the two-mirror rule shouldn't live in
  the UI. Backup happens once, both writes use the same bytes.
- **`SaveLocator`** ✅ built (G4) — discovery of the save path(s), userdata mirror(s), and game
  install (via `libraryfolders.vdf`). Pure path logic; unit-tested against a faked filesystem root.
  Cross-platform, so the Windows-registry Steam path is injected by the caller (`WindowsSteam.Roots`).
- **`CarCollection.DuplicatePreset(car, sourcePreset, newName)`** (for **G5**) — insert a new preset
  block into the car's `pstv` node (new name string + a copy of the source's `stvc` and `atvc` nodes
  and records) and bump the preset count. Analogous to `AddTuningValue` but one level up: it adds a
  *preset*, not a *record*. Reuses `SetDecodedPayload` for the resize. Guard rails: reject a
  duplicate/empty name, cap the preset count. Unit-tested against a fixture (new preset present,
  values equal the source, source untouched, byte-length grew by exactly the new block, re-serialize
  round-trips) — but the in-game confirmation in §8a is the one that actually clears it.

Nothing else for G1–G4. Export/import/plan/apply already exist and are verified.

## 6. Architecture

- **MVVM, no framework.** Plain `INotifyPropertyChanged` view-models; WPF data-binding. The app is
  small enough that a DI/MVVM package would be more ceremony than it saves.
- **View-models**: `MainViewModel` (loaded save, selected car/preset, paths), `CarVm`, `PresetVm`,
  `ValueRowVm` (name, display value, unmapped flag), `ImportPreviewVm` (wraps an `ImportPlan`).
- **`ISystemState`** injected (already exists) so the running-app check is fakeable — the Apply
  pipeline gets a smoke test without needing the game.
- **Threading**: load/serialize a ~9 KB save is sub-millisecond; no async needed. Keep it simple.

## 7. Explicitly out of v1

| Deferred | Why | Where it goes |
|---|---|---|
| Per-slider editing | needs proven `paramIndex` → `.ctms` ranges | v2, after calibration |
| Range enforcement | binding is inferred; a wrong limit shown as truth is worse than none | v2 |
| Tier 2 import (add missing records) | its own feature, `PLAN_presets.md` M4 | after M4 lands |
| Duplicating a preset (create by copy) | resizes the preset list; new format work | **now planned — G5** (§8a) |
| Deleting presets, or new-from-scratch presets | shrinks/creates list entries; more format work + UX | later |
| Identifying idx 3,4,51,52,57-59 | calibration task, not UI | `PARAM_MAP.md` "Observed but unmapped" |

## 8. Milestones

- **G1 — Read-only browser.** ✅ DONE. Cars→presets tree + a values detail grid over a `SaveFile`,
  rendered through `ParamMap` (name, UI value, raw SI, aux; unmapped indices shown muted). Auto-loads
  the live save on launch, plus Open… / Reload. No write path. Files: `Mvvm.cs`, `SaveViewModels.cs`,
  `MainViewModel.cs`, `MainWindow.xaml(.cs)`. Confirms the binding and `ParamMap` rendering.
- **G2 — Export & Copy.** ✅ code-complete (awaiting one in-game confirmation of the GUI write). A
  preset's detail pane has **Export…** (writes JSON via `PresetIo`, no save write) and **Copy from…**
  (`CopyPresetDialog`: pick a source preset, optional grow, live `ImportPlan` preview, then
  **Back up & apply**). The write goes through `SaveWriter.WriteAllMirrors` — back up, write the save
  **and** the userdata mirror, and warn (not refuse) if the game/Steam is running. Path/mirror discovery is the
  minimal `SavePaths` probe (full `SaveLocator` is G4). Files: `SavePaths.cs`,
  `CopyPresetViewModel.cs`, `CopyPresetDialog.xaml(.cs)`; `SaveWriter.WriteAllMirrors` + tests in core.
  The `PresetIo` path itself is already verified in-game, so this confirms GUI wiring, not the format.
- **G3 — Import from file.** ✅ code-complete (awaiting one in-game confirmation). **Import…** on the
  detail pane picks a `.json`, parses it with `PresetIo.FromJson` (malformed/unknown-version files are
  rejected with a clear message), then shows the same preview/grow/apply as copy via
  `ImportPresetDialog`. The preview rendering and the write path are shared with copy through
  `PlanPreviewViewModel` — only the source (a file vs an in-save preset) differs. Files:
  `PlanPreviewViewModel.cs`, `ImportPresetViewModel.cs`, `ImportPresetDialog.xaml(.cs)`.
- **G4 — Path discovery + polish.** ✅ DONE. `Wf2Core.SaveLocator` (testable against a faked FS tree):
  finds every profile save, the userdata mirror(s), and the game install by following Steam
  `libraryfolders.vdf` across drives. The app feeds it the Windows-registry Steam path
  (`WindowsSteam.Roots`, since core is cross-platform) and retires the old app-side `SavePaths`.
  Auto-load handles zero / one / several profiles (loads the first, notes the rest); the footer shows
  the backup directory; write dialogs show the full backup path. Files: `SaveLocator.cs` +
  `SaveLocatorTests` (6) in core, `WindowsSteam.cs` in the app.
- **G5 — Duplicate Preset.** ✅ DONE, verified in-game 2026-07-23. A **Duplicate…** button on the
  detail pane prompts for a name (`TextPromptDialog`, defaulted to `"<name> copy"` and validated
  against the car's existing names) and creates a copy via `CarCollection.DuplicatePreset` → the safe
  write pipeline. Files: `TextPromptDialog.xaml(.cs)`, CLI `preset duplicate`,
  `CarCollection.DuplicatePreset` + `DuplicatePresetTests` (4) in core.
  *Verified in-game:* duplicated Hurricane `Hybrid_` → `Hybrid_ copy`; the new preset appeared in the
  list, was selectable, carried the copied tune, and all cars/other presets were intact. **This
  resolves the §8a `stvc` unknown** — see the note there.

- **G6 — Parts view.** 🟡 core built, UI removed (parked). The resolver `Wf2Core/EquippedParts.cs`
  (+ `LabeledString.cs`, shared with `GuideExporter`) turns a car's fitted paths into grouped,
  named, adjustable-flagged parts — friendly names from each `.upgr`'s `VEHICLE_UPGRADE_NAME` via the
  game install (`SaveLocator.FindGameInstall`, G4), falling back to a prettified variant. Fully
  tested (`EquippedPartsTests`, 12). The app UI (a parts panel shown when a car is selected) was
  **built and then removed** at the user's request on 2026-07-23 — "may add back later". Re-adding is
  pure wiring: `MainViewModel` resolves `SelectedCar.Parts` into two lists and `MainWindow` shows them
  when a car (not a preset) is selected. **Names only — hard performance stats (BHP, grip) are not
  recoverable from the `.upgr` without further RE, so they stay out of scope.**

- **G7 — New / empty preset.** ✅ built. `CarCollection.CreatePreset` adds an empty preset (all
  sliders at default = zero records, exactly like the game's own new presets) by inserting a preset
  block with a fresh empty `atvc` and an `stvc` copied from an existing preset of the same car (safe:
  the words carry no per-preset identity, §8a). Shares the insert/validation path with duplicate
  (`BeginPresetInsert` / `InsertPreset`). CLI `preset create`; GUI has **New preset…** both on a
  selected car's summary pane and in the preset toolbar, through the same safe write. Structurally
  verified (valid save, empty preset present, round-trips); rides the duplicate write path already
  confirmed in-game.

- **Visual theme — "Pit Wall" (dark), applied 2026-07-23.** A dark, telemetry-styled theme (UI
  direction A of three mockups) is implemented as implicit styles in `App.xaml`: near-black ground
  with a blue bias, one amber accent, tabular Consolas values (amber for the Value column, muted for
  Stored/aux), state as chips. Because WPF's stock controls are light and use system colours, the
  tree, DataGrid, ComboBox, CheckBox and scrollbars are retemplated; every window/dialog inherits the
  theme via implicit styles. Palette + type live in the mockup artifact and `App.xaml`'s header.

Each milestone is independently shippable; G1 alone is already more usable than the CLI for browsing.

## 8a. G5 — Duplicate Preset (design)

**What it does.** On the selected preset, a **Duplicate…** button prompts for a name (default
`"<name> copy"`, auto-suffixed to avoid a collision) and creates a new preset holding the same stored
values. The new preset appears in the tree; nothing about the source changes. Writes through the
same safe pipeline as G2/G3 (back up, both mirrors, warn-if-running).

**Why it is not just "copy".** G2 *Copy* writes values into an **existing** target preset. Duplicate
has no target — it **adds a preset slot** to the car. The car record's `pstv` node is
`[pstv][u32 0][u32 presetCount]` then `presetCount ×` `{ [str name] stvc[4×u32] atvc[u32 kind=2][u32 count] records }`
(see `CarCollection` / `docs/format.md`). Duplication inserts a new such block just before the list's
end and increments `presetCount`. It grows the decompressed payload — the verified variable-size
write path (same mechanism Tier 2 used) recomputes every length and CRC.

**The `stvc` node — RESOLVED 2026-07-23.** We preserve each preset's four `stvc` words verbatim and
have not decoded them. The open worry was that they might encode something position- or
identity-dependent (an index, id, or hash), so that copying them into a new preset would make the
game reject it. **The in-game test settled this: a duplicate with byte-identical `stvc` words loads,
is selectable, and works.** The four words carry no per-preset identity — copying them verbatim is
safe. (This does not decode what they *do* mean; it only proves duplication does not need to.)

**Naming.** The new name must be unique within the car (the game keys presets by name). Propose
`"<name> copy"`, then `"<name> copy 2"`, etc. Reject empty/duplicate names in the prompt.

**Acceptance (in-game):** duplicate a preset, load the save, confirm the new preset exists, is
selectable, carries the copied values, and that all cars and the other presets are intact — then a
second pass confirming the *source* preset is byte-for-byte unchanged.

**Open sub-questions:** allow duplicating an empty (all-default) preset? (Harmless but pointless —
lean yes, it is just an empty `atvc`.) And should Duplicate live on the preset's detail pane
(alongside Export/Copy/Import) or the tree's right-click menu? (Lean detail pane, for consistency.)

## 9. Open questions

1. **G2 in-game reconfirmation:** the `PresetIo` write path is verified. Do you want a fresh in-game
   test when the GUI first writes (confirms *wiring*: backup + both mirrors + refuse-while-running),
   or is the existing preset-import confirmation enough? (Recommend: one quick test — the two-mirror
   write is new code.)
2. **Framework:** plain MVVM as above, or do you want a specific toolkit (CommunityToolkit.Mvvm)? I
   lean plain — fewer dependencies for a small app.
