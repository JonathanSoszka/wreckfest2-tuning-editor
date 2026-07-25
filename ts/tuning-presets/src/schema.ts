import { PARAMS, FORMAT_VERSION } from "./schema.generated.js";
import type { ParamId, ParamSchema } from "./schema.generated.js";

export { PARAMS, FORMAT_VERSION };
export type { ParamId, ParamSchema };

/** All parameters, ascending by index. */
export const ALL_PARAMS: ParamSchema[] = Object.values(PARAMS).sort((a, b) => a.index - b.index);

/** Look a parameter up by its numeric game index. */
export const PARAM_BY_INDEX: Map<number, ParamSchema> = new Map(ALL_PARAMS.map((p) => [p.index, p]));

// The app computes values in 32-bit float; mirror that exactly so a library-authored value is
// byte-identical to one the app itself would produce (no spurious "changed" diffs on import).
const f32 = Math.fround;

/** The value change per slider notch, in stored units. */
export function stepSize(p: ParamSchema): number {
  return f32(f32(f32(p.max) - f32(p.min)) / p.steps);
}

/** The stored (SI) value produced by slider position `aux`. */
export function valueAt(p: ParamSchema, aux: number): number {
  return f32(f32(p.min) + f32(aux * stepSize(p)));
}

/** Snap an arbitrary position to the nearest legal notch (integer, 0..steps). */
export function clampAux(p: ParamSchema, aux: number): number {
  return Math.min(Math.max(Math.round(aux), 0), p.steps);
}

/** The nearest legal notch that produces a stored value closest to `storedValue`. */
export function auxForValue(p: ParamSchema, storedValue: number): number {
  if (p.max === p.min) return 0;
  return clampAux(p, ((storedValue - p.min) / (p.max - p.min)) * p.steps);
}

/** The nearest legal notch for a value expressed as the game's UI shows it (e.g. 60 for 60%). */
export function auxForDisplay(p: ParamSchema, uiNumber: number): number {
  return auxForValue(p, uiNumber / p.displayFactor);
}

/** Render a stored value the way the game's UI would, e.g. "60", "-1.25 deg", "45 kN/m". */
export function display(p: ParamSchema, storedValue: number): string {
  const n = Math.round(storedValue * p.displayFactor * 10000) / 10000;
  return p.displayUnit ? `${n} ${p.displayUnit}` : `${n}`;
}
