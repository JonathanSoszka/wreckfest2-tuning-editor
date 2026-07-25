# Wreckfest 2 Runtime Editor — Plan (approved 2026-07-20)

> ## ❌ ABANDONED — do not implement. Kept for history.
> This live-memory approach existed only because edited save files appeared unfixable. As of
> 2026-07-22 the save format is solved and writing works (`docs/format.md`), so this is unnecessary.
> It also never worked: value scans only ever found downstream mirror copies, and the authoritative
> value the Save button reads was never isolated. The `Wf2Cli memscan` code still builds and is
> harmless, but it is not on the critical path.

## Why
Edited save *files* get stripped by the `racc` record hash (see memory/record-hash-is-the-wall).
Editing the game's **live memory** and letting the game save normally sidesteps the hash entirely —
the game computes valid hashes on save. No anti-cheat ships with WF2 (no EAC/BattlEye; PlayFab/Steam
online only), so this is safe for offline/garage/career use. Do NOT take edited values into ranked online.

## Decisions
- **Tuning first** (M1–M5), then parts (M6).
- **AOB signature scanning** for durable re-acquisition across launches/updates.

## Architecture (extends the existing C# solution)
- `Wf2Core/Memory/` — `NativeMethods` (RPM/WPM/VirtualQueryEx P/Invoke), `GameProcess` (attach,
  read/write, region enumeration), `MemoryScanner` (float find + next-scan filter + candidate persistence).
- `Wf2Cli` — `memscan attach|find|filter|list|read|write` (the discovery/edit driver).
- `Wf2App` (WPF, later) — attach + live tuning sliders with extended ranges + save reminder.

## Milestones & acceptance tests
- **M1 attach & read** — CLI prints module base, reads 'MZ' at image base, lists readable regions. [CODE DONE, needs live test]
- **M2 locate tuning** — anchor on brake balance float (~pct/100) + percent byte; find/filter to the exact address; map neighbouring tuning fields.
- **M3 re-acquisition** — AOB signature on the code that reads the struct so it auto-relocates each launch.
- **M4 write live** — set brake balance from the tool; in-game gauge updates; save in-game; reload persists (no strip).
- **M5 tuning UI** — WPF panel, extended ranges, apply + save reminder.
- **M6 parts** — investigate part reference/index representation; safe swap; persists. (Expected hardest.)

## Discovery session recipe (M2)
1. Restore real save; launch game; enter garage on a car with adjustable brakes (RoadSlayer @ known %).
2. `memscan find <currentBrakeFloat>` → candidate set.
3. Change brake % in-game; `memscan filter <newFloat>`; repeat once → unique address.
4. Map the tuning struct around it; record offsets.

## Risks & mitigations
- ASLR/dynamic alloc → never hardcode absolute addresses; resolve via module base + AOB each session.
- Game patches shift offsets → keep signatures in editable config; document re-find.
- Bad writes crash game → clamp to tested ranges; expand incrementally.
- Active-car selection → detect which vehicle struct is live.
- Parts are references not scalars → M6 genuinely harder; tuning delivers value first.
