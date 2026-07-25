# Wf2Cli

Headless harness over `Wf2Core`. This is the **primary tool for reverse-engineering and
verification** — fast to iterate, easy to diff, no GUI in the way. Most findings in
`docs/format.md` came from commands in here.

Console app, `net8.0`, top-level statements in `Program.cs`.

```powershell
dotnet run --project Wf2Cli -- <command> [args]
# or after building:
.\Wf2Cli\bin\Debug\net8.0\Wf2Cli.exe <command> [args]
```

## Commands

### Save inspection
| Command | Purpose |
|---|---|
| `info <file.sgfi>` | outer header + one line per chunk: block count, decoded size, CRC status |
| `cars <file.sgfi>` | cars → presets → stored tuning records |
| `parts <file.sgfi>` | the parts fitted to each car |
| `roundtrip <file.sgfi>` | parse → serialize → confirm byte-identical |
| `decompress <file.sgfi> <out.bin> [chunk]` | decode **every** LZ4 block of a chunk (default `srcc`) to the raw node tree |
| `hexdiff <a.sgfi> <b.sgfi>` | every differing byte offset — the workhorse for in-game diff sessions |

### Game data
| Command | Purpose |
|---|---|
| `catalog <vehicleDir> [car]` | installable parts from `<install>\data\vehicle` (~1734 loose `.upgr` files) |
| `tuning <tuningDir>` | decode every `.ctms` — the raw slider-unit min/max per tunable parameter |
| `calibrate <save> [saves…]` | aggregate every stored value per `paramIndex` across all presets — the **empirical stored-unit range** per index, and which indices are still unnamed |
| `bbag <file> [out.bin]` | decode **any** bbag container (`.vecs` `.vesg` `.vbpr` `.vedi` `.vtpr` `.upgr` `.ctms`) and dump its strings/floats — how the cars' **base/default** values were found |
| `guides <vehicleDir> <outDir>` | export car names/descriptions + catalog to JSON |

### Presets
| Command | Purpose |
|---|---|
| `preset export <save> <car> <preset> <out.json>` | save one tune as portable JSON |
| `preset export-all <save> <outDir>` | every preset of every car, one file each — bulk backup |
| `preset import <save> <out.sgfi> <car> <preset> <in.json> [--dry-run] [--allow-grow]` | apply a tune → writes a **new** file |
| `preset duplicate <save> <out.sgfi> <car> <preset> <newName>` | copy a preset into a new one → writes a **new** file |
| `preset create <save> <out.sgfi> <car> <newName>` | add a new empty preset (all sliders at default) → writes a **new** file |

`import` prints its full plan before writing: what changes, what is skipped and why, plus warnings
for cross-car imports and missing adjustable parts. `--dry-run` prints the plan and writes nothing —
**use it first.**

Import is **Tier 1**: it overwrites values the target preset already stores. A slider sitting at its
default stores no record, so there is nothing to overwrite and the value is reported as skipped. Set
that slider in-game once, then re-import.

### Editing
| Command | Purpose |
|---|---|
| `settune <in> <out> <car> <preset> <paramIndex> <aux> <value>` | overwrite one stored tuning value → writes a **new** file |

There is deliberately no part-swap command. The old `setpart` was built on a parser that scanned the
outer tree (mostly still-compressed bytes) and never recomputed chunk CRCs, so its output loaded and
then silently stripped. Part editing, if it comes back, belongs on `CarCollection` /
`SaveChunk.DecodedPayload` like `settune` — see [`docs/PLAN_presets.md`](../docs/PLAN_presets.md).

## Conventions

- **Reads and writes files you name.** Nothing here touches the live save implicitly — but
  `settune` does write, so pass explicit paths and keep backups.
- Exit codes: `0` success, `1` error, `2` usage.
- No format logic here — anything binary belongs in `Wf2Core`. If you find yourself parsing bytes in
  `Program.cs`, it's in the wrong project.

## Useful starting point

```powershell
# what can actually be tuned, and within what limits
dotnet run --project Wf2Cli -- tuning "C:\Program Files (x86)\Steam\steamapps\common\Wreckfest 2\data\vehicle\shared\part\tuning"
```
