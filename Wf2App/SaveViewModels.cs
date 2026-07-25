using Wf2Core;

namespace Wf2App;

/// <summary>
/// One stored tuning value, rendered for display. Read-only: G1 is a browser (see
/// <c>docs/PLAN_gui.md</c>). All naming and unit conversion comes from <see cref="ParamMap"/>, so an
/// index the map does not know still shows — as <c>parameter N</c>, flagged <see cref="IsMapped"/>
/// false — rather than being hidden.
/// </summary>
public sealed class ValueRowVm
{
    public ValueRowVm(TuningRecord record)
    {
        ParamIndex = record.ParamIndex;
        Aux = record.Aux;
        Raw = record.Value;

        var info = ParamMap.Lookup(record.ParamIndex);
        IsMapped = info is not null;
        Name = info?.Name ?? $"parameter {record.ParamIndex}";
        Display = ParamMap.DisplayOf(record.ParamIndex, record.Value);
    }

    public uint ParamIndex { get; }
    public string IndexLabel => $"#{ParamIndex}";

    /// <summary>The friendly name, or <c>parameter N</c> when the index is unmapped.</summary>
    public string Name { get; }

    /// <summary>False when the index is not in <see cref="ParamMap"/> — the row is shown muted.</summary>
    public bool IsMapped { get; }

    /// <summary>The value as the game's UI would render it (name + unit via <see cref="ParamMap"/>).</summary>
    public string Display { get; }

    /// <summary>The raw stored physical value, shown alongside the display value for transparency.</summary>
    public float Raw { get; }

    public uint Aux { get; }
}

/// <summary>One tuning preset: a name, its stored-value count, and the values themselves.</summary>
public sealed class PresetVm
{
    public PresetVm(TuningPreset preset, bool isActive)
    {
        Name = preset.Name;
        ValueCount = preset.Tuning.Count;
        Values = preset.Tuning.Select(t => new ValueRowVm(t)).ToList();
        IsActive = isActive;
    }

    public string Name { get; }
    public int ValueCount { get; }
    public IReadOnlyList<ValueRowVm> Values { get; }

    /// <summary>True when this is the preset the car currently has selected in-game.</summary>
    public bool IsActive { get; }

    /// <summary>Tree label, e.g. <c>"Hybrid_  (25)"</c>.</summary>
    public string Header => $"{Name}  ({ValueCount})";

    /// <summary>True when the preset stores anything — an empty preset means "all sliders default".</summary>
    public bool HasValues => ValueCount > 0;
}

/// <summary>One car: its name/config, its presets, and which preset it currently has selected.</summary>
public sealed class CarVm
{
    public CarVm(CarRecord car)
    {
        Name = car.Name;
        Config = car.Config;
        SelectedPreset = car.SelectedPreset;
        PartCount = car.Parts.Count;
        Presets = car.Presets
            .Select(p => new PresetVm(p, isActive: string.Equals(p.Name, car.SelectedPreset, StringComparison.Ordinal)))
            .ToList();
    }

    public string Name { get; }
    public string Config { get; }
    public string SelectedPreset { get; }
    public int PartCount { get; }
    public IReadOnlyList<PresetVm> Presets { get; }

    /// <summary>True for the first car below the "more than one preset" group — renders a divider above it.</summary>
    public bool HasDividerAbove { get; set; }

    /// <summary>Tree label, e.g. <c>"Hurricane  [car02:default]"</c>.</summary>
    public string Header => $"{Name}  [{Config}]";
}
