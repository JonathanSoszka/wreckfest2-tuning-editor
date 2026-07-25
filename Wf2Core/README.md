# Wf2Core

The format library. **All reverse-engineering and binary logic lives here** — no UI, no console I/O.
`Wf2App` and `Wf2Cli` are thin consumers.

`net8.0` · nullable enabled · **`TreatWarningsAsErrors=true`** · single dependency: `K4os.Compression.LZ4`.

> Read [`docs/format.md`](../docs/format.md) before changing anything in here. It is the
> authoritative spec and it wins over any comment, doc, or older code that disagrees.

## Layout

| Area | Files | Purpose |
|---|---|---|
| Container | `BbagContainer.cs`, `Lz4Block.cs`, `Crc32C.cs` | The 20-byte header + LZ4 + CRC-32C primitives shared by **all** bbag files (`.sgfi`, `.upgr`, `.ctms`), incl. dictionary decoding for linked blocks |
| Save | `SaveFile.cs`, `SaveChunk`, `SaveWriter.cs` | Parse/serialize `profile.sgfi`, walk the chunk chain, recompute integrity |
| Cars & tuning | `CarCollection.cs`, `CarRecord`, `TuningPreset`, `TuningRecord` | Cars → presets → tuning records |
| Presets | `PresetIo.cs`, `ParamMap.cs` | Export/import tunes as JSON; plan-then-apply so nothing is written unseen. `ParamMap` is the `paramIndex` → name/unit table from `docs/PARAM_MAP.md` — informational only, edits key off the numeric index |
| Tuning schema | `CtmsFile.cs`, `TuningParameter` | `.ctms` definitions = the **legal min/max** for every tunable parameter |
| Parts | `PartCatalog.cs`, `GuideExporter.cs` | Catalog of installable parts from the game's loose `.upgr` files. Parts *fitted to a car* are read from `CarRecord.Parts` |
| Safety | `ISystemState.cs`, `ProcessSystemState.cs`, `GameRunningException` | "Is the game running?" gate for safe writes |

## The rules that matter

**1. Four integrity layers must be recomputed on every write.**
Miss the CRCs → the game *loads but silently strips every car and tune*. Miss a length → a fatal read
error. There is no partial-credit failure mode; see `docs/format.md` §3.

**2. Never patch bytes inside a compressed LZ4 stream.**
Literals are reused as back-reference sources by later matches — patching 3 bytes silently corrupted
13 in testing and invalidated several experiments. Always **decompress → edit → recompress**.

**3. Tuning values are physical, in SI units.**
Not normalized, and not the number the game's UI shows (which also switches metric/imperial).
Ride height is metres, springs/ARB are N/m, dampers N·s/m. Mapping: [`docs/PARAM_MAP.md`](../docs/PARAM_MAP.md).

**4. Presets store only non-default values.** An absent parameter means "default", not zero.

**4a. The cars payload spans multiple LZ4 blocks.** `BbagContainer.Content` is only the *first*
block — a complete-looking 64 KiB prefix whose CRC validates. Always read
`SaveChunk.DecodedPayload`, which decodes continuation blocks with the previous output as a
dictionary. Never concatenate compressed blocks; that silently misaligns. Write through
`SaveChunk.SetDecodedPayload`, which re-splits into blocks and recomputes every block CRC;
continuation blocks are written self-contained, which the game accepts (verified in-game).

**5. Stay account-agnostic.** No hardcoded Steam IDs or user paths in this library — callers supply
paths. This is what lets the project generalize beyond one machine.

## Testing note

A parse→serialize round-trip is byte-identical **only** when the compressed payload is preserved
verbatim. Any path that **recompresses** legitimately produces different bytes than the game's
(our LZ4 isn't theirs — confirmed fine in-game). For those, assert on **decoded logical content and
valid CRCs**, never on raw bytes.

## Write path status

**Fully verified in-game (2026-07-22)** — size-changing writes *and* multi-block writes. An edit that
shrank the save 8668 → 8636 bytes loaded correctly; so did an edit to a car living in **block 2** of
the cars payload (Braking Balance 60% → 77%, 4 bytes changed at offset 69650), with all 21 cars
intact. Size-changing writes are the *normal* path, since our LZ4 compresses better than the game's.

Still true: get a CRC wrong and the game loads but silently strips every car; get a length wrong and
it dies with a fatal read error. The tests exist to keep both honest.
