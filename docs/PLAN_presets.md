# Plan — Tuning Preset Export / Import

**Status: M2 (export), M3 (Tier 1 import), M4 (Tier 2 grow-import) done and verified in-game; M5 (warn-only range validation) done. Remaining: M6 wiring + the GUI (see `PLAN_gui.md`).**

Goal (per `README.md` scope): save a tune to a file, reuse it on another car, back it up, share it.
Strictly within values the game can legally produce — **no cap-breaking**.

---

## 0. RETRACTED: the "64 KiB cap" constraint does not exist

An earlier version of this plan was built around a claim that the cars container is a hard 64 KiB
buffer, that the save was full, and that the game was silently truncating and losing car data. **All
of that was wrong.**

The cars payload is a **multi-block LZ4 stream** (see `format.md` §7). The 64 KiB is just the block
size. The reader was decoding only block 1 — which yields a complete-looking prefix whose CRC even
validates — so cars stored past it appeared to vanish. Nothing was ever lost, and there is **no space
pressure on adding presets**.

Consequences for this plan:
- The old **M1 growth spike is unnecessary** and has been removed.
- The **Tier 1 / Tier 2** split (space-neutral vs growth) is no longer forced by a space limit.
  Tier 1 is still the *safer first step* simply because it is the already-proven write path.
- Import may add records freely, subject to the real constraint below.

**There is now no format-level constraint at all.** Multi-block writing was implemented and verified
in-game on 2026-07-22 (edited a preset on "Jackal", which lives in block 2 — loaded correctly with
all 21 cars intact). Every car and every preset is readable and writable.

## 1. Milestones

### M1 — Multi-block writing ✅ DONE (verified in-game 2026-07-22)
`SaveChunk.SetDecodedPayload` re-splits the payload across blocks and recomputes every block CRC.
Verified by editing a preset on "Jackal" (block 2) and loading it in-game. No longer blocking.

### M2 — Export ✅ DONE
`PresetIo.Export` / `ToJson` / `FromJson`. `wf2 preset export` and `export-all`.

*Verified:* JSON round-trips with exact float equality; a preset exported and re-imported onto
itself plans zero changes (`PresetIoTests`).

### M3 — Import: Tier 1 ✅ DONE (verified in-game 2026-07-22)
`PresetIo.Plan` / `Apply`, plan-then-apply so nothing is written unseen. Overwrites records that
already exist in the target preset; anything else is reported as skipped.

*Verified in-game:* exported Hurricane / `Hybrid_` and imported it onto Hurricane / `Preset 2` —
10 values overwritten, 4 already matching, 11 skipped. The save loaded with Braking Balance reading
**52 %** (stored 0.52), all 21 cars present and every other preset untouched.

*Also verified structurally:* decoded payload byte-length unchanged (70821), all CRCs valid,
byte-identical re-serialize, and a full decoded diff showing only the ten intended records changed.

### M4 — Import: Tier 2 (add missing records) ✅ DONE (verified in-game 2026-07-22)
Add records for parameters the target preset lacks, in ascending `paramIndex` order (the game's own
ordering), bumping the `atvc` count. `CarCollection.AddTuningValue`; enabled by
`PresetIo.ImportOptions(AllowAdd: true)` / CLI `--allow-grow`. Missing parameters become
`ImportPlan.Added` instead of `Skipped`.

This is the first write that grows the **decompressed** payload — every earlier in-game test (Tier 1,
settune) was decoded-size-neutral; only the compressed file moved. **The game accepts a grown
payload.**

*Verified in-game:* grow-imported Hurricane `Hybrid_` onto `Mora Raceway (Tarmac)`, adding 7 records
(gears 2–6 + Front Toe). Decoded payload grew by exactly 84 bytes (7×12); the game loaded with the
gearbox showing the added custom ratios, all 21 cars intact, and every other preset unchanged.

*Also verified structurally:* only the target preset changed, records stayed sorted, the target's own
parameters outside the import set were preserved, all CRCs valid, byte-identical re-serialize.

