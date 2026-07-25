# Wreckfest 2 Editor — Progress & Handoff

> **STATUS 2026-07-22: THE FORMAT IS SOLVED AND WRITING WORKS.**
> A save rebuilt by our own code loaded in-game with all 20 cars and every preset intact, and the
> edited value applied. The years-long "strip" was **CRC-32C over a chunk range we had mis-located**
> — not an exotic field-walk hash. See `format.md` for the authoritative spec and `PARAM_MAP.md`
> for tuning parameter names/units.
>
> Sections 3-5 below are kept as a record of the investigation. Where they say the record hash is
> unreproducible, or that memory editing is the way forward, **they are superseded** by `format.md`.


_A self-contained review document. Written 2026-07-20. Companion to `PLAN_runtime.md`,
`PROGRESS.md` (running changelog), `docs/format.md`, and the memory files
`record-hash-is-the-wall.md` / `runtime-editor-pivot.md`._

---

## 1. Goal

Build a Windows desktop app (C#/.NET; solution `Wf2SaveEditor.sln` = `Wf2Core` lib, `Wf2Cli`
console, `Wf2App` WPF, `Wf2Core.Tests`) that lets the user edit their car **upgrade parts**
(Part 1) and **tuning presets** (Part 2) more easily than the in-game UI allows. Personal-first but
generalizable. **Scope correction:** exceeding the in-game caps is explicitly OUT of scope — the
goal is easier tuning + preset export/import within legal values (see `README.md`). `net8.0`, nullable on, warnings-as-errors.

Two strategic approaches have been pursued:
- **Approach A — offline save-file editing.** Blocked by a per-record integrity hash (§4).
- **Approach B — runtime memory editing (current).** Premise validated, but blocked on isolating
  the authoritative in-memory value the game's Save reads (§5).

---

## 2. Save file format (SOLVED)

Path the game reads: `Documents\My Games\Wreckfest 2\76561198026287214\savegame\profile.sgfi`.
Steam Cloud mirror: `…\Steam\userdata\66021486\1203190\remote\profile.sgfi`; state in
`remotecache.vdf` (SHA-1/size/timestamps).

**One uniform container for ALL bbag files** (SOLVED 2026-07-21 — `.sgfi` saves, `.upgr` parts,
`.ctms` tuning defs are the same format):
`[u32 RootValue=7][4CC tag][u32 reserved][u32 compressedLength][u32 CRC-32C of DECOMPRESSED payload][LZ4 block]`
Tags are stored **reversed** (`ifgs`=sgfi, `rgpu`=upgr, `smtc`=ctms, `racc`=ccar, `sdia`=aids).
`.upgr`/`.ctms` payloads are LZ4 too — what looked like "bbag variable-length control bytes" were
just LZ4 tokens; decompressed, the data is clean 4-byte-aligned records. Verified: CRC-32C matches
on 15/15 `.ctms`. Decoder: `Wf2Core/CtmsFile.cs` + `Crc32C.cs`, CLI `wf2 tuning <dir>`.

**Container:** 20-byte header + raw-LZ4 block (Bugbear "bbag" serializer, magic `ifgs`).
Header (LE): `0x00` RootValue=7 · `0x04` "ifgs" · `0x08` reserved=0 · `0x0C` compressed length
(= fileLen−20) · `0x10` **CRC-32C of the decompressed node tree** (poly `0x82F63B78`, init/xorout
`0xFFFFFFFF`; the binary uses the SSE4.2 `crc32` intrinsic + slice-by-N tables at
`DAT_1413265xx…1413281xx`). LZ4 raw block: C# `K4os.Compression.LZ4` (`L12_MAX`), Python
`lz4.block`. Decompress hint used: `uncompressed_size=0x80000`.

**Tuning (`atvc` block).** Per parameter: `…02 26 02 11 [percentByte] 06 00 [float32]…`.
- `percentByte` = the exact UI percentage: `0x37`=55, `0x4B`=75, `0x64`=100.
- paired `float32` ≈ pct/100: 55→`0x3f0ccccd` (0.55), 75→`0x3f403300` (0.7508), 100→`0x3f806200`
  (1.003). Roughly linear; small positive drift at high %.
