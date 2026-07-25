import { test } from "node:test";
import assert from "node:assert/strict";

import {
  PresetBuilder,
  validate,
  valueAt,
  display,
  auxForDisplay,
  PARAMS,
  FORMAT_VERSION,
} from "../dist/index.js";

const SOURCE = { car: "Rocket", carConfig: "car03:default", preset: "Test" };
const UTC = "2026-07-25T00:00:00Z";

// Known (aux -> value / UI string) read straight from a real save (live baseline). value is computed
// in float32 like the app, so it can differ from the game's own rounding by ~1e-7 (physically
// identical); the UI number the guide author cares about is exact.
const REAL = [
  { id: "brakingBalance", aux: 60, value: 0.6, ui: "60" },
  { id: "brakingPressure", aux: 13, value: 115, ui: "115" },
  { id: "gearboxFinalDrive", aux: 21, value: 4.3, ui: "4.3" },
  { id: "frontCamber", aux: 15, value: -1.25, ui: "-1.25 deg" },
  { id: "antiRollBarFront", aux: 18, value: 45000, ui: "45 kN/m" },
];

test("valueAt matches real values (within float tolerance) and displays the exact UI number", () => {
  for (const { id, aux, value, ui } of REAL) {
    const p = PARAMS[id];
    assert.ok(Math.abs(valueAt(p, aux) - value) < 1e-4, `${id} @ aux ${aux}: ${valueAt(p, aux)}`);
    assert.equal(display(p, valueAt(p, aux)), ui, `${id} display`);
  }
});

test("setDisplay picks the notch the UI number implies", () => {
  // brake balance shows a percentage (factor 100): 60 -> aux 60 -> value 0.6
  assert.equal(auxForDisplay(PARAMS.brakingBalance, 60), 60);
  // final drive is a raw ratio (factor 1): 4.3 -> aux 21
  assert.equal(auxForDisplay(PARAMS.gearboxFinalDrive, 4.3), 21);
});

test("builder produces an importable, consistent preset", () => {
  const preset = new PresetBuilder(SOURCE)
    .setDisplay("brakingBalance", 60)
    .set("gearboxFinalDrive", { aux: 21 })
    .set("frontCamber", { value: -1.25 })
    .build({ exportedUtc: UTC });

  assert.equal(preset.formatVersion, FORMAT_VERSION);
  assert.equal(preset.exportedUtc, UTC);
  assert.deepEqual(preset.source, SOURCE);

  const byIndex = Object.fromEntries(preset.tuning.map((t) => [t.paramIndex, t]));
  assert.equal(byIndex[0].aux, 60);
  assert.equal(byIndex[0].display, "60");
  assert.equal(byIndex[40].aux, 21);
  assert.ok(Math.abs(byIndex[40].value - 4.3) < 1e-4);
  assert.equal(byIndex[26].aux, 15);
  assert.equal(byIndex[26].value, -1.25);   // camber lands exactly

  // tuning sorted by paramIndex, and every entry validates
  const indices = preset.tuning.map((t) => t.paramIndex);
  assert.deepEqual(indices, [...indices].sort((a, b) => a - b));
  assert.deepEqual(validate(preset), []);
});

test("out-of-range display is clamped to the legal range", () => {
  const preset = new PresetBuilder(SOURCE).setDisplay("brakingBalance", 250).build({ exportedUtc: UTC });
  const bb = preset.tuning.find((t) => t.paramIndex === 0);
  assert.equal(bb.aux, PARAMS.brakingBalance.steps); // clamped to max notch
  assert.equal(bb.value, 1);
  assert.deepEqual(validate(preset), []);
});

test("setting the same parameter twice overwrites (no duplicates)", () => {
  const preset = new PresetBuilder(SOURCE)
    .setDisplay("brakingBalance", 40)
    .setDisplay("brakingBalance", 70)
    .build({ exportedUtc: UTC });
  const rows = preset.tuning.filter((t) => t.paramIndex === 0);
  assert.equal(rows.length, 1);           // last write wins, no duplicate
  assert.equal(rows[0].aux, 70);
  assert.equal(rows[0].value, valueAt(PARAMS.brakingBalance, 70)); // exact float32, displays as 70%
});

test("validate rejects an inconsistent (aux, value) pair", () => {
  const bad = {
    formatVersion: FORMAT_VERSION,
    exportedUtc: UTC,
    source: SOURCE,
    requiredParts: [],
    tuning: [{ paramIndex: 0, aux: 60, value: 0.9 }], // 0.9 is not what aux 60 produces (0.6)
  };
  const errors = validate(bad);
  assert.ok(errors.some((e) => /not on-step/.test(e)), errors.join("; "));
});

test("validate rejects a bad format version and duplicates", () => {
  assert.ok(validate({ formatVersion: 2, exportedUtc: UTC, source: SOURCE, requiredParts: [], tuning: [] })
    .some((e) => /formatVersion/.test(e)));

  const dup = {
    formatVersion: FORMAT_VERSION, exportedUtc: UTC, source: SOURCE, requiredParts: [],
    tuning: [{ paramIndex: 0, aux: 60, value: 0.6 }, { paramIndex: 0, aux: 40, value: 0.4 }],
  };
  assert.ok(validate(dup).some((e) => /more than once/.test(e)));
});

test("setRaw allows a relative parameter to pass through", () => {
  const preset = new PresetBuilder(SOURCE)
    .setRaw({ paramIndex: 24, aux: 10, value: 53200 }) // Springs - front (no schema)
    .build({ exportedUtc: UTC });
  const spring = preset.tuning.find((t) => t.paramIndex === 24);
  assert.equal(spring.value, 53200);
  assert.deepEqual(validate(preset), []);
});
