# Wreckfest 2 bbag file format — SOLVED (2026-07-22)

**Fully solved and verified end-to-end** — container, chunk chain, multi-block payloads, and reading
and writing of both. Nothing in this spec is speculative.

`profile.sgfi` (saves), `*.upgr` (parts) and `*.ctms` (tuning definitions) are all the **same
container format**. Verified end-to-end: a save rebuilt with these rules loads in-game with all
data intact and the edited value applied.

---

## 1. Container: 20-byte header + LZ4

```
offset  size  field
0x00    u32   RootValue        always 7
0x04    4CC   tag              stored REVERSED: 'ifgs'=sgfi, 'rgpu'=upgr, 'smtc'=ctms
0x08    u32   reserved         varies; preserve verbatim
0x0C    u32   compressedLength = fileLength - 20
0x10    u32   CRC-32C of the DECOMPRESSED payload
0x14    ...   raw LZ4 block (no stored size — decode into a growing buffer)
```

CRC-32C = Castagnoli, poly `0x82F63B78`, init/xorout `0xFFFFFFFF`. (The binary uses the SSE4.2
`crc32` intrinsic with slice-by-N tables — same value, just accelerated.)

4CC tags are little-endian so they read backwards: `ifgs`→sgfi, `racc`→ccar (car), `sdia`→aids,
`rgpu`→upgr, `smtc`→ctms, `forp`→prof.

> **Correction to earlier notes:** header `0x10` is *not* cosmetic. It is a real CRC and must be
> recomputed. The same applies to the per-chunk hashes below, which earlier notes also wrote off.

## 2. The tree is a CHAIN OF CHUNKS  ← the cause of the "strip"

The decompressed payload opens with a small root node, then a run of length-prefixed chunks each
followed by its own checksum:

```
[4CC 'ubas'][u32 kind][u32 chunkCount]        <- root node (12 bytes)
[u32 len1][chunk1][u32 crc1]
[u32 len2][chunk2][u32 crc2] ...              <- chunkCount times
```

- The **length is a PREFIX**, not a suffix. The first chunk's length sits at tree offset `0x0C`,
  inside the root node. (An earlier draft of this doc drew it as `[chunk][crc][len]` — equivalent
  mid-chain, but it hid where the first length lives and implied the wrong parse start.)
- Each **CRC covers from its chunk's start up to the CRC field itself**.
- Each **length = chunkLength + 4** (chunk plus its trailing CRC).
- Each chunk starts with a **nested container** (§1) and may contain further sibling nodes after it.
- `chunkCount` at `0x08` is authoritative — a real save has 4.

A real save has four chunks:

| chunk | container | notes |
|---|---|---|
| 1 | `forp` | profile |
| 2 | `srcc` | **all cars** — multi-block (§7); container block is 65536 B, more follows in the trailer |
| 3 | `sspu` | small |
| 4 | `sdia` | driving aids |

**The cars' chunk CRC sits ~442 bytes past the end of the `srcc` container.** Missing it is exactly
what produced the long-standing "loads but strips every car and tune" symptom.

## 3. Integrity layers — all must be recomputed on edit

1. inner container header CRC — CRC-32C of that container's **decompressed** bytes
2. **every chunk CRC** in the tree (§2)
3. outer header `compressedLength`
4. outer header CRC — CRC-32C of the whole **decompressed tree**

Symptoms: miss #1/#2 → loads but **strips**. Miss a length → `Fatal Error: FS: Failed to Read 4
bytes, because Memory File has only 0 bytes Available`.

> **Do not be alarmed by an unchanged outer CRC.** Because each chunk is immediately followed by its
> own CRC, the running CRC over that region collapses to a fixed residue (CRC-32 linearity). So a
> size-preserving chunk edit with a correctly recomputed chunk CRC leaves the **whole-tree CRC
> unchanged**. Verified: `BACKUP_20260722_012434.sgfi` and `TEST_brake44_v2.sgfi` have different
> trees but the identical outer CRC `0x321b4df7`, and both validate. It looks stale; it is correct.

> **The cars container is NOT the whole cars payload — see §7.** It holds only the first 64 KiB
> block; the payload continues in the chunk trailer. Earlier drafts of this doc claimed the 64 KiB
> was a hard cap and that the game was truncating and losing car data. **That was completely wrong**
> — nothing is lost. The reader was only decoding block 1, so cars past it appeared to vanish.

## 4. Cars, presets and tuning

Inside the `srcc` container: per-car records with `VEHICLE_NAME_*` + display name, a `carNN:config`
string, then full part paths. A preset is `pstv` + name + `stvc`, then an `atvc` value node:

```
atvc [u32 kind=2] [u32 count]   then count × 12-byte records:
     [u32 paramIndex] [u32 aux] [f32 value]
