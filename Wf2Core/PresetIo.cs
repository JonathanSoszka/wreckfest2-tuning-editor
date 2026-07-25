using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wf2Core;

/// <summary>Where an exported tune came from. Informational — import never keys off this.</summary>
public sealed record PresetSource(string Car, string CarConfig, string Preset);

/// <summary>
/// One exported tuning value.
/// </summary>
/// <param name="ParamIndex">Authoritative. The parameter this value belongs to.</param>
/// <param name="Value">
/// Authoritative. The <b>physical</b> value in SI base units, exactly as stored — never the UI number.
/// </param>
/// <param name="Aux">
/// Authoritative. Carried through verbatim. Its meaning is only proven for the gearbox
/// (<c>value = min + aux/100 × (max−min)</c>), so we never synthesize it.
/// </param>
/// <param name="Name">Informational. Regenerated on read; ignored on import.</param>
/// <param name="Display">Informational. The value as the UI would show it; ignored on import.</param>
public sealed record TuningExportValue(uint ParamIndex, float Value, uint Aux,
                                       string? Name = null, string? Display = null);

/// <summary>A tune saved outside the game: one preset of one car, as portable JSON.</summary>
public sealed record PresetExport(
    int FormatVersion,
    string ExportedUtc,
    PresetSource Source,
    IReadOnlyList<string> RequiredParts,
    IReadOnlyList<TuningExportValue> Tuning);

/// <summary>A value that will be written, with the value it replaces.</summary>
public sealed record PlannedChange(uint ParamIndex, string Name,
                                   float FromValue, uint FromAux,
                                   float ToValue, uint ToAux)
{
    /// <summary>True when the stored bits would not actually change.</summary>
    public bool IsNoOp => FromValue.Equals(ToValue) && FromAux == ToAux;
}

/// <summary>
/// A value that will be added because the target preset does not store the parameter yet. Only
/// produced when the import is allowed to grow the preset (Tier 2).
/// </summary>
public sealed record AddedChange(uint ParamIndex, string Name, float Value, uint Aux);

/// <summary>A value that will <b>not</b> be written, and the reason in plain words.</summary>
public sealed record SkippedChange(uint ParamIndex, string Name, string Reason);

/// <summary>
/// A value that will be written but lies outside the accepted range for its parameter — a warn-only
/// flag, never a block.
/// </summary>
/// <param name="IsExact">
/// True when <see cref="Min"/>/<see cref="Max"/> are the game's <b>exact</b> legal limits (from the
/// parameter's <c>.ctms</c> schema, see <see cref="TuningSchema"/>) — the value genuinely exceeds what
/// the game allows. False when they are the <b>observed</b> range (<see cref="ParamRanges"/>, a lower
/// bound), so the value may still be legal, just unusual.
/// </param>
public sealed record RangeWarning(uint ParamIndex, string Name, float Value, float Min, float Max,
                                  bool IsAdd, bool IsExact);

/// <summary>
/// What an import would do, inspectable <b>before</b> anything is written. The GUI shows it as a
/// diff; the CLI prints it under <c>--dry-run</c>. Given how this format punishes mistakes, nothing
/// should write a save without the user having been able to see this first.
/// </summary>
public sealed record ImportPlan(string Car, string Preset,
                                IReadOnlyList<PlannedChange> Applied,
                                IReadOnlyList<AddedChange> Added,
                                IReadOnlyList<SkippedChange> Skipped,
                                IReadOnlyList<string> Warnings,
                                IReadOnlyList<RangeWarning> RangeWarnings)
{
    /// <summary>True when applying this plan would change nothing.</summary>
    public bool IsEmpty => Added.Count == 0 && Applied.All(a => a.IsNoOp);
}

