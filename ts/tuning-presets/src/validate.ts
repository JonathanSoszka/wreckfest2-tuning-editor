import { FORMAT_VERSION, PARAM_BY_INDEX, valueAt } from "./schema.js";
import type { PresetExport } from "./types.js";

/**
 * Enforce exactly what the app's importer requires, at author time. Returns a list of human-readable
 * errors; an empty list means the app will accept the preset. Mirrors PresetIo.FromJson plus the
 * (aux, value) consistency the game relies on.
 */
export function validate(preset: PresetExport): string[] {
  const errors: string[] = [];

  if (preset.formatVersion !== FORMAT_VERSION)
    errors.push(`formatVersion must be ${FORMAT_VERSION} (got ${preset.formatVersion}).`);

  const s = preset.source;
  if (!s || !s.car || !s.carConfig || !s.preset)
    errors.push("source.car, source.carConfig and source.preset are all required.");

  if (!Array.isArray(preset.tuning)) {
    errors.push("tuning must be an array.");
    return errors;
  }

  const seen = new Set<number>();
  for (const v of preset.tuning) {
    const tag = `parameter ${v.paramIndex}`;
    if (!Number.isFinite(v.value)) errors.push(`${tag}: value must be a finite number.`);
    if (!Number.isInteger(v.aux) || v.aux < 0) errors.push(`${tag}: aux must be a non-negative integer.`);
    if (seen.has(v.paramIndex)) errors.push(`${tag} is listed more than once.`);
    seen.add(v.paramIndex);

    const p = PARAM_BY_INDEX.get(v.paramIndex);
    if (p) {
      if (v.aux > p.steps) errors.push(`${p.name}: aux ${v.aux} exceeds the maximum notch ${p.steps}.`);
      const expected = valueAt(p, v.aux);
      if (Math.abs(v.value - expected) > 1e-4)
        errors.push(
          `${p.name}: value ${v.value} is not on-step for aux ${v.aux} (expected ${expected}). ` +
            "Use the builder's set()/setDisplay() so value and aux stay consistent."
        );
    }
  }

  return errors;
}