- The brake-balance entry only exists when **adjustable brakes** is equipped (the part gates the
  tuning slot). Each car's tuning preset is a `pstv`/"Preset" containing `atvc`, inside its `racc`
  vehicle record.

**Parts (loadout).** Path strings inside the `racc` record, interned via a string dictionary
(FNV-1a-64 prime `0x100000001b3` seen in the binary = the intern hash). `Wf2Cli loadout <file>`
best-effort lists them. Catalog = loose `.upgr` files under `data\vehicle\{carNN,shared}\part\…`
(≈1734 parts, 19 cars).

Byte-exact round-trip (parse→serialize) is verified (`Wf2Cli roundtrip`).

---

## 3. The "strip" (core symptom of Approach A)

Loading a hand-edited `profile.sgfi` makes the game **drop all vehicle parts/tuning** back to
defaults on load ("strip"). Controlled tests pinned the cause to a **per-record integrity hash on
the `racc` (vehicle) record**, not the whole-file CRC.

Ruled out as the cause (each still stripped):
- whole-file CRC-32C (fixed correctly → still stripped);
- **recompression** — an **in-place 3-byte edit** of the existing LZ4 stream (no recompression)
  still stripped, so our LZ4 output is exonerated;
- **byte/float mismatch** — brake stores BOTH a percent byte and a float; editing both consistently
  still stripped;
- **value validity** — exact game-produced values still stripped.

---

## 4. Roadblock A — the record hash (unsolved; Approach A wall)

**Frame** (decompressed tree, LE): `[u32 record_length][u32 hash][f3 4b]['racc' …record…]`.
`f3` marks a hashed node; the three hashed node types are `urve`, `racc`, `nart`.

**Known (record_length, stored_hash) oracle pairs** (verify any candidate algorithm against ALL):
| save | record_length | stored hash |
|---|---|---|
| stripped-default | 0x1220 = 4640 | `0x3b061f91` |
| +adj-brakes @75% | 0x1259 = 4697 | `0x258ff564` |
| base (full)      | 0x1b40 = 6976 | `0x63719c5f` |

**On the game's own Save**, this record's length grows and the hash is **recomputed** — observed
directly (`srcc … [len 0x1b2e→0x1b33→0x1b34][hash 099ed1a3→547c5e3a→6cabc0ad] f34b racc`). That is
exactly why the game's saves are valid and ours are not.

**Ruled out for the algorithm — it is NOT a flat-byte hash.** Exhaustively tested over every
plausible start/end boundary on all three oracle saves simultaneously: CRC-32/CRC-32C with a full
custom (poly, init, refin, refout, xorout) grid; Adler-32; FNV-1/1a 32-bit and **64-bit** (low/high/
xor folds); Murmur3; xxHash32; djb2; sdbm; Jenkins one-at-a-time. Zero matches. Conclusion: the hash
is accumulated **during the bbag serialization walk** (field order, excludes length/offset metadata,
follows interned string refs) so it cannot be reproduced from raw decompressed bytes.

**Ghidra dead ends** (project at `D:\wf2_re`, headless JDK21 + Ghidra 12.1.2; exe copied to
`D:\wf2_re\Wreckfest2.exe` to avoid the space in the path):
- The `0xf3` constant is a **red herring**: `FUN_14007a970` / `FUN_14007d120` that use `0xf3` are a
  **string/path normalizer** (0xf3 = trim-flags bitmask for spaces/slashes/dots), not a hash node.
- The table CRC (`>>0x18`, tables `DAT_141327100`/`DAT_141328100`) is the **accelerated whole-file
  CRC-32C**, not a second algorithm.
- Prior decompiles saved at `D:\wf2_re\*.txt` (`recordhash.txt`, `subtree.txt`, `saveside.txt`,
  `hashnode.txt`, `decomp.txt`, `callers.txt`); scripts in `D:\wf2_re\scripts\*.java`.

