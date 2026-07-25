import {
  PARAMS,
  PARAM_BY_INDEX,
  FORMAT_VERSION,
  valueAt,
  clampAux,
  auxForValue,
  auxForDisplay,
  display,
} from "./schema.js";
import type { ParamId } from "./schema.js";
import type { PresetExport, PresetSource, TuningExportValue } from "./types.js";
import { validate } from "./validate.js";

export interface BuildOptions {
  /** Timestamp to stamp (ISO-8601 Z). Defaults to now. Pass a fixed value for reproducible output. */
  exportedUtc?: string;
  /** Include the informational `name`/`display` fields (default true). */
  includeLabels?: boolean;
}

/** How to set a schema-backed parameter: by slider notch, stored value, or the UI number. */
export type SetAt = { aux: number } | { value: number } | { display: number };

/**
 * Fluent builder for an importable tuning preset. Every schema-backed setter computes a consistent,
 * on-step (aux, value) pair, so the output always passes the app's importer.
 *
 * ```ts
 * const json = new PresetBuilder({ car: "Rocket", carConfig: "car03:default", preset: "Dirt" })
 *   .setDisplay("brakingBalance", 60)
 *   .setDisplay("frontCamber", -1.25)
 *   .set("gearboxFinalDrive", { aux: 21 })
 *   .toJson();
 * ```
 */
export class PresetBuilder {
  private readonly source: PresetSource;
  private parts: string[] = [];
  private readonly values = new Map<number, TuningExportValue>();

  constructor(source: PresetSource) {
    this.source = { ...source };
  }

  /** Set a known parameter by id, choosing the position by notch, stored value, or UI number. */
  set(id: ParamId, at: SetAt): this {
    const p = PARAMS[id];
    if (!p) throw new Error(`Unknown parameter id '${id}'.`);
    const aux =
      "aux" in at ? clampAux(p, at.aux)
      : "value" in at ? auxForValue(p, at.value)
      : auxForDisplay(p, at.display);
    const value = valueAt(p, aux);
    this.values.set(p.index, { paramIndex: p.index, value, aux, name: p.name, display: display(p, value) });
    return this;
  }

  /** Convenience: set a known parameter by the number the game's UI shows (e.g. 60 for 60%). */
  setDisplay(id: ParamId, uiNumber: number): this {
    return this.set(id, { display: uiNumber });
  }

  /**
   * Escape hatch for a parameter with no schema (springs/dampers/ride-height, whose base is
   * car-specific and not yet decoded) or an index this library doesn't know. You supply the exact
   * `aux` and `value`; only the finite/unique rules are enforced.
   */
  setRaw(v: { paramIndex: number; aux: number; value: number; name?: string }): this {
    const label = v.name ?? PARAM_BY_INDEX.get(v.paramIndex)?.name;
    this.values.set(v.paramIndex, {
      paramIndex: v.paramIndex,
      value: v.value,
      aux: v.aux,
      ...(label ? { name: label } : {}),
    });
    return this;
  }

  /** Remove a previously-set parameter. */
  unset(id: ParamId): this {
    const p = PARAMS[id];
    if (p) this.values.delete(p.index);
    return this;
  }

  /** Declare the adjustable-part roles this tune assumes (drives the app's missing-part warning). */
  requiredParts(parts: string[]): this {
    this.parts = [...parts];
    return this;
  }

  /** Non-fatal notes about the current values (out-of-range, inferred mapping). Never throws. */
  warnings(): string[] {
    const out: string[] = [];
    for (const v of this.values.values()) {
      const p = PARAM_BY_INDEX.get(v.paramIndex);
      if (!p) continue;
      if (v.value < Math.min(p.min, p.max) - 1e-4 || v.value > Math.max(p.min, p.max) + 1e-4)
        out.push(`${p.name}: ${v.value} is outside the legal range ${p.min}..${p.max}.`);
      if (!p.confirmed) out.push(`${p.name}: parameter mapping is inferred, not confirmed.`);
    }
    return out;
  }

  /** Build (and validate) the typed preset object. Throws if it would not import cleanly. */
  build(opts: BuildOptions = {}): PresetExport {
    const includeLabels = opts.includeLabels !== false;
    const tuning = [...this.values.values()]
      .sort((a, b) => a.paramIndex - b.paramIndex)
      .map((v) =>
        includeLabels
          ? v
          : { paramIndex: v.paramIndex, value: v.value, aux: v.aux }
      );

    const preset: PresetExport = {
      formatVersion: FORMAT_VERSION,
      exportedUtc: opts.exportedUtc ?? isoNow(),
      source: this.source,
      requiredParts: this.parts,
      tuning,
    };

    const errors = validate(preset);
    if (errors.length) throw new Error("Preset is not importable:\n" + errors.map((e) => "  - " + e).join("\n"));
    return preset;
  }

  /** Build and serialize to the exact JSON the app imports. */
  toJson(opts: BuildOptions = {}): string {
    return JSON.stringify(this.build(opts), null, 2) + "\n";
  }
}

function isoNow(): string {
  // Match the app's "yyyy-MM-ddTHH:mm:ssZ" shape (no milliseconds).
  return new Date().toISOString().replace(/\.\d{3}Z$/, "Z");
}