### M5 — Validation ✅ DONE (warn-only; exact where the schema is known)
Values written by an import/copy that fall out of range are flagged as `ImportPlan.RangeWarnings` and
shown by the CLI and GUI preview — never blocked (warn-only was the deliberate v1 choice).

Two range sources, checked in order:
1. **Exact schema** (`Wf2Core/TuningSchema.cs`) for the absolute (`armt`) parameters — the real
   `.ctms` `[min, max]`, now that the arithmetic is cracked (`value = min + aux×(max−min)/steps`; see
   `PARAM_MAP.md`). A breach here genuinely **exceeds the game's limit** (`RangeWarning.IsExact`). This
   fixed real false positives: anti-roll bar is legally 0–100000 but was only ever *observed*
   17500–80000, so the old empirical check wrongly flagged legal values.
2. **Observed range** (`Wf2Core/ParamRanges.cs`, baked from `wf2 calibrate` over 241 presets) — the
   fallback for **relative** parameters (springs/dampers = `prmt`, ride height = `rrmt`) whose
   absolute bounds are car-specific, and for still-unidentified indices. A warning here means "outside
   anything seen", not "illegal".

**Why `.ctms` works now but didn't before.** Its min/max are the *slider* range; the missing piece was
that `aux` is the slider position and the value is `min + aux×(max−min)/steps`. For absolute params the
schema min/max are already stored units, so they are the exact legal range. For relative params the
min/max are offsets against a car base that is still uncracked — hence the observed-range fallback.

### M6 — CLI, then GUI
`wf2 preset export|import`, then wire into `Wf2App`.

---

## 2. Export format (JSON)

Human-readable, diffable, shareable. `paramIndex` + `value` are **authoritative**; `name`/`unit`/
`display` are informational only (regenerated on read, never trusted on import).

```jsonc
{
  "formatVersion": 1,
  "exportedUtc": "2026-07-22T08:00:00Z",
  "source": { "car": "Hurricane", "carConfig": "car02:default", "preset": "CALIB" },
  "requiredParts": [                       // adjustable parts the tune assumes are fitted
    "shared/part/brakes/adjustable_brakes_disc_14",
    "car02/part/gearbox/adjustable_full_6"
  ],
  "tuning": [
    { "paramIndex": 0,  "value": 0.11,  "aux": 11, "name": "Braking Balance", "display": "11 %" },
    { "paramIndex": 40, "value": 2.20,  "aux": 0,  "name": "Gearbox - final drive", "display": "2.20" }
  ]
}
```

Notes:
- **`value` is the physical SI value** — the thing the game actually stores. Never export the UI number.
- **`aux` is the slider position**, and `value` is derived from it —
  `value = min + aux × (max − min) / steps` (min/max/steps from the part's `.ctms`; solved
  2026-07-23, see `PARAM_MAP.md`). Export still carries both verbatim, which stays correct and is
  what makes a tune portable. **Opportunity:** an importer could now re-derive `value` from `aux`
  against the *target* car's schema instead of copying the source's value — the right fix for
  cross-car imports where the two cars' ranges differ.
- **Only non-default values exist** in a preset, so an absent parameter means "leave at default" —
  it does **not** mean zero. Import must respect that distinction.

## 3. Import semantics

| Tier | Action | Size impact | Status |
|---|---|---|---|
| **1** | Overwrite existing records (match `paramIndex`) | none | ✅ implemented |
| **2** | Add records the target lacks | grows | ✅ implemented, verified in-game |
| — | Add/remove whole presets | grows a lot | out of scope for v1 (complexity, not space) |

Default behaviour: attempt Tier 1, and report clearly which parameters were skipped and why.
Never silently drop values — a partially-applied tune the user thinks is complete is the worst
outcome here.

Import targets an **existing preset slot** by name. Creating new presets is deferred.

## 4. Validation

Ordered cheapest-first; refuse rather than guess.

1. **Structural** — `formatVersion`, known `paramIndex` values, finite floats.
2. **Applicability** — does the target car have the adjustable parts the tune needs? A gearbox ratio
   is meaningless on a car with no adjustable gearbox. We have the part → `.ctms` map (166 parts) and
   the fitted-parts list per car, so this is checkable.
