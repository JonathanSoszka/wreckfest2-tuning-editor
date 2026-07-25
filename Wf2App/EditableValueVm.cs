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
    private readonly bool _stored;             // did the preset already store this parameter?

    /// <summary>A parameter the preset stores. Editable if it has an exact schema, else read-only.</summary>
    public EditableValueVm(TuningRecord record)
    {
        ParamIndex = record.ParamIndex;
        Name = ParamMap.NameOf(record.ParamIndex);
        _schema = TuningSchema.For(record.ParamIndex);
        _originalAux = record.Aux;
        _fixedValue = record.Value;
        _aux = record.Aux;
        _stored = true;
    }

    /// <summary>
    /// An editable parameter the preset does <b>not</b> store — it is sitting at the game's default
    /// (which presets omit). The slider lets the user set it; until they move it, nothing is written and
    /// the value reads "default" (we don't decode per-car defaults, so we don't invent a number).
    /// </summary>
    public EditableValueVm(uint paramIndex, TuningParameter schema)
    {
        ParamIndex = paramIndex;
        Name = ParamMap.NameOf(paramIndex);
        _schema = schema;
        _originalAux = 0;
        _fixedValue = 0;
        _aux = 0;
        _stored = false;
    }

    public uint ParamIndex { get; }
    public string Name { get; }
    public string IndexLabel => $"#{ParamIndex}";

    /// <summary>True when this parameter has an exact schema and can be moved.</summary>
    public bool IsEditable => _schema is not null;

    /// <summary>True when the preset already stored this parameter (so a save overwrites, not adds).</summary>
    public bool WasStored => _stored;

    /// <summary>An editable parameter still at its (unstored) default — nothing to write, no value to show.</summary>
    public bool AtDefault => IsEditable && !_stored && (uint)_aux == _originalAux;

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
            OnPropertyChanged(nameof(AtDefault));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>The stored value the current slider position produces (or the fixed value when read-only).</summary>
    public float Value => _schema is null ? _fixedValue : _schema.ValueAt((uint)_aux);

    /// <summary>The value rendered the way the game's UI shows it — or "default" for an untouched default.</summary>
    public string Display =>
        !IsEditable ? ParamMap.DisplayOf(ParamIndex, _fixedValue)
        : AtDefault ? "default"
        : ParamMap.DisplayOf(ParamIndex, Value);

    /// <summary>The value at the two slider ends, for context labels.</summary>
    public string MinLabel => _schema is null ? "" : ParamMap.DisplayOf(ParamIndex, _schema.ValueAt(0));
    public string MaxLabel => _schema is null ? "" : ParamMap.DisplayOf(ParamIndex, _schema.ValueAt(_schema.Steps));

    /// <summary>True when the slider has moved from where it started (an unstored default is dirty once moved off 0).</summary>
    public bool IsDirty => IsEditable && (uint)_aux != _originalAux;

    /// <summary>The aux to write on save.</summary>
    public uint CurrentAux => (uint)_aux;
}