**Cloud-bypass hypothesis — REFUTED.** `test_stringswap` once loaded via Steam Cloud with a wrong
CRC and a stale record hash, suggesting the cloud/lenient load path skips validation. A controlled
re-test (edit delivered via the userdata cache + forced restore, offline) was **delivered to My
Games and still stripped identically** to a direct load. Not reproducible; abandon this path.
(Also learned: Steam uploads FROM My Games, not the userdata cache, so cache edits are futile.)

---

## 5. Roadblock B — runtime memory editing (current front; premise validated, isolation unsolved)

**Why this approach:** if we change values in live memory and let the game Save, the game
recomputes the record hash itself (§4). No anti-cheat ships with WF2 (no EAC/BattlEye; PlayFab +
Steam for online only) → safe for offline/garage/career use. Do NOT take edited values online.

**Tooling built (compiles clean):** `Wf2Core/Memory/{NativeMethods,GameProcess,MemoryScanner}.cs`
and `Wf2Cli memscan` subcommands: `attach`, `find <float> [tol]`, `filter <float> [tol]`,
`findi <int>`, `filteri <int>`, `list`, `read <hexAddr> [n]`, `write <hexAddr> <float>`,
`writei <hexAddr> <int>`. Candidate set persists to `./memscan.candidates` between invocations
(next-scan workflow). Scanner walks committed, readable, non-guard regions via `VirtualQueryEx`.

**Verified:**
- **M1 attach/read/write works, no admin needed.** e.g. base `0x7FF78F360000`, 80 MB image, 4,602
  readable regions (~8.4 GB committed).
- Brake balance is findable in memory as a float ≈ pct/100 and **tracks the slider** across
  find→filter (e.g. 40,642 → 3–121 candidates after one filter).
- The in-game tuning has an **explicit Save button**; exiting prompts save/discard. (Several earlier
  "save" tests were actually *discards* → their negative results are invalid.)
- On a real Save, brake persists into the **hashed record** (§4), stored as a raw `float32` at tree
  offset ≈3280 (0.88→`@3281`, 0.19→`@3280`). NOTE: the Hurricane's tune persists here, NOT as a
  separate `atvc` block — the only `atvc` (offset 748, percent byte 100) is RoadSlayer's saved preset.

**The blocker:** value-scanning only ever finds **downstream mirrors**. Writing them changes
**neither the display (UI value is cached) nor the save**:
- Wrote 5 exact-value float copies (incl. the two cross-session-stable addresses
  `0x1D1F83E7458`, `0x1D20A35FBDC`) from 0.19→0.88, user hit real Save → **save recorded 0.19**, not
  0.88. So the Save reads an **authoritative committed value we have not isolated**.
- Strong hypothesis: the authoritative source is the **UI-committed integer percent**; the float
  mirrors are derived for physics/display and never read back. The save's `float32` is likely
  computed from the int at save time.
- Addresses **reallocate on menu transitions** (garage exit), so any absolute address is
  session-scoped; a robust tool needs pointer/AOB resolution (planned M3), and even discovery must
  re-scan each session (except the two stable addresses above, which persisted but are still mirrors).

Last action in progress: `memscan findi 19` → **185,203** int candidates, awaiting a filter pass
(user was to set brake to 84% → `filteri 84`).

---

## 6. Attempts & outcomes (quick table)

| # | Attempt | Outcome |
|---|---|---|
| A1 | Fix whole-file CRC-32C on edited save | still stripped |
| A2 | In-place 3-byte tuning edit (no recompress) + CRC | still stripped → recompression exonerated |
| A3 | Edit percent byte **and** float consistently | still stripped |
| A4 | Flat-byte record-hash search (all algos × ranges × 3 oracles) | no match → not flat-byte |
| A5 | Ghidra hunt via `0xf3` tag / table CRC | red herrings (string-normalizer / whole-file CRC) |
| A6 | Steam-Cloud lenient delivery (offline forced restore) | delivered but still stripped → refuted |
| B1 | Attach + read/write live process | works |
| B2 | find→filter brake float | narrows to a few, tracks slider |
| B3 | Write float mirrors, real Save | save keeps slider value → mirrors are downstream |
| B4 | Int hunt for UI percent | in progress (185k candidates, needs filtering) |