/// <summary>
/// Export and import of tuning presets.
///
/// <para><b>Two tiers.</b> A value whose parameter the target preset <em>already stores</em> is
/// overwritten (Tier 1) — size-neutral, the most-verified write path. A value whose parameter is
/// <em>missing</em> (its slider sits at the game's default, so no record exists) is either
/// <b>added</b>, when <see cref="ImportOptions.AllowAdd"/> is set (Tier 2, grows the preset), or
/// <b>skipped</b> and reported when it is not.</para>
///
/// <para><b>Nothing is silently dropped.</b> Every value in the file ends up in exactly one of
/// <see cref="ImportPlan.Applied"/>, <see cref="ImportPlan.Added"/> or
/// <see cref="ImportPlan.Skipped"/> — a partially applied tune the user believes is complete is the
/// worst outcome available here.</para>
/// </summary>
public static class PresetIo
{
    /// <summary>The version stamped into exports. Bump when the schema changes incompatibly.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Options controlling how an import applies.</summary>
    /// <param name="AllowAdd">
    /// When true, a parameter the target preset does not store yet is <b>added</b> (Tier 2, grows the
    /// preset) rather than skipped. When false (the default), such a parameter is skipped and reported.
    /// </param>
    public sealed record ImportOptions(bool AllowAdd = false)
    {
        /// <summary>Tier 1 only: overwrite existing records, skip everything else.</summary>
        public static ImportOptions Default { get; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ------------------------------------------------------------------ export

    /// <summary>
    /// Capture one preset as a portable export.
    /// </summary>
    /// <param name="exportedUtc">
    /// Timestamp to stamp in. Injected rather than read from the clock so exports are reproducible
    /// and round-trip tests are deterministic.
    /// </param>
    public static PresetExport Export(CarRecord car, TuningPreset preset, DateTimeOffset exportedUtc)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(preset);

        var values = preset.Tuning
            .Select(t => new TuningExportValue(
                t.ParamIndex, t.Value, t.Aux,
                ParamMap.NameOf(t.ParamIndex),
                ParamMap.DisplayOf(t.ParamIndex, t.Value)))
            .ToList();

        return new PresetExport(
            CurrentFormatVersion,
            exportedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            new PresetSource(car.Name, car.Config, preset.Name),
            AdjustableParts(car),
            values);
    }

