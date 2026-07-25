using Wf2Core;

namespace Wf2App;

/// <summary>
/// One tuning value in edit mode. The slider position <b>is</b> the stored <c>aux</c>, and the value
/// is derived from it — <c>value = min + aux × (max − min) / steps</c> — so any position produces a
/// legal, on-step value by construction. Only parameters with a known exact schema
/// (<see cref="TuningSchema"/>) are editable; the rest show their value read-only.
/// </summary>
public sealed class EditableValueVm : ObservableObject
{
    private readonly TuningParameter? _schema; // null → not editable (relative / unidentified)
    private readonly float _fixedValue;        // shown for non-editable rows
    private readonly uint _originalAux;

    public EditableValueVm(TuningRecord record)
    {
        ParamIndex = record.ParamIndex;
        Name = ParamMap.NameOf(record.ParamIndex);
        _schema = TuningSchema.For(record.ParamIndex);
        _originalAux = record.Aux;
        _fixedValue = record.Value;
        _aux = record.Aux;
    }

    public uint ParamIndex { get; }
    public string Name { get; }
    public string IndexLabel => $"#{ParamIndex}";

    /// <summary>True when this parameter has an exact schema and can be moved.</summary>
    public bool IsEditable => _schema is not null;

    /// <summary>Slider maximum: aux runs 0..steps. Zero for non-editable rows.</summary>
    public double SliderMax => _schema?.Steps ?? 0;

    private double _aux;
    /// <summary>Slider position, i.e. the stored <c>aux</c>. Clamped and snapped to whole increments.</summary>
    public double Aux
    {
        get => _aux;
        set
        {
            double snapped = Math.Clamp(Math.Round(value), 0, SliderMax);
            if (!Set(ref _aux, snapped)) return;
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>The stored value the current slider position produces (or the fixed value when read-only).</summary>
    public float Value => _schema is null ? _fixedValue : _schema.ValueAt((uint)_aux);

    /// <summary>The value rendered the way the game's UI shows it.</summary>
    public string Display => ParamMap.DisplayOf(ParamIndex, Value);

    /// <summary>The value at the two slider ends, for context labels.</summary>
    public string MinLabel => _schema is null ? "" : ParamMap.DisplayOf(ParamIndex, _schema.ValueAt(0));
    public string MaxLabel => _schema is null ? "" : ParamMap.DisplayOf(ParamIndex, _schema.ValueAt(_schema.Steps));

    /// <summary>True when the slider has moved from where it started.</summary>
    public bool IsDirty => IsEditable && (uint)_aux != _originalAux;

    /// <summary>The aux to write on save.</summary>
    public uint CurrentAux => (uint)_aux;
}
