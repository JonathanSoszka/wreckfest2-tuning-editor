using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Wf2Core;

namespace Wf2App;

/// <summary>
/// The window's view-model: load a save and expose its cars, presets and values for browsing. G1 is
/// read-only — there is no write path here yet (see <c>docs/PLAN_gui.md</c>).
///
/// <para><b>No format logic lives in this project.</b> Everything binary goes through
/// <see cref="SaveFile"/>; this class only binds the result to the UI.</para>
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly SaveWriter _writer = new(new ProcessSystemState());
    private readonly SaveLocator _locator = SaveLocator.ForCurrentMachine(WindowsSteam.Roots());
    private SaveFile? _save;

    /// <summary>Whether the game or Steam is running — a write-time hazard the user is warned about.</summary>
    public bool IsGameOrSteamRunning => _writer.IsGameOrSteamRunning();

    /// <summary>Where timestamped backups are written before every save.</summary>
    public string BackupDir => SaveWriter.DefaultBackupDir;

    public MainViewModel()
    {
        OpenCommand = new RelayCommand(() => OpenRequested?.Invoke());
        ReloadCommand = new RelayCommand(Reload, () => SavePath is not null);
    }

    /// <summary>Cars in the currently loaded save, in file order.</summary>
    public ObservableCollection<CarVm> Cars { get; } = [];

    private string? _savePath;
    /// <summary>Path of the loaded save, or null when nothing is loaded.</summary>
    public string? SavePath
    {
        get => _savePath;
        private set
        {
            if (!Set(ref _savePath, value)) return;
            OnPropertyChanged(nameof(HasSave));
            OnPropertyChanged(nameof(ShowNoSave));
            OnPropertyChanged(nameof(ShowEmptyHint));
        }
    }

    public bool HasSave => _savePath is not null;

    /// <summary>No profile is loaded yet — show the first-run "Open a profile" prompt.</summary>
    public bool ShowNoSave => _savePath is null;

    private string _status = "No save loaded.";
    /// <summary>A one-line status shown in the footer.</summary>
    public string Status { get => _status; private set => Set(ref _status, value); }

    private CarVm? _selectedCar;
    public CarVm? SelectedCar
    {
        get => _selectedCar;
        set { if (Set(ref _selectedCar, value)) NotifyDetailState(); }
    }

    private PresetVm? _selectedPreset;
    /// <summary>The preset whose values fill the detail pane. Set by the tree selection.</summary>
    public PresetVm? SelectedPreset
    {
        get => _selectedPreset;
        set { if (Set(ref _selectedPreset, value)) NotifyDetailState(); }
    }

    /// <summary>A preset is selected — show its tuning grid.</summary>
    public bool HasSelectedPreset => _selectedPreset is not null;

    /// <summary>A car (but no preset under it) is selected — show the car summary + New preset.</summary>
    public bool ShowCar => _selectedCar is not null && _selectedPreset is null;

    /// <summary>A save is loaded but nothing is selected — show the "pick a car" hint.</summary>
    public bool ShowEmptyHint => HasSave && _selectedCar is null && _selectedPreset is null;

    private void NotifyDetailState()
    {
        IsEditing = false;   // changing the selection leaves edit mode
        OnPropertyChanged(nameof(HasSelectedPreset));
        OnPropertyChanged(nameof(ShowCar));
        OnPropertyChanged(nameof(ShowEmptyHint));
        OnPropertyChanged(nameof(ViewingPreset));
        OnPropertyChanged(nameof(EditingPreset));
        OnPropertyChanged(nameof(CanEdit));
    }

    // ---------------------------------------------------------------- edit mode

    private string? _editCar, _editPreset;

    private bool _isEditing;
    /// <summary>True while the selected preset's values are being edited with sliders.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (!Set(ref _isEditing, value)) return;
            OnPropertyChanged(nameof(ViewingPreset));
            OnPropertyChanged(nameof(EditingPreset));
        }
    }

    /// <summary>Preset selected, not editing → show the read-only value grid.</summary>
    public bool ViewingPreset => HasSelectedPreset && !_isEditing;

    /// <summary>Preset selected, editing → show the slider editor.</summary>
    public bool EditingPreset => HasSelectedPreset && _isEditing;

    private IReadOnlyList<EditableValueVm> _editables = [];
    /// <summary>The selected preset's values as editable rows (populated in edit mode).</summary>
    public IReadOnlyList<EditableValueVm> Editables
    {
        get => _editables;
        private set => Set(ref _editables, value);
    }

    /// <summary>True when a preset is selected — every preset can be edited, since any editable
    /// parameter can be set even if the preset currently leaves it at its default.</summary>
    public bool CanEdit => _save is not null && SelectedCar is not null && SelectedPreset is not null;

    /// <summary>
    /// Enter edit mode. Builds a row for <b>every</b> editable parameter (whether or not the preset
    /// stores it) plus any stored parameter that has no editable schema (shown read-only for context),
    /// ordered by parameter index. Stored parameters start at their stored position; the rest start at
    /// their default and are only written if the user moves them.
    /// </summary>
    public void BeginEdit()
    {
        if (_save is null || SelectedCar is null || SelectedPreset is null) return;
        var car = _save.Cars.Find(SelectedCar.Name);
        var preset = car?.Find(SelectedPreset.Name);
        if (car is null || preset is null) return;

        _editCar = car.Name;
        _editPreset = preset.Name;

        var stored = preset.Tuning.ToDictionary(t => t.ParamIndex);
        var indices = TuningSchema.EditableIndices
            .Concat(preset.Tuning.Select(t => t.ParamIndex))   // include stored relative/unknown params
            .Distinct()
            .OrderBy(i => i);
        Editables = indices.Select(i =>
            stored.TryGetValue(i, out var rec)
                ? new EditableValueVm(rec)
                : new EditableValueVm(i, TuningSchema.For(i)!)).ToList();
        IsEditing = true;
    }

    /// <summary>Leave edit mode, discarding any unsaved slider changes.</summary>
    public void CancelEdit()
    {
        Editables = [];
        IsEditing = false;
    }

    /// <summary>True when a slider has moved — there is something to save.</summary>
    public bool HasEdits => _editables.Any(e => e.IsDirty);

    /// <summary>
    /// Write every changed value (Tier 1, in place) through the safe pipeline, then leave edit mode.
    /// Applied to a freshly-loaded save so a failed write never half-edits the display.
    /// </summary>
    /// <param name="force">Write even if the game/Steam is running (the caller must have warned).</param>
    /// <exception cref="GameRunningException">Game/Steam running and <paramref name="force"/> is false.</exception>
    public WriteResult SaveEdits(bool force = false)
    {
        if (_savePath is null || _editCar is null || _editPreset is null)
            throw new InvalidOperationException("Not editing a preset.");

        var dirty = _editables.Where(e => e.IsDirty).ToList();
        var fresh = SaveFile.Load(_savePath);
        foreach (var e in dirty)
        {
            // A parameter the preset already stored is overwritten in place; one that was at its default
            // gets a new record added (the game omits defaults, so this is how we bring it off default).
            if (e.WasStored)
                fresh.Cars.SetTuningValue(_editCar, _editPreset, e.ParamIndex, e.CurrentAux, e.Value);
            else
                fresh.Cars.AddTuningValue(_editCar, _editPreset, e.ParamIndex, e.CurrentAux, e.Value);
        }
        var bytes = fresh.Serialize();

        var targets = _locator.WriteTargetsFor(_savePath);
        var backup = _writer.WriteAllMirrors(targets, bytes, force: force);

        CancelEdit();
        Load(_savePath);
        Status = $"Saved {dirty.Count} change(s) to {_editCar} / {_editPreset}  ·  wrote {targets.Count} file(s)  ·  backup: " +
                 (backup is null ? "none" : Path.GetFileName(backup));
        return new WriteResult(backup, targets, bytes.Length);
    }

    public ICommand OpenCommand { get; }
    public ICommand ReloadCommand { get; }

    /// <summary>Raised when the user asks to open a file; the view shows the dialog and calls
    /// <see cref="Load"/>. Keeps the file dialog (a view concern) out of the view-model.</summary>
    public event Action? OpenRequested;

    /// <summary>Load a save from disk, replacing whatever is shown. CRC problems are surfaced in the
    /// status line rather than thrown — a damaged save is still worth inspecting.</summary>
    public void Load(string path)
    {
        try
        {
            var save = SaveFile.Load(path);
            _save = save;
            Cars.Clear();
            // Cars the user has actually built presets on (more than one) float to the top; then by name.
            var ordered = save.Cars.Select(c => new CarVm(c))
                .OrderByDescending(c => c.Presets.Count > 1)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Mark the boundary between the two groups so the tree can draw a divider there.
            int firstSingle = ordered.FindIndex(c => c.Presets.Count <= 1);
            if (firstSingle > 0)
                ordered[firstSingle].HasDividerAbove = true;
            foreach (var vm in ordered)
                Cars.Add(vm);

            SavePath = path;
            SelectedCar = null;
            SelectedPreset = null;

            int presets = Cars.Sum(c => c.Presets.Count);
            string crc = save.AllCrcsValid ? "all CRCs valid" : "CRC MISMATCH — inspect only";
            Status = $"{Cars.Count} cars, {presets} presets  ·  {crc}  ·  {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            _save = null;
            Cars.Clear();
            SavePath = null;
            SelectedPreset = null;
            Status = $"Could not open {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- G2: export, duplicate, rename, delete

    /// <summary>
    /// Create a new empty preset (all sliders at default) on the selected car and write through the
    /// safe pipeline. Applied to a freshly-loaded save so a failed write never half-edits the display.
    /// </summary>
    /// <param name="force">Write even if the game/Steam is running (the caller must have warned).</param>
    /// <exception cref="GameRunningException">Game/Steam running and <paramref name="force"/> is false.</exception>
    public WriteResult CreatePresetOnSelectedCar(string newName, bool force = false)
    {
        if (_savePath is null) throw new InvalidOperationException("No save loaded.");
        if (SelectedCar is null) throw new InvalidOperationException("No car selected.");
        var carName = SelectedCar.Name;

        var fresh = SaveFile.Load(_savePath);
        fresh.Cars.CreatePreset(carName, newName);
        var bytes = fresh.Serialize();

        var targets = _locator.WriteTargetsFor(_savePath);
        var backup = _writer.WriteAllMirrors(targets, bytes, force: force);

        Load(_savePath);
        Status = $"Created preset '{newName.Trim()}' on {carName}  ·  wrote {targets.Count} file(s)  ·  backup: " +
                 (backup is null ? "none" : Path.GetFileName(backup));
        return new WriteResult(backup, targets, bytes.Length);
    }

    /// <summary>Preview importing a tune file as a new preset on <paramref name="carName"/> (for warnings).</summary>
    public ImportPlan PlanImportAsNew(string carName, PresetExport import)
    {
        if (_save is null) throw new InvalidOperationException("No save loaded.");
        return PresetIo.PlanNewPreset(_save, carName, import);
    }

    /// <summary>
    /// Create a new preset on <paramref name="carName"/> holding the imported tune's values, and write
    /// through the safe pipeline. Applied to a freshly-loaded save so a failed write never half-edits
    /// the display.
    /// </summary>
    /// <param name="force">Write even if the game/Steam is running (the caller must have warned).</param>
    /// <exception cref="GameRunningException">Game/Steam running and <paramref name="force"/> is false.</exception>
    public WriteResult CreateFromImport(string carName, string newName, PresetExport import, bool force = false)
    {
        if (_savePath is null) throw new InvalidOperationException("No save loaded.");

        var fresh = SaveFile.Load(_savePath);
        fresh.Cars.CreatePreset(carName, newName);
        foreach (var v in import.Tuning)
            fresh.Cars.AddTuningValue(carName, newName, v.ParamIndex, v.Aux, v.Value);
        var bytes = fresh.Serialize();

        var targets = _locator.WriteTargetsFor(_savePath);
        var backup = _writer.WriteAllMirrors(targets, bytes, force: force);

        Load(_savePath);
        Status = $"Imported {import.Tuning.Count} value(s) into new preset '{newName.Trim()}' on {carName}  ·  " +
                 $"wrote {targets.Count} file(s)  ·  backup: " + (backup is null ? "none" : Path.GetFileName(backup));
        return new WriteResult(backup, targets, bytes.Length);
    }

    /// <summary>
    /// Duplicate a named preset of the selected car under <paramref name="newName"/> and write through
    /// the safe pipeline, applied to a freshly-loaded save so a failed write never half-edits the
    /// display. Targets any preset by name (used by both the detail view and the right-click menu).
    /// </summary>
    /// <param name="force">Write even if the game/Steam is running (the caller must have warned).</param>
    /// <exception cref="GameRunningException">Game/Steam running and <paramref name="force"/> is false.</exception>
    public WriteResult DuplicatePresetOnSelectedCar(string presetName, string newName, bool force = false)
    {
        if (_savePath is null) throw new InvalidOperationException("No save loaded.");
        if (SelectedCar is null) throw new InvalidOperationException("No car selected.");
        var carName = SelectedCar.Name;

        var fresh = SaveFile.Load(_savePath);
        fresh.Cars.DuplicatePreset(carName, presetName, newName);
        var bytes = fresh.Serialize();

        var targets = _locator.WriteTargetsFor(_savePath);
        var backup = _writer.WriteAllMirrors(targets, bytes, force: force);

        Load(_savePath);
        Status = $"Duplicated {carName} / {presetName} → '{newName.Trim()}'  ·  wrote {targets.Count} file(s)  ·  backup: " +
                 (backup is null ? "none" : Path.GetFileName(backup));
        return new WriteResult(backup, targets, bytes.Length);
    }

    /// <summary>Export a named preset of the selected car to JSON (a pure file write — no backup needed).</summary>
    public void ExportPreset(string presetName, string outPath)
    {
        if (_save is null) throw new InvalidOperationException("No save loaded.");
        if (SelectedCar is null) throw new InvalidOperationException("No car selected.");
        var car = _save.Cars.Find(SelectedCar.Name) ?? throw new InvalidOperationException("Car not found.");
        var preset = car.Find(presetName) ?? throw new InvalidOperationException($"No preset named '{presetName}'.");
        File.WriteAllText(outPath, PresetIo.ToJson(PresetIo.Export(car, preset, DateTimeOffset.UtcNow)));
        Status = $"Exported {car.Name} / {preset.Name} ({preset.Tuning.Count} value(s)) → {Path.GetFileName(outPath)}";
    }

    /// <summary>
    /// Rename a preset of the selected car and write through the safe pipeline. Applied to a freshly
    /// loaded save so a failed write never half-edits the display.
    /// </summary>
    /// <param name="force">Write even if the game/Steam is running (the caller must have warned).</param>
    /// <exception cref="GameRunningException">Game/Steam running and <paramref name="force"/> is false.</exception>
    public WriteResult RenamePresetOnSelectedCar(string presetName, string newName, bool force = false)
    {
        if (_savePath is null) throw new InvalidOperationException("No save loaded.");
        if (SelectedCar is null) throw new InvalidOperationException("No car selected.");
        var carName = SelectedCar.Name;

        var fresh = SaveFile.Load(_savePath);
        fresh.Cars.RenamePreset(carName, presetName, newName);
        var bytes = fresh.Serialize();

        var targets = _locator.WriteTargetsFor(_savePath);
        var backup = _writer.WriteAllMirrors(targets, bytes, force: force);

        Load(_savePath);
        Status = $"Renamed '{presetName}' → '{newName.Trim()}' on {carName}  ·  wrote {targets.Count} file(s)  ·  backup: " +
                 (backup is null ? "none" : Path.GetFileName(backup));
        return new WriteResult(backup, targets, bytes.Length);
    }

    /// <summary>
    /// Delete a preset from the selected car and write through the safe pipeline. Applied to a freshly
    /// loaded save so a failed write never half-edits the display.
    /// </summary>
    /// <param name="force">Write even if the game/Steam is running (the caller must have warned).</param>
    /// <exception cref="GameRunningException">Game/Steam running and <paramref name="force"/> is false.</exception>
    public WriteResult DeletePresetOnSelectedCar(string presetName, bool force = false)
    {
        if (_savePath is null) throw new InvalidOperationException("No save loaded.");
        if (SelectedCar is null) throw new InvalidOperationException("No car selected.");
        var carName = SelectedCar.Name;

        var fresh = SaveFile.Load(_savePath);
        fresh.Cars.DeletePreset(carName, presetName);
        var bytes = fresh.Serialize();

        var targets = _locator.WriteTargetsFor(_savePath);
        var backup = _writer.WriteAllMirrors(targets, bytes, force: force);

        Load(_savePath);
        Status = $"Deleted '{presetName}' from {carName}  ·  wrote {targets.Count} file(s)  ·  backup: " +
                 (backup is null ? "none" : Path.GetFileName(backup));
        return new WriteResult(backup, targets, bytes.Length);
    }

    private void Reload()
    {
        if (_savePath is not null) Load(_savePath);
    }

    /// <summary>
    /// Load the player's live save if it can be found, so the app is useful the moment it opens.
    /// Silent when absent — the user can still Open one. This is a deliberately minimal probe; robust
    /// discovery (Steam library folders, the userdata mirror) is <c>SaveLocator</c> in G4.
    /// </summary>
    public void TryAutoLoad()
    {
        var saves = _locator.FindSaves();
        if (saves.Count == 0)
        {
            Status = "No save found automatically. Use Open… to choose a profile.sgfi.";
            return;
        }

        Load(saves[0]);
        if (saves.Count > 1)
            Status += $"  ·  {saves.Count} profiles found — Open… to pick another";
    }
}

/// <summary>The outcome of a successful save write: the backup taken and the files written.</summary>
public sealed record WriteResult(string? BackupPath, IReadOnlyList<string> Targets, int Bytes);
