# Progress Tracker

> ## ⚠️ Milestone table below predates the 2026-07-22 solve.
> The save format is now fully solved and writing is verified in-game — see `README.md` and
> `docs/format.md`. The A/B/C milestones here describe the original offline-editor plan and were
> overtaken by events. **The Changelog further down is still valuable history**; the status table is not.
>
> Note especially: Milestone 0.2's "0x10 not validated / cosmetic" conclusion is **WRONG**
> (it is a CRC-32C). That error is corrected in `docs/format.md`.

Living status for the Wreckfest 2 Save Editor. Spec is in [PLAN.md](PLAN.md).

**How to use this file (for humans and agents):**
- Update the status of a test the moment it flips. Statuses: `⬜ todo`, `🟡 in-progress`,
  `✅ pass`, `❌ fail`, `🚫 blocked`, `⏭️ n/a`.
- A **milestone** flips to ✅ only when *all* its tests are ✅ (empirical gates included).
- Add a dated line to the **Changelog** for every status change or notable finding.
- Keep the **Open questions / blockers** list current — this is what a fresh agent reads first.
- Don't delete history; strike through or move to Changelog.

_Last updated: 2026-07-18_

---

## Milestone summary
| Milestone | Description | Status |
|---|---|---|
| **0** | Header field identified (write gate) | ✅ complete — but see banner: `0x10` **IS** a CRC-32C, not cosmetic |
| **A** | Safe byte-identical round-trip | ✅ **complete** (round-trip + safe write + in-game accept) |
| **B** | Edit upgraded parts | 🟡 next — needs body node-grammar decode |
| **C** | Edit tuning presets | ⬜ todo |

## Test checklist
### Milestone 0 — Header field identified
- [x] **0.1** Length field: `int32@0x0c == fileLength − headerConstant` for 100% of fixtures — ✅ (`HeaderTests.T0_1`; headerConstant = 20)
- [x] **0.2** Checksum semantics — ❌ **THIS CONCLUSION WAS WRONG.** ~~save with altered `0x10` loaded fine → not validated → cosmetic~~. `0x10` **IS a CRC-32C of the decompressed payload and MUST be recomputed** (see `docs/format.md`). The probe that "loaded fine" was testing the Steam cloud mirror, not the file the game reads.
- [x] **0.3** *(only if 0.2 shows it is validated)* — ⚠️ was marked N/A on the strength of 0.2's wrong conclusion; `0x10` **is** validated.

### Milestone A — Safe byte-identical round-trip
- [x] **A.1** Round-trip identity: `Serialize(Parse(x)) == x` byte-for-byte, all fixtures — ✅ (`RoundTripTests.A1`; also CLI `roundtrip`)
- [x] **A.2** Safety interlocks: write blocked when game/Steam running; backup created — ✅ (`SafeWriteTests`)
- [x] **A.3** *(empirical gate)* Game boots with re-saved profile, nothing changed — ✅ **2026-07-18**: File A (byte-identical) loaded fine; restore confirmed normal.

### Milestone B — Edit upgraded parts
- [ ] **B.1** Catalog sanity: 0 unresolved fitted parts across fixtures — ⬜
- [ ] **B.2** Targeted-edit round-trip: diff confined to edited node + header — ⬜
- [ ] **B.3** *(empirical gate)* Two slot edits show correctly in-game, nothing else changed — ⬜

### Milestone C — Edit tuning presets
- [ ] **C.1** Mapping locked: `(slider, offset, min, max, unit)` matches in-game reading — ⬜
- [ ] **C.2** Encode/decode inverse + range clamping — ⬜
- [ ] **C.3** Isolated-write round-trip: diff confined to edited float + header — ⬜
- [ ] **C.4** *(empirical gate)* Distinctive tuning value shows in-game — ⬜

---

## Fixtures collected
| File | Source / state | Notes |
|---|---|---|
| `remote_20260718_023435/profile.sgfi` | backup of live save, 7262 B | first snapshot; baseline |

