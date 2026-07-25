# Wf2Core.Tests

xUnit acceptance tests for `Wf2Core`. These are the **safety net for a format we reverse-engineered
by trial and error** — several conclusions in this project's history turned out to be wrong, and the
tests exist so that doesn't happen silently again.

```powershell
dotnet test
```

## What to assert on — read this before writing a test

**Do not assert byte-identical output for anything that recompresses.**
A parse→serialize round-trip is byte-identical *only* when the compressed payload is preserved
verbatim. A writer that recompresses legitimately produces different bytes than the game's, because
our LZ4 encoder isn't theirs — this was **confirmed fine in-game**. For those paths assert:

1. every CRC in the output validates (outer, each chunk, each nested container), and
2. the **decoded logical content** matches expectations.

A test demanding raw-byte equality from a recompressing writer will fail on correct code.

## Fixtures

Real game-written saves live in `fixtures/`. Two matter most:

| Fixture | Role |
|---|---|
| `BACKUP_20260722_012434.sgfi` | untouched, game-written save — 20 cars, presets intact |
| `TEST_brake44_v2.sgfi` | **correctness oracle**: a save *we* rebuilt that the game **accepted and loaded intact** |

The pair differs by exactly one edit: Hurricane → preset `Preset 1` → the `paramIndex 0`
(Braking Balance) record, `aux 19 / value 0.19` → `aux 44 / value 0.44`.

That makes the strongest available test: *apply that edit to the backup, serialize, and confirm the
result decodes to the same content as the oracle with all CRCs valid.* It proves the writer against
**real in-game acceptance**, not just internal self-consistency.

Treat fixtures as read-only. Never point a test at the live save.

## Coverage worth keeping green

- header parse + length invariant, CRC-32C over the decompressed payload
- chunk-chain walk: all chunk CRCs and lengths validate on a real save
- enumeration: 21 cars, Hurricane presets (`Preset 1`, `CALIB`, `Hybrid_`), record counts
- no-op round-trip: content preserved, CRCs valid
- **edit → serialize → matches the oracle** (the key test)
- **multi-block cars payload** — 21 cars incl. "Jackal", which lives only in a continuation block
  (`Cars_IncludesCarsStoredInContinuationBlocks`). A test asserting 20 cars once encoded this bug.
- `.ctms` decode: 15/15 files, declared parameter count == parsed count, CRC valid
- part catalog; fitted parts (`CarPartsTests`) — every path complete and well-formed, including
  for cars in continuation blocks
- preset export/import (`PresetIoTests`) — JSON round-trips with exact float equality, identity
  import plans zero changes, cross-car import keeps every CRC valid and all 21 cars, and every
  value lands in either Applied or Skipped
- safe-write refuses when the game is running

## Gaps to be honest about

- Variable-size writes are now **verified in-game** (2026-07-22). Still, remember the general
  principle: a green test here proves internal consistency, not that the game accepts the file.
  Anything genuinely new should get one real in-game confirmation.
