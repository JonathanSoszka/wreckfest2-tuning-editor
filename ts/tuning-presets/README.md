# @wreckfest/tuning-presets

Construct **importable Wreckfest 2 tuning presets** (`.json`) from TypeScript — for a companion
website that publishes tuning suggestions the [Wreckfest 2 Tuning Editor](../../README.md) can import.

Export only: this library *builds* preset files. The desktop app handles import.

## Install

```bash
npm install @wreckfest/tuning-presets
```

## Use

```ts
import { PresetBuilder } from "@wreckfest/tuning-presets";

const json = new PresetBuilder({ car: "Rocket", carConfig: "car03:default", preset: "Dirt — beginner" })
  .setDisplay("brakingBalance", 60)     // the number the game's UI shows (60%)
  .setDisplay("frontCamber", -1.25)     // degrees
  .set("gearboxFinalDrive", { aux: 21 })// or set the slider notch directly
  .set("frontToe", { value: 0 })        // or the stored (SI) value
  .requiredParts(["part/roll_bar/front_antiroll_bar_adjustable.upgr"])
  .toJson();

// `json` is exactly what the app's "Import from file" accepts.
```

Three ways to set a parameter, all of which stay on the game's legal steps:

- `set(id, { display })` / `setDisplay(id, n)` — the number the UI shows.
- `set(id, { value })` — the stored SI value.
- `set(id, { aux })` — the raw slider notch (`0..steps`).

`build()` throws if the result wouldn't import; `warnings()` reports soft issues (out of range,
inferred mapping) without throwing. `setRaw({ paramIndex, aux, value })` is an escape hatch for
springs/dampers/ride-height (no fixed schema yet) or unknown indices.

## What's editable

The 21 parameters with an exact schema — brakes, differentials, anti-roll bars, camber/toe, the
gearbox, and ackerman. Springs, dampers and ride height have a car-specific base that isn't decoded
yet, so they're `setRaw`-only. See `PARAMS` / `ALL_PARAMS` for the full list and ranges.

## How it stays correct

`src/schema.generated.ts` is generated from `schema/wf2-schema.json`, which is emitted by the app's
own source of truth:

```bash
# from the repo root
dotnet run --project Wf2Cli -- schema ts/tuning-presets/schema/wf2-schema.json
cd ts/tuning-presets && npm run gen
```

The aux→value arithmetic is computed in 32-bit float to match the app byte-for-byte. `npm test`
checks library output against real values read from actual saves.

## Scripts

- `npm run gen` — regenerate the schema from `schema/wf2-schema.json`.
- `npm run build` — generate + compile to `dist/`.
- `npm test` — build then run the test suite.