    /// <summary>
    /// The fitted parts a tune plausibly depends on — those whose asset path says "adjustable".
    /// Only adjustable parts carry a tuning schema, so a non-adjustable part cannot be the subject
    /// of a stored value.
    ///
    /// <para>Returned as a <b>car-independent role</b>: the owning directory
    /// (<c>data/vehicle/car02/</c> or <c>data/vehicle/shared/</c>) is stripped, leaving e.g.
    /// <c>part/roll_bar/front_antiroll_bar_adjustable.upgr</c>. Every car keeps its own copy of the
    /// same part under its own directory, so comparing full paths across cars would report every
    /// part as missing — the role is the thing that is actually comparable.</para>
    ///
    /// <para>This is a <b>filename heuristic</b>, not a read of each part's <c>smtc</c> reference
    /// (which would need the game install on hand). It drives warnings only, never a hard failure.</para>
    /// </summary>
    public static IReadOnlyList<string> AdjustableParts(CarRecord car)
    {
        ArgumentNullException.ThrowIfNull(car);
        return car.Parts
            .Where(p => p.Contains("adjustable", StringComparison.OrdinalIgnoreCase))
            .Select(PartRole)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Strip <c>data/vehicle/&lt;owner&gt;/</c> from a part path, leaving the car-independent role.
    /// Paths that do not have that shape are returned unchanged.
    /// </summary>
    private static string PartRole(string path)
    {
        const string prefix = "data/vehicle/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return path;
        int slash = path.IndexOf('/', prefix.Length);
        return slash < 0 ? path : path[(slash + 1)..];
    }

    /// <summary>Serialize an export to JSON.</summary>
    public static string ToJson(PresetExport export) =>
        JsonSerializer.Serialize(export, Json);

    /// <summary>
    /// Parse an export from JSON, with structural validation.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The JSON is malformed, from an unknown format version, or holds a non-finite value.
    /// </exception>
    public static PresetExport FromJson(string json)
    {
        PresetExport? export;
        try
        {
            export = JsonSerializer.Deserialize<PresetExport>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not a valid preset file: {ex.Message}", ex);
        }

        if (export is null)
            throw new InvalidDataException("Not a valid preset file: the document is empty.");
        if (export.FormatVersion != CurrentFormatVersion)
            throw new InvalidDataException(
                $"Preset file is format version {export.FormatVersion}; this build understands " +
                $"version {CurrentFormatVersion}.");
        if (export.Tuning is null)
            throw new InvalidDataException("Preset file has no 'tuning' array.");

        foreach (var v in export.Tuning)
            if (!float.IsFinite(v.Value))
                throw new InvalidDataException(
                    $"Preset file holds a non-finite value ({v.Value}) for parameter {v.ParamIndex}.");

        var duplicate = export.Tuning.GroupBy(v => v.ParamIndex).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException(
                $"Preset file lists parameter {duplicate.Key} more than once.");

        return export;
    }

    // ------------------------------------------------------------------ import

    /// <summary>
    /// Work out what importing <paramref name="import"/> onto a preset would do. Writes nothing.
    /// </summary>
    /// <param name="options">
    /// Import behaviour. Defaults to Tier 1 (overwrite only); pass one with
    /// <see cref="ImportOptions.AllowAdd"/> set to also add parameters the preset lacks.
    /// </param>
    /// <exception cref="InvalidOperationException">The car or preset does not exist in the save.</exception>
    public static ImportPlan Plan(SaveFile save, string carName, string presetName, PresetExport import,
                                  ImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(import);
        options ??= ImportOptions.Default;

        var car = save.Cars.Find(carName)
            ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");
        var preset = car.Find(presetName)
            ?? throw new InvalidOperationException($"Car '{car.Name}' has no preset named '{presetName}'.");

        var applied = new List<PlannedChange>();
        var added = new List<AddedChange>();
        var skipped = new List<SkippedChange>();

        foreach (var v in import.Tuning)
        {
            string name = ParamMap.NameOf(v.ParamIndex);
            var target = preset.Find(v.ParamIndex);
            if (target is not null)
            {
                applied.Add(new PlannedChange(v.ParamIndex, name,
                                              target.Value, target.Aux, v.Value, v.Aux));
            }
            else if (options.AllowAdd)
            {
                added.Add(new AddedChange(v.ParamIndex, name, v.Value, v.Aux));
            }
            else
            {
                skipped.Add(new SkippedChange(v.ParamIndex, name,
                    "the target preset has this slider at its default, so it stores no record. " +
                    "Re-run with grow/add enabled to add it, or set the slider in-game once."));
            }
        }

        return new ImportPlan(car.Name, preset.Name, applied, added, skipped,
                              Warnings(car, preset, import),
                              RangeWarningsFor(applied, added));
    }

    /// <summary>
    /// Flag every value being written that falls outside the observed range for its parameter
    /// (<see cref="ParamRanges"/>). Warn-only — the values are still applied.
    /// </summary>
    private static List<RangeWarning> RangeWarningsFor(
        IEnumerable<PlannedChange> applied, IEnumerable<AddedChange> added)
    {
        var warnings = new List<RangeWarning>();
        foreach (var c in applied)
            if (!c.IsNoOp && CheckRange(c.ParamIndex, c.Name, c.ToValue, isAdd: false) is { } w)
                warnings.Add(w);
        foreach (var a in added)
            if (CheckRange(a.ParamIndex, a.Name, a.Value, isAdd: true) is { } w)
                warnings.Add(w);
        return warnings;
    }

    /// <summary>
    /// Range-check one value: prefer the exact <c>.ctms</c> schema (<see cref="TuningSchema"/>) and
    /// fall back to the observed ranges (<see cref="ParamRanges"/>) only where no exact schema exists.
    /// Returns null when the value is in range or there is nothing to judge by.
    /// </summary>
    private static RangeWarning? CheckRange(uint paramIndex, string name, float value, bool isAdd)
    {
        if (TuningSchema.For(paramIndex) is not null)
            return TuningSchema.IsOutsideExact(paramIndex, value, out float min, out float max)
                ? new RangeWarning(paramIndex, name, value, min, max, isAdd, IsExact: true)
                : null;

        return ParamRanges.IsOutsideObserved(paramIndex, value, out var r)
            ? new RangeWarning(paramIndex, name, value, r.Min, r.Max, isAdd, IsExact: false)
            : null;
    }

    private static List<string> Warnings(CarRecord car, TuningPreset? preset, PresetExport import)
    {
        var warnings = new List<string>();

        if (!string.Equals(car.Config, import.Source.CarConfig, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Cross-car import: the tune came from '{import.Source.Car}' " +
                         $"({import.Source.CarConfig}), the target is '{car.Name}' ({car.Config}). " +
                         "Values are physical, so they carry over, but a setup tuned for another " +
                         "chassis is not necessarily a good one.");

        var have = new HashSet<string>(AdjustableParts(car), StringComparer.OrdinalIgnoreCase);
        var missing = (import.RequiredParts ?? [])
            .Where(p => !have.Contains(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            warnings.Add("The target car does not have these adjustable parts the tune assumes: " +
                         string.Join(", ", missing) +
                         ". Values for sliders those parts provide will be skipped or ignored by the game.");

        var unknown = import.Tuning.Select(v => v.ParamIndex)
            .Where(i => ParamMap.Lookup(i) is null).OrderBy(i => i).ToList();
        if (unknown.Count > 0)
            warnings.Add($"Unrecognised parameter index(es): {string.Join(", ", unknown)}. " +
                         "They will still be written verbatim — the map in docs/PARAM_MAP.md is " +
                         "just incomplete.");

        // Range checking is deliberately absent, not forgotten: paramIndex -> .ctms binding is
        // inferred rather than proven (docs/PLAN_presets.md §4), so a range check today could
        // reject legal tunes. Values come from the game's own sliders, so they are in range by
        // construction as long as the file was not hand-edited.
        if (preset is not null && preset.Tuning.Count == 0)
            warnings.Add($"Preset '{preset.Name}' currently stores no values at all — every slider " +
                         "is at its default. A Tier 1 import has nothing to overwrite; enable " +
                         "grow/add to write the whole tune.");

        return warnings;
    }

    /// <summary>
    /// Preview importing <paramref name="import"/> as a <b>new</b> preset on <paramref name="carName"/>
    /// (rather than overwriting an existing one). Every value is an add; the warnings (cross-car,
    /// missing parts, out-of-range) are computed against the target car. Writes nothing — the caller
    /// creates the preset and adds the values (see the app's create-from-import path).
    /// </summary>
    /// <exception cref="InvalidOperationException">The car does not exist in the save.</exception>
    public static ImportPlan PlanNewPreset(SaveFile save, string carName, PresetExport import)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(import);
        var car = save.Cars.Find(carName)
            ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");

        var added = import.Tuning
            .Select(v => new AddedChange(v.ParamIndex, ParamMap.NameOf(v.ParamIndex), v.Value, v.Aux))
            .ToList();

        return new ImportPlan(car.Name, "(new preset)", [], added, [],
                              Warnings(car, preset: null, import),
                              RangeWarningsFor([], added));
    }

    /// <summary>
    /// Apply a plan. The caller is responsible for serializing and writing the save afterwards.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The save changed since <see cref="Plan"/> ran, so a record is no longer where it was.
    /// </exception>
    public static void Apply(SaveFile save, ImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var change in plan.Applied)
        {
            if (change.IsNoOp) continue;
            save.Cars.SetTuningValue(plan.Car, plan.Preset, change.ParamIndex,
                                     change.ToAux, change.ToValue);
        }

        // Adds resize the payload; each re-finds the preset by name and re-reads the node offset
        // after the previous insert shifted it, so order is irrelevant and reparse-safe.
        foreach (var add in plan.Added)
            save.Cars.AddTuningValue(plan.Car, plan.Preset, add.ParamIndex, add.Aux, add.Value);
    }
}