_(Add each new before/after snapshot here as it's captured.)_

## Open questions / blockers
- **Body node-grammar (Milestone B)** — CONFIRMED equipped parts = path strings under `rgpu`, and
  the game reads them from the string (no stat-byte catalog needed — Exp #3). Remaining work: a
  structural parser that locates each slot's string and updates **enclosing length fields** when a
  new part name has a different length (string len → `rgpu` size → `racc` size → payload len 0x0C).
  See `docs/format.md`. May need one variable-length in-game sample to confirm length propagation.
- **Valid part-name list** — need the set of legal part path strings per slot (harvest from `.rpck`
  or accumulate from saves) to populate the editor's dropdowns.
- **`.rpck` archive format** — unknown; affects Part 1 catalog extraction (string-harvest fallback exists).
- **Tuning float layout** — offsets/scale per slider unmapped (Part 2).
- ~~**`0x10` header bytes**~~ — ❌ that resolution was WRONG; it is a CRC-32C (see `docs/format.md`).

## Changelog
- **2026-07-20** — **STEP-BACK found a model-breaking anomaly.** Forensic check: `test_stringswap.sgfi`
  (the ONE edit that worked in-game) has a **MISMATCHED CRC** (stored 0x627B9D75 vs actual CRC32C(tree)
  0x4DAE1969) — yet it loaded, showed the edited radiator, and did NOT strip. So the CRC is NOT always
  enforced, and vehicle edits do NOT always strip. The difference: `test_stringswap` was deployed via
  **Steam Cloud** (edit userdata\remote, Cloud ON); every edit that stripped/errored was deployed
  **directly to My Games** (Cloud OFF). Hypothesis: Cloud-delivered saves load leniently (no CRC
  enforcement, no tamper-strip); direct My Games edits trip a consistency/tamper reset. **We may have
  abandoned the working method.** Built `deploy-cloud.ps1/.bat` (targets userdata mirror) + `test_cloud.sgfi`
  (current save, brake 0.85, CRC fixed). Re-testing the Cloud path next. Confirmed:
  framework uses FNV-1a-64 (prime 0x100000001b3, basis 0xcbf29ce484222325) for NAME hashing; only 2
  funcs use the `crc32` insn (both whole-file). Traced read-record subtree (`FUN_1409bc960`, 45 fns) —
  NO per-record hash there. Save side (`FUN_1409c3b20/4310/4d60`) only writes the whole-file CRC. The
  per-record value (racc field, content-derived) is computed in the recursive OBJECT (de)serializer,
  and is NOT crc32c/crc32/adler/xxhash/murmur/fnv over the record bytes (brute-forced all, both field
  mappings). Also verified atvc+0xA IS the brake float (not structural). Open possibilities: custom
  hash deep in the serializer, OR the atvc variable-length encoding for 0.25 differs from the game's
  (structural). Genuinely hard wall after extensive effort. Ghidra project + scripts at D:\wf2_re.
- **2026-07-20** — **Ghidra route (12.1.2 + JDK21) — header CRC fully confirmed, per-record hash still open.**
  Decompiled the bag-loader (`FUN_1409c3e80`): reads `[u32 size][u32 expected_crc]`, LZ4-decompresses,
  checks `CRC32C(buffer)==expected` → **exactly our finding** (header `0x10`). Traced up: it's Bugbear's
  generic **bbag** framework (`FUN_1409bb8f0` = open/read-header → `FUN_1409bc790` format dispatch →
  bag-loader). Only 2 functions in the exe use the `crc32` instruction (both whole-file), so the
  per-record hash (the 3 `f3`-node values: urve/racc/nart) is **custom + inlined** in the framework's
  record reader — not reproducible via CRC/xxHash/Murmur/Adler brute-force over any range. Cracking it
  needs deeper tracing of the recursive record deserializer, with real risk it hashes the in-memory
  object (not file bytes) → possibly impractical for a file editor. CHECKPOINT. Tools at D:\wf2_re.
- **2026-07-20** — Per-record layer mapped. Only **3 hashed nodes** in the tree: `urve`, `racc`
  (the vehicle record = all cars + presets), `nart`, each framed `[u32 hash][f3 XX][tag]`. Editing
  anything inside `racc` breaks its hash → game strips ALL vehicle data (explains the global strip).
  The node hash is NOT crc32c/crc32/adler/xxh32/mm3/xxh64 over racc..nart ranges, and NOT computed by
  the CRC fn (all 5 sites whole-file) → it's **inlined in the deserializer**. Traced bag-loader
  (0x1409c3e80) → caller dispatcher (0x1409c4714); deserializer is deeper. Cracking the node hash
  needs tracing/decompiling the deserializer (bigger, more open-ended than the header CRC). CHECKPOINT:
  header-CRC win stands (game loads edits); node-hash is the last layer.
- **2026-07-20** — **Header CRC fix WORKS — game loaded the edited save (no IOE_35).** But it stripped
  all cars' parts + tuning presets ⇒ a SECOND, per-record integrity layer discards records whose
  own field is stale. The per-record `racc` field (0x63719C5F on RESTORE) changes with content but is
  NOT plain/prefix/running CRC-32C of any contiguous range (tested). No per-record call to the CRC fn
  in the loader (all 5 sites are whole-file). Next: trace the tree PARSER (separate fn from load+CRC)
  to find how records are validated. RESTORE_realsave verified self-consistent (0x10==CRC32C(tree),
  has parts) → safe restore point. Header-checksum breakthrough stands; per-record layer is the last piece.
- **2026-07-20** — ★★★★ **CHECKSUM CRACKED via disassembly (route 2).** `Wreckfest2.exe` is 22MB, no
  DRM, plaintext strings. Located the load path via `Bag IO Error`/`loadgame` string xrefs (capstone,
  no Ghidra needed): after LZ4 decompress, `call 0x140993070; cmp eax,[rbp+0x48]; jne error`. The
  checksum fn dispatches to `0x140992bc0` = **CRC-32C** (SSE4.2 `crc32` insn, `not ecx` init=0xFFFFFFFF,
  triple-parallel folding). **VERIFIED: `CRC32C(decompressed tree) == header field @0x10`** exactly
  (0x8383CDAD on RESTORE). So `0x10` (thought cosmetic) IS the content checksum — Milestone 0.2 was
  wrong (tested on the mirror). **FIX: edit tree → write CRC32C(tree) to 0x10.** Regenerated in-place
  `test_tuning.sgfi` (brake 0.25 + fixed CRC, self-consistent). Awaiting deploy → if it loads+25%, ALL
  editing is unlocked (tuning now, parts next). ~90% autonomous as predicted.
- **2026-07-20** — **FINAL BLOCKER identified: custom per-record integrity checksum.** In-place edit
  (2 literal bytes, valid LZ4, correct file) STILL → IOE_35. Located the check: each major record is
  framed `[u32 checksum][f3 XX][tag]`; the `racc` (car) checksum changed with the brake edit
  (0%=0x93B8EAB1, 100%=0x688726F1). Swept CRC32/CRC32C/Adler32/FNV1/1a/sum/xor/djb2/sdbm/murmur3 over
  all ranges near racc on two known-good saves → **NO match** ⇒ custom/seeded hash. Reproducing it
  needs the game's serializer code (Ghidra disassembly of Wreckfest2.exe). Black-box RE is exhausted
  for WRITING. (Note: `test_stringswap` part edit once loaded OK — parts may be less strictly checksummed
  than tuning; unconfirmed.) Reader/inspector side is complete & solid. Diagnosis: the game's bag reader **rejects our recompressed LZ4 stream** (standard
  lz4 lib reads it fine, game's stricter decoder doesn't). ⇒ **Do NOT recompress. Edit bytes IN PLACE
  in the game's original compressed stream** (same length, literal bytes only) — the mechanism
  test_stringswap used. Confirmed brake float bytes are literals in the stream (compressed pos 734-737).
  Generated in-place `test_tuning.sgfi` (2 bytes changed, same size) from the good save. This also
  reframes: our C# `SetTree` (recompress) is unusable for writing; need an in-place byte-editor. Awaiting
  in-game result (loads+25% = win; IOE_35 again = also a content checksum).
- **2026-07-20** — ★★★ **ROOT CAUSE of all "edit ignored" failures found.** There are TWO profile.sgfi:
  the game READS `Documents\My Games\Wreckfest 2\76561198026287214\savegame\profile.sgfi`; the Steam
  `userdata\...\remote\profile.sgfi` we'd been editing is only the **Cloud mirror**. With Cloud ON,
  Steam bridged them (why `test_stringswap` once worked); with Cloud OFF, our deploys to userdata were
  orphaned — the game kept reading the untouched My Games copy. PROOF: userdata atvc+0xA=0.25 (my edit)
  vs My Games=1.003 (untouched). **We may have never validly tested any edit.** Fixed deploy/extract
  scripts to target My Games. Backed up real save (`MYGAMES_profile.realsave.bak.sgfi`). Re-testing the
  tuning float (0.25) against the CORRECT file next — this may un-break parts AND tuning.
- **2026-07-19** — **TUNING LOCATED (Milestone C).** Brake-balance experiment (0%→100% front) → tuning
  presets live in the `atvc` block (inside `pstv`/"Preset") in the car record, as **raw float32** (no
  strings, no interning!). Brake balance @ `atvc+0xA`: 100% front = `1.00107`, 0% = stored compact
  (default omitted → variable-length). **KEY:** an already-set tuning value is a plain 4-byte float,
  editable IN PLACE (no shift, no dictionary). Generated `Desktop\test_tuning.sgfi` (100%→0.5, only
  the float changed) — AWAITING in-game validation that it shows ~50% front. If good, tuning editing
  is straightforward. Backups current (`profile.20260719_010154.played.bak.sgfi` + auto on deploy).
- **2026-07-19** — Idea-2 (de-intern) deploy test **FAILED**: edited `racing_high_flow`@0x316 →
  `derby_reinforced`; game ignored it and re-saved byte-identical to pre (POST==PRE). Diff of
  derby-equipped vs racing-equipped saves shows WHY: the equipped-part name is stored **as a literal
  when new to the dict, or as a 3-byte reference when already interned** — 0x316 was a non-authoritative
  copy. Same-length swap only works when the slot is stored literally (not controllable). Reliable
  arbitrary part editing needs the literal↔reference/dictionary system fully cracked (disassembly-level)
  OR a reference→literal rewrite + payload-len fix (untested). **Recommend pivoting to Part 2 (tuning,
  numeric) which sidesteps all of this.** New backup: `profile.20260719_010154.played.bak.sgfi`.
- **2026-07-18** — Step-back review paid off. (1) LZ4 independently confirmed = Bugbear "bag" format
  (community tools). (2) **Part catalog is loose files on disk** — built `PartCatalog` (walks
  `data\vehicle\{carNN,shared}\part\**\*.upgr`), CLI `catalog`, tests (29/29). Verified vs real data
  (19 cars, ~1734 parts). (3) `s/cooling` mystery solved = `.../parts/cooling`. (4) Generated Idea-2
  deploy test via the full C# pipeline (`Desktop\test_writer.sgfi`): de-intern edit
  `racing_high_flow`→`derby_reinforced` (single occurrence) → should flip the equipped radiator to
  derby. AWAITING in-game validation (tests C# decompress→edit→LZ4HC recompress + de-intern approach).
- **2026-07-18** — Parsed decompressed tree structure + built same-length part-swap primitive
  (`PartEditor.SwapSameLengthPart`, CLI `setpart`, tests 26/26). **BUT discovered a second, INNER
  string-dictionary inside the tree** (under LZ4): part names are stored contiguously only when
  freshly set; otherwise split into a reference (e.g. live radiator = `s/cooling`+ref+`_radiator.upgr`).
  So contiguous find/replace only works on literally-stored names — the live-save swap test failed
  for this reason. Robust arbitrary editing needs the inner literal slot-entry format decoded (write
  fresh literals) — the next RE layer. Details in `docs/format.md`.
- **2026-07-18** — **LZ4 layer implemented in C#** (`K4os.Compression.LZ4`). `SgfiFile.DecompressTree()`
  / `SetTree()` (LZ4HC); `LoadoutReader` now reads the decompressed tree; CLI `decompress` command;
  `Lz4PayloadTests` (decompress + recompress round-trip). **22/22 green.** Loadout now shows full
  paths (e.g. air_filter's `data/vehicle/shared/part/...`); remaining `~` fragments are node-structure
  artifacts to resolve by parsing the tree (no longer compression). Next: parse the decompressed tree
  node structure for clean names + exact slot boundaries, then the writer (edit tree → SetTree).
- **2026-07-18** — ★★ **BREAKTHROUGH: the payload is a raw LZ4 block** (header 20 bytes + LZ4 from
  `0x14`; game uses LZ4HC). Confirmed via standard lz4 lib (identical to hand-decoder), clean EOF on
  all 4 captures, HC recompress to original size, and round-trip decode==tree. **This dissolves the
  "compression wall"** — the editor decompresses → edits the plain tree → recompresses. The earlier
  "custom string compression" was just the compressed view. Next: LZ4 layer in C# (K4os NuGet) +
  re-target the loadout decoder at the decompressed tree.
- **2026-07-18** — **Stage 2 (token decoder) — hit a static-analysis wall.** From the derby↔racing
  captures: NO nested byte-length field changes on a part edit — only payload len `0x0C` (good sign
  the writer is simple). BUT the per-string length encoding is not derivable from current samples
  (no clean length prefix, no fixed terminator; compressed names use dictionary back-refs). Blocker:
  how the game marks a part string's end. Targeted capture queued: remove the air filter (a full
  literal → `stock`) to expose the length encoding cleanly. `airfilter_before.sgfi` captured.
- **2026-07-18** — **Decoder Stage 1 (best-effort reader) built.** `Wf2Core/LoadoutReader.cs` locates
  the `rgpu` block and scans out fitted parts, classifying all 18 RoadSlayer slots to canonical
  categories (full paths clean; LZ-compressed names flagged as fragments). CLI `loadout` command +
  `LoadoutReaderTests` (17/17 green). Fixture `profile_derby.sgfi` added. Next: Stage 2 = true token
  decoder (expand literals + back-refs) to replace the heuristic and get whole part names.
- **2026-07-18** — **Experiment #4:** radiator derby→racing (−12 B). Slot re-encoded as
  literal `s/cooling` + back-reference + literal `_radiator` → **string table is LZ/dictionary-
  compressed** (repeated substrings referenced, not repeated). Editor plan set: build a DECODER
  (expand literals+backrefs to read the loadout) and a **literal-only ENCODER** (emit edited names
  uncompressed + fix enclosing lengths; game re-compresses on its next save). Captures now handled
  by Claude directly from the live save (`D:\projects\wreckfest\captures\`). Details in `docs/format.md`.
- **2026-07-18** — **Experiment #3 DECISIVE.** In-place string swap (radiator name only, trailer left
  stale) → game displayed the NEW name → **the game reads equipped parts from the path string; the
  data bytes are a regenerated cache.** Part 1 hugely simplified: editing a part = writing the path
  string; **no per-part stat/ID catalog needed**, only the list of valid part names. Remaining work:
  handle variable-length name changes (update enclosing length fields) via the structural parser.
- **2026-07-18** — Diff experiment #2: clean radiator swap (`racing_high_flow`→`derby_reinforced`).
  **CONFIRMED** equipped parts live as path strings under `rgpu` (this is the Milestone B target).
  **CONFIRMED** each part carries part-specific control+trailer bytes (stats/ID) — swapping needs the
  correct bytes, not just the string — and swapped-out parts move to an owned/inventory list keeping
  their encoding. ⇒ two Part 1 paths: (a) full catalog from `.rpck`, or (b) MVP "equip a part you
  already own" (move existing byte-blocks, no catalog needed). Details in `docs/format.md`.
- **2026-07-18** — Diff experiment #1 (before/after). Sequence-aligned. Found pervasive per-save
  noise fields (1–4 B after each record tag, like the 0x10 field) + `rgpu` block relocation with
  pointer-like control bytes. BUT no part value changed — air_filter still `sport.upgr` in both;
  capture recorded a car-switch (`car03`→`car01`), not a part swap. Need a clean re-capture with a
  confirmed value-changing edit. Details in `docs/format.md`.
- **2026-07-18** — Static analysis of the body node tree → `docs/format.md`. Found: payload is a
  reversed-FourCC node tree; the `rgpu` (=`upgr`) block holds fitted parts, preceded by `u32=18`
  (slot count). Parts are asset paths ending `.upgr` or the literal `stock`, and **strings are
  prefix/dictionary-compressed** (only one full path + one `stock` in plaintext). ⇒ editing a part
  needs a re-encoder, not a byte patch. Defined the single in-game diff experiment to crack the
  control-byte codec (Milestone B).
- **2026-07-18** — **Milestones 0 and A COMPLETE.** In-game result: both File A (identical) and
  File B (`0x10` altered) loaded fine → `0x10` is not validated → cosmetic, preserved verbatim.
  **[Later corrected: this probe used the Steam cloud mirror, not the real save. `0x10` IS a CRC-32C.]**
  Updated `SgfiFile` docs + `RecomputeDerivedFields` (updates PayloadLength, leaves 0x10). Added
  `EditHeaderTests`. 11/11 tests green. Next: decode body node-grammar for Milestone B (parts).
- **2026-07-18** — Added CLI `maketests`; generated Milestone 0.2/A.3 probe files into
  `Documents\Wreckfest2 Backups\header_test_20260718\` (File A byte-identical control; File B
  `0x10`→`0x9D84628A`, body untouched; `RESTORE_original.sgfi`; `README.txt` with the exact
  swap/restore procedure). Verified A == source and A vs B differ only at 0x10–0x13. Awaiting
  the in-game session result to resolve 0.2 (and confirm A.3).
- **2026-07-18** — Scaffolded C# solution (`Wf2Core`, `Wf2Cli`, `Wf2App` WPF, `Wf2Core.Tests`).
  Implemented `SgfiFile` (20-byte header typed, body retained raw) + safe-write pipeline
  (`SaveWriter` + `ISystemState` game/Steam guard). **0.1 ✅, A.1 ✅, A.2 ✅** — 8/8 tests green,
  byte-identical round-trip verified on the live save. Header: `RootValue@0x00=7`,
  `PayloadLen@0x0C = size-20`, `Field10@0x10 = 0x627B9D75` (still unknown → 0.2). Body grammar
  not yet decoded. Remaining for Milestone 0/A: in-game gates 0.2 and A.3.
- **2026-07-18** — Located install, save (`profile.sgfi`), and Steam cloud manifest.
  Confirmed parts + tuning both live in `profile.sgfi`. Identified reversed-FourCC node-tree
  format and prefix-compressed part paths. Header `0x0c` = payload length; `0x10` = unknown
  changing field. Backup taken. PLAN/PROGRESS/AGENTS created. All milestones ⬜.