```

- `value` is the **physical value in SI base units** — NOT normalized; the UI converts for display
  (ride height in metres, springs/ARB in N/m, dampers in N·s/m). The game can switch metric/imperial,
  so never treat a UI number as the stored value.
- `aux` is the normalized slider percent for the gearbox (verified exactly on all 7 gear params:
  `value = min + aux/100 × (max−min)` vs `gearbox_6-speed_full.ctms`). Not verified elsewhere —
  e.g. Braking Pressure stores `aux=18` with value 140.
- **Only non-default values are stored**; untouched sliders are absent entirely.
- `atvc` is a tagged union: a non-zero string-length field means the record holds text (a preset
  name), not a number.

Parameter names/units: `PARAM_MAP.md`. Legal ranges: `.ctms` files via `Wf2Core/CtmsFile.cs` /
`wf2 tuning <dir>`.

## 5. Editing rules (learned the hard way)

- **NEVER patch bytes inside a compressed stream.** LZ4 literals get reused as back-reference
  sources by later matches — patching 3 literal bytes silently corrupted 13 decoded bytes in
  testing. This invalidated several earlier "in-place edit" experiments. Always
  decompress → edit → recompress.
- **Size-neutral edits are safest.** If the recompressed container is exactly the original size, no
  chunk length changes (only CRCs). Searching edit-value × LZ4 level for an exact size match is a
  practical trick for single-value edits.
- **Variable-size writes are VERIFIED (2026-07-22).** When a rebuild changes a container's size,
  the enclosing chunk's length shifts by the same delta (`len = chunkLength + 4`). Proven in-game:
  a `settune` edit shrank the save 8668 → 8636 bytes (srcc container and chunk length both −36) and
  the game loaded it with the edited value applied and all 20 cars/presets intact. This is the
  normal path — our LZ4 compresses better than the game's, so real edits change size.
- Our LZ4 output is accepted: a zero-edit decompress→recompress round-trip loaded normally.

## 6. Part catalog (earlier findings, still valid)

- Loose files on disk, no archive parsing: `<install>\data\vehicle\{carNN,shared}\part\<category>\<variant>.upgr`
  — 19 cars, ~1734 `.upgr` files.
- `.upgr` files decompress to clean, fully-literal strings: localisation key, display name, icon
  path, description ("Affects: Brake balance."), referenced meshes, and an `smtc` reference to a
  `.ctms` tuning definition **only for adjustable parts** — precisely what gates tuning in the garage.
- Brake `.upgr` variants (disc 11/12/14, drum 12/14) are numerically identical; they differ only in
  name, icon and mesh references. Braking performance is not defined in these files.
- The old `s/cooling` puzzle was prefix compression: the real path is
  `.../engine/stock/parts/cooling/...` (`part` + `s` = `parts`).


## 7. Multi-block payloads (the cars payload)

A payload larger than 64 KiB is split into **linked LZ4 blocks**. The first lives in the container;
each subsequent block follows in the chunk trailer, framed:

```
[u32 compressedLength][u32 CRC-32C of THIS block's decompressed bytes][compressed bytes]
```

**Blocks are not independently decodable.** Later blocks contain LZ4 back-references into the
*decompressed output* of earlier ones (LZ4 "linked block" mode). Decoding block 2 on its own fails
outright. It must be decoded with the accumulated previous output supplied as an **LZ4 dictionary**
(`LZ4_decompress_safe_usingDict` semantics: the dictionary sits immediately before the output so
match offsets reach back into it).

> **Do NOT decode by concatenating compressed blocks.** It looks like it works and yields plausible
> output, but LZ4 blocks are self-terminating (the final sequence must be literals), so the previous
> stream cannot simply be continued. The result is silently misaligned: in testing it produced 4957
> bytes instead of the correct 5190, with corrupted strings like `car16/p2rt/temp/driver.upivery`.

**Worked example** (`BACKUP_20260722_012434.sgfi`):

| | compressed | decompressed | CRC |
|---|---|---|---|
| block 1 (container) | 7796 | 65536 | header `0x12318980` ✓ |
| block 2 (trailer) | 448 | 5190 | framing `0xec1f4db5` ✓ |
| **total cars payload** | | **70726** | |

Reading block 1 alone yields a complete-looking 64 KiB prefix **whose CRC validates**, so the
omission is silent — this is exactly how the car "Jackal" (with parts and three presets) appeared to
be missing from the save while being present in the player's garage the entire time.

**Implementation:** `Lz4Block.DecodeWithDictionary` (a direct LZ4 block decoder — `K4os` does not
expose `usingDict`), driven by `SaveChunk.DecodedPayload`, which verifies each block's CRC and
refuses to surface a block it cannot verify. Regression test:
`SaveFileTests.Cars_IncludesCarsStoredInContinuationBlocks`.

**Writing (VERIFIED in-game 2026-07-22):** `SaveChunk.SetDecodedPayload` re-splits the payload into
64 KiB slices — first into the container, the rest into the trailer — recomputing every block CRC and
framing length. Cars in continuation blocks are fully editable.

Continuation blocks are written **self-contained** (compressed without a dictionary). The game
decodes them *with* the previous output as a dictionary, which is harmless: a self-contained block
never emits back-references reaching into the dictionary, so it decodes identically either way. This
avoids needing LZ4's `compress_usingDict`, which `K4os` does not expose; the only cost is a larger
block than the game would emit (1581 B vs 448 B in testing).

Proven end-to-end: editing Braking Balance 60% → 77% on a preset belonging to a car stored in
**block 2** produced a save the game loaded with the value applied and all 21 cars intact. Exactly
4 payload bytes changed, at offset 69650 — inside block 2.