3. **Range** — **warn** (never block) when a value is outside the observed stored-unit range for its
   index (`ParamRanges`). Implemented in M5. *Not* against `.ctms`: those bounds are in a different
   unit space and can't be compared to a stored value (see M5).

> **Historical note (superseded):** this section originally planned to clamp/reject against the
> `.ctms` min/max, with the honest caveat that the `paramIndex → .ctms` binding was inferred. M5
> resolved it differently — the `.ctms` bounds turned out to be uncomparable to stored values, so
> validation uses empirical stored-unit ranges instead. The original options are kept below for
> provenance:
> Options: (a) ship with warn-only ranges,
> (b) a short calibration run (set a slider to its min and max, observe the stored values) to bind
> them properly. **Recommend (a) for v1, (b) before any "share tunes with strangers" use.**

Out-of-range values should be **rejected or clamped with a visible warning** — never silently
written, since staying inside the game's limits is an explicit project requirement.

## 5. API sketch (`Wf2Core`)

```csharp
public sealed record PresetExport(int FormatVersion, DateTime ExportedUtc,
                                  PresetSource Source, IReadOnlyList<string> RequiredParts,
                                  IReadOnlyList<TuningExportValue> Tuning);

public static class PresetIo
{
    public static PresetExport Export(CarRecord car, TuningPreset preset);
    public static string       ToJson(PresetExport export);
    public static PresetExport FromJson(string json);          // structural validation
    public static ImportPlan   Plan(SaveFile save, string car, string preset, PresetExport import);
    public static void         Apply(SaveFile save, ImportPlan plan);   // caller then Save()
}

// Plan is inspectable BEFORE writing: what changes, what is skipped, and why.
public sealed record ImportPlan(IReadOnlyList<PlannedChange> Applied,
                                IReadOnlyList<SkippedChange> Skipped,
                                bool TargetIsReadOnly);   // true when the car lives past block 1
```

The **plan-then-apply** split matters: the GUI can show a diff and the CLI can `--dry-run`, so nobody
writes a save without seeing what happens. Given how this format punishes mistakes, that is worth
the extra type.

## 6. CLI

```
wf2 preset export <save> <car> <preset> <out.json>
wf2 preset export-all <save> <outDir>
wf2 preset import <save> <out.sgfi> <car> <preset> <in.json> [--dry-run] [--allow-grow]
```

`--dry-run` prints the `ImportPlan` and writes nothing. Growth requires opt-in.

## 7. Risks

| Risk | Mitigation |
|---|---|
| ~~Cars past block 1 read-only~~ | resolved — multi-block writing verified in-game |
| `paramIndex` → `.ctms` binding unproven | warn-only ranges in v1; calibration before sharing |
| `aux` semantics unknown outside gearbox | carry verbatim, never synthesize |
| Cross-car tunes that don't apply | applicability check via fitted adjustable parts |
| Silent partial import | plan/dry-run + explicit skip reporting |
| User data loss | existing rules: game closed, timestamped backup, write **both** My Games + userdata mirror |

## 8. Decisions taken

1. **Scope of v1:** single-preset export, plus `export-all` for bulk backup. Whole-save *bundles*
   (one file describing many presets) are not implemented — `export-all` writes one file per preset,
   which is simpler to diff and share.
2. **Range validation:** warn-only, as recommended. No range check runs at all today, deliberately:
   the `paramIndex` → `.ctms` binding is inferred, so a check could reject legal tunes. Values that
   came from the game's own sliders are in range by construction. Bind properly before any
   "share tunes with strangers" use.
3. **`requiredParts` is a car-independent role** (`part/gearbox/adjustable_full_6.upgr`), not a full
   path. Every car ships its own copy of the same part under its own directory, so comparing full
   paths reported *every* part as missing on any cross-car import.

## 9. Known behaviour worth not re-discovering

**An import usually changes the file size even though Tier 1 is size-neutral.** The *decoded*
payload keeps its exact length; the file grows because our LZ4 output is larger than the game's —
continuation blocks are re-encoded self-contained (no dictionary), which compresses worse. Observed:
8668 → 9278 bytes with the decoded payload unchanged at 70706. This is expected, and the
size-changing write path is verified in-game.