---

## 7. Remaining pathways

1. **Finish the integer hunt** (cheapest next step). `findi`/`filteri` the UI percent across 2–3
   distinct slider values → isolate the committed int → write it → confirm display + Save change.
   If the Save value is derived from this int, this closes B/M4.
2. **"Find what accesses this address"** (definitive). Set a **hardware breakpoint** (debug
   registers DR0–DR3 via `Get/SetThreadContext`) on a known mirror or on the save-read, capture the
   instruction, and walk back to the authoritative struct + a stable pointer path. This is the
   Cheat-Engine technique and the most reliable; needs new tooling (thread enumeration + DR
   breakpoints + a small disasm/context dump).
3. **Pointer scan** for a static base → offsets path to the authoritative value (durable M3).
4. **RE the Save serialization** (Ghidra): find where the writer reads brake balance from the setup
   struct — reveals both the authoritative field and (bonus) how the value is encoded.
5. **RE the record hash** (only if reverting to Approach A): find the read-side validate
   (compute-over-`racc`-subtree, compare to stored u32, branch to strip) using the §4 oracle to
   confirm. Enables a fully-offline editor with no game running.

Recommended order: (1) then (2). (2) also solves the reallocation problem for the eventual app.

---

## 8. Data inventory (for a reviewer)

**Desktop `.sgfi` captures** (`C:\Users\Jonathan\Desktop\`):
| file | brake | sha1 / note |
|---|---|---|
| `base_current.sgfi` | RoadSlayer 55% (full loadout) | `827d0c2f…` — user's REAL save backup |
| `RESTORE_realsave.sgfi` | RoadSlayer-era clean | `c67765d0…` |
| `stripped_current.sgfi` | defaults (post-strip) | `e337770f…` — 13 default part entries |
| `brakes75_current.sgfi` | Hurricane +adj-brakes 75% | `bb551368…` |
| `hurricane_saved33.sgfi` | Hurricane 33% (real Save) | shows the srcc len/hash bump |
| `hurricane_test88.sgfi` | after writing 0.88 mirrors + Save | save kept 0.19 (proves mirrors downstream) |
| `test_brake100_inplace.sgfi` | in-place 100% edit | `edfe15e5…` — stripped on load |

Also on Desktop: deploy/restore scripts (`restore-real-save.bat` restores `base_current.sgfi` to
My Games + userdata mirror), and Approach-A cloud/mygames deploy scripts (now superseded).

**Memory addresses (session-scoped unless noted):** brake-balance float mirrors last seen at
`0x1D1F83E7458` and `0x1D20A35FBDC` (**stable across sessions**, but still downstream mirrors);
transient mirrors under `0x1D2187…`, `0x1D218F…`. All held the exact slider value yet none drove
the Save.

**Ghidra:** `D:\wf2_re\` (project `ghidra_proj\wf2.gpr`, program `Wreckfest2.exe`, tools under
`tools\`). Re-run a script headless:
`analyzeHeadless D:\wf2_re\ghidra_proj wf2 -process Wreckfest2.exe -noanalysis -scriptPath D:\wf2_re\scripts -postScript <Script>.java`
(set `JAVA_HOME=D:\wf2_re\tools\jdk\jdk-21.0.11+10`).

**Python helpers used throughout** (native Windows `python3` has `lz4`): decompress = 
`lz4.block.decompress(open(f,'rb').read()[20:], uncompressed_size=0x80000)`; CRC-32C table with poly
`0x82F63B78`.

## 9. Current live state / cautions
- User is racing the **Hurricane**; **RoadSlayer is currently stripped** (lost adjustable brakes in
  Approach-A tests). Real save backed up at `base_current.sgfi` + `Documents\Wreckfest2 Backups\`;
  `restore-real-save.bat` restores it.
- The game was **running** during the memory session (pid changes per launch). Memory addresses in
  §8 are dead after a relaunch — re-discover.
- Approach-A deploy scripts (cloud/mygames/full) were **removed** in cleanup (they strip; superseded).
  `restore-real-save.bat` remains. The kept Desktop evidence files are those listed in §8.
