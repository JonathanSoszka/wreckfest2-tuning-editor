# Wf2App

The WPF desktop GUI — the end-user face of the project.

`net8.0-windows` · `UseWPF` · references `Wf2Core` only.

> **Status: G1–G5 done** (G5 duplicate verified in-game 2026-07-23)**.** Browse cars → presets → tuning values (names/units from `ParamMap`),
> **export** a preset to JSON, **copy** a tune between presets, **import** a tune from a file, and
> **duplicate** a preset. Writes go through the safe pipeline (backup + both mirrors; warns, but lets you proceed, if the
> game/Steam is running). Robust save/mirror/game discovery via `Wf2Core.SaveLocator`. See
> `docs/PLAN_gui.md` for the roadmap.

## Run it

```powershell
dotnet run --project Wf2App
```

It auto-loads the live save (`Documents\My Games\Wreckfest 2\<profile>\savegame\profile.sgfi`) if it
can find one; otherwise use **Open…**.

## Layout

- `Mvvm.cs` — `ObservableObject` + `RelayCommand`. No MVVM toolkit; the app is small (plan §6).
- `SaveViewModels.cs` — `CarVm` / `PresetVm` / `ValueRowVm`: read-only projections of `Wf2Core` types.
- `MainViewModel.cs` — loads a `SaveFile`, exposes `Cars`, the tree selection, status, and the write
  actions (`ExportSelectedPreset`, `PlanCopy`, `PlanImport`, `ApplyPlan`, `DuplicateSelectedPreset`).
- `TextPromptDialog.xaml(.cs)` — a small validated name prompt (G5 duplicate naming).
- `WindowsSteam.cs` — Windows-registry Steam path, fed to `Wf2Core.SaveLocator` (which is
  cross-platform and does the rest of the save/mirror/game-install discovery).
- `PlanPreviewViewModel.cs` — shared base for the copy and import dialogs: the grow toggle and the
  rendered `ImportPlan` preview (`PreviewLine`s), recomputed on every input change.
- `CopyPresetViewModel.cs` / `CopyPresetDialog.xaml(.cs)` — copy dialog: source is a preset picker.
- `ImportPresetViewModel.cs` / `ImportPresetDialog.xaml(.cs)` — import dialog: source is a parsed
  `.json` file. Both hand the confirmed plan back; the write is owned by `MainViewModel.ApplyPlan` →
  `SaveWriter.WriteAllMirrors`.
- `MainWindow.xaml(.cs)` — tree + detail grid + Duplicate/Export/Copy/Import buttons. Code-behind
  carries only view concerns: the file dialogs, the duplicate-name prompt, translating
  `TreeView.SelectedItem` (not bindable) onto the view-model, the one shared "warn if game running"
  gate (`ConfirmWriteDespiteRunning`), and surfacing write results as message boxes.

## The one hard rule

**No format logic in this project.** No byte parsing, no CRC, no LZ4, no offsets. This project
binds UI to `Wf2Core` and nothing else. If you're reaching for `BinaryReader` here, it belongs in
`Wf2Core` with a test.

Rationale: the format was expensive and error-prone to work out. Keeping it in one tested library is
what stops a UI convenience shortcut from silently corrupting someone's save.

## What it should do (per the project scope)

Scope is **easier tuning + preset export/import**, strictly within values the game itself can
produce — see the root [`README.md`](../README.md).

1. **Browse** cars and their tuning presets from a save.
2. **Edit** tuning values with real parameter names and units (`docs/PARAM_MAP.md`), showing each
   parameter's **legal min/max** from the `.ctms` schema.
3. **Export / import** presets — copy a tune between cars, save to a file, share, restore.
4. **Validate** on import: reject anything outside the `.ctms` range. Exceeding in-game limits is
   explicitly out of scope, so this is a real requirement, not a nicety.

## Safety requirements for any write path

- **Back up first** — timestamped copy to `Documents\Wreckfest2 Backups\`. Unconditional.
- Write **both** the real save (`Documents\My Games\...\savegame\profile.sgfi`) **and** the Steam
  `userdata\...\remote\` mirror. Writing only one lets Steam Cloud re-sync the old file back over
  the edit — this actually happened during development. Unconditional.
- Show the user a diff/summary before committing a write (the preview dialog).
- **Warn if Wreckfest 2 or Steam is running, but let the user proceed.** `SaveWriter.WriteAllMirrors`
  refuses by default (throws `GameRunningException`) and the CLI keeps that guard; the GUI checks
  `IsGameOrSteamRunning` itself, shows a Yes/No warning, and passes `force: true` if the user accepts.
  The core enforcement stays intact and tested — only the GUI opts to override it, per the user's
  request. The backup and both-mirror writes still happen, so a bad outcome is always recoverable.

## Format status

The save format is fully solved and writing is verified in-game, including size-changing edits — so
the UI can edit freely. The safety rules above still apply: they protect against *user* mistakes and
Steam Cloud races, not format bugs.
