// The importable preset format — mirrors the C# records in Wf2Core/PresetIo.cs. Keys are camelCase,
// matching the app's JSON serializer.

/** Where a tune came from. Informational — the app uses it only for cross-car warnings on import. */
export interface PresetSource {
  car: string;
  carConfig: string;
  preset: string;
}

/** One exported tuning value. `paramIndex`, `value` and `aux` are authoritative and written verbatim. */
export interface TuningExportValue {
  /** The game's numeric parameter index. */
  paramIndex: number;
  /** The physical value in SI base units, exactly as stored — never the UI number. */
  value: number;
  /** The slider position (an integer 0..steps). */
  aux: number;
  /** Informational; regenerated and ignored on import. */
  name?: string;
  /** Informational; the value as the UI shows it. Ignored on import. */
  display?: string;
}

/** A tune saved outside the game: one preset of one car, as portable JSON. */
export interface PresetExport {
  /** Must be 1 — the app rejects any other version. */
  formatVersion: number;
  /** ISO-8601 UTC timestamp, e.g. "2026-07-25T05:00:00Z". Informational. */
  exportedUtc: string;
  source: PresetSource;
  /** Car-independent adjustable-part roles the tune assumes; drives a warning if the target lacks them. */
  requiredParts: string[];
  tuning: TuningExportValue[];
}
