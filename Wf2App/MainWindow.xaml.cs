using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wf2Core;

namespace Wf2App;

/// <summary>
/// Interaction logic for MainWindow.xaml. Holds only view concerns: the file-open dialog and mapping
/// the tree's selection onto the view-model. All data comes from <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Title = $"Wreckfest 2 Tuning  ·  v{AppVersion()}";
        _vm.OpenRequested += ShowOpenDialog;
        _vm.TryAutoLoad();
    }

    /// <summary>The build's version (e.g. "1.1.0"), from the assembly — CI stamps it from the release tag.</summary>
    private static string AppVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Informational version may carry build metadata after '+' (e.g. "1.1.0+abc123") — trim it.
        return (info?.Split('+')[0]) ?? "dev";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(AppTheme.Current) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            AppTheme.Apply(dialog.Result);
            SettingsStore.Save(dialog.Result);
        }
        // On cancel the dialog reverts the live preview itself.
    }

    private void ShowOpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a Wreckfest 2 save",
            Filter = "Wreckfest 2 save (*.sgfi)|*.sgfi|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
            _vm.Load(dialog.FileName);
    }

    /// <summary>
    /// The tree lists cars only. <see cref="TreeView.SelectedItem"/> is read-only and not bindable, so
    /// we translate the selection into the view-model here: selecting a car shows its preset pane.
    /// Presets themselves are chosen in that pane (see <see cref="PresetRow_Click"/>).
    /// </summary>
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CarVm car)
        {
            _vm.SelectedCar = car;
            _vm.SelectedPreset = null;
        }
    }

    /// <summary>Clicking a preset row in the car pane selects it — the detail switches to its tuning.</summary>
    private void PresetRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PresetVm preset })
            _vm.SelectedPreset = preset;   // SelectedCar already points at this preset's car
    }

    /// <summary>
    /// After a write reloads the save (which clears the tree's selection), re-select a car by name —
    /// so the user stays where they were instead of being dropped to the empty pane. When a preset
    /// name is given, land on that preset in the car pane too (the tree lists cars only, so the preset
    /// is driven straight through the view-model).
    /// </summary>
    private void RestoreSelection(string carName, string? presetName)
    {
        // Defer until the reloaded tree has generated its item containers.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var carVm = _vm.Cars.FirstOrDefault(c => c.Name == carName);
            if (carVm is null) return;

            // Selecting the car container fires Tree_SelectedItemChanged (SelectedCar=carVm,
            // SelectedPreset=null); fall back to setting the car directly if it isn't realized yet.
            if (Tree.ItemContainerGenerator.ContainerFromItem(carVm) is TreeViewItem carItem)
            {
                carItem.IsSelected = true;
                carItem.BringIntoView();
            }
            else
            {
                _vm.SelectedCar = carVm;
                _vm.SelectedPreset = null;
            }

            if (presetName is not null)
                _vm.SelectedPreset = carVm.Presets.FirstOrDefault(p => p.Name == presetName);
        }));
    }

    // ---------------------------------------------------------------- slider editing

    private void Edit_Click(object sender, RoutedEventArgs e) => _vm.BeginEdit();

    private void DiscardEdit_Click(object sender, RoutedEventArgs e) => _vm.CancelEdit();

    private void SaveEdits_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null || _vm.SelectedPreset is null) return;
        var carName = _vm.SelectedCar.Name;
        var presetName = _vm.SelectedPreset.Name;

        if (!_vm.HasEdits) { _vm.CancelEdit(); return; }   // nothing changed — just leave edit mode
        if (!ConfirmWriteDespiteRunning(out bool force)) return;

        try
        {
            var result = _vm.SaveEdits(force);
            var backup = result.BackupPath ?? "(none)";
            MessageBox.Show(this,
                $"Saved to {carName} / {presetName}.\n\nWrote {result.Bytes} bytes to {result.Targets.Count} file(s).\n\nBackup: {backup}",
                "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            RestoreSelection(carName, presetName);
        }
        catch (GameRunningException ex)
        {
            MessageBox.Show(this, ex.Message, "Close the game first", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------------- G2 actions

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null || _vm.SelectedPreset is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export tune",
            Filter = "Tune (*.json)|*.json|All files (*.*)|*.*",
            FileName = Sanitize($"{_vm.SelectedCar.Name}_{_vm.SelectedPreset.Name}") + ".json",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            _vm.ExportSelectedPreset(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Back link on the preset view — return to the car's preset selection.</summary>
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        var car = _vm.SelectedCar;
        if (car is null) return;
        // Show the car pane immediately. This works whether the user reached the preset via the tree
        // (tree has the preset selected) or via a car-pane row (tree still has the car selected — so
        // re-selecting the car fires no event and cannot clear the preset on its own).
        _vm.SelectedPreset = null;
        RestoreSelection(car.Name, null);   // keep the tree highlight on the car
    }

    /// <summary>Import a tune file as a NEW preset on the selected car (never overwrites an existing one).</summary>
    private void ImportNew_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null) return;
        var carName = _vm.SelectedCar.Name;

        var dialog = new OpenFileDialog
        {
            Title = "Import tune as a new preset",
            Filter = "Tune (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        PresetExport import;
        try
        {
            import = PresetIo.FromJson(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Not a valid tune file", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var taken = _vm.SelectedCar.Presets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seed = string.IsNullOrWhiteSpace(import.Source.Preset) ? "Imported preset" : import.Source.Preset;
        var prompt = new TextPromptDialog("Import as new preset",
            $"Name for the new preset on “{carName}” (from {import.Source.Car} / {import.Source.Preset}, {import.Tuning.Count} value(s)):",
            UniqueName(seed, taken), taken) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        var newName = prompt.EnteredText;

        // Surface compatibility / out-of-range notes before writing.
        var plan = _vm.PlanImportAsNew(carName, import);
        var notes = plan.Warnings.Concat(plan.RangeWarnings.Select(r =>
            $"{r.Name}: {r.Value:0.####} " + (r.IsExact
                ? $"exceeds the game limit ({r.Min:0.####}–{r.Max:0.####})"
                : $"is outside the range seen in existing presets ({r.Min:0.####}–{r.Max:0.####})"))).ToList();
        if (notes.Count > 0)
        {
            var choice = MessageBox.Show(this,
                "This tune has:\n\n• " + string.Join("\n\n• ", notes) + "\n\nImport it anyway?",
                "Import warnings", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes) return;
        }

        if (!ConfirmWriteDespiteRunning(out bool force)) return;
        try
        {
            var result = _vm.CreateFromImport(carName, newName, import, force);
            var backup = result.BackupPath ?? "(none)";
            MessageBox.Show(this,
                $"Imported into new preset '{newName}' on {carName}.\n\nWrote {result.Bytes} bytes to {result.Targets.Count} file(s).\n\nBackup: {backup}",
                "Imported", MessageBoxButton.OK, MessageBoxImage.Information);
            RestoreSelection(carName, newName);
        }
        catch (GameRunningException ex)
        {
            MessageBox.Show(this, ex.Message, "Close the game first", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null || _vm.SelectedPreset is null) return;
        var carName = _vm.SelectedCar.Name;   // a successful write reloads and clears the selection

        var taken = _vm.SelectedCar.Presets.Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultName = UniqueName($"{_vm.SelectedPreset.Name} copy", taken);

        var prompt = new TextPromptDialog("Duplicate preset",
            $"New name for the copy of “{_vm.SelectedPreset.Name}”:", defaultName, taken) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        var newName = prompt.EnteredText;

        if (!ConfirmWriteDespiteRunning(out bool force)) return;
        try
        {
            var result = _vm.DuplicateSelectedPreset(newName, force);
            var backup = result.BackupPath ?? "(none)";
            MessageBox.Show(this,
                $"Created preset '{newName}'.\n\nWrote {result.Bytes} bytes to {result.Targets.Count} file(s).\n\nBackup: {backup}",
                "Duplicated", MessageBoxButton.OK, MessageBoxImage.Information);
            RestoreSelection(carName, newName);
        }
        catch (GameRunningException ex)
        {
            MessageBox.Show(this, ex.Message, "Close the game first", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Duplicate failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null) return;
        var carName = _vm.SelectedCar.Name;   // capture: a successful write reloads and clears the selection

        var taken = _vm.SelectedCar.Presets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prompt = new TextPromptDialog("New preset",
            $"Name for the new preset on “{carName}” (every slider starts at its default):",
            UniqueName("New Preset", taken), taken) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        var newName = prompt.EnteredText;

        if (!ConfirmWriteDespiteRunning(out bool force)) return;
        try
        {
            var result = _vm.CreatePresetOnSelectedCar(newName, force);
            var backup = result.BackupPath ?? "(none)";
            MessageBox.Show(this,
                $"Created preset '{newName}' on {carName}.\n\nWrote {result.Bytes} bytes to {result.Targets.Count} file(s).\n\nBackup: {backup}",
                "New preset", MessageBoxButton.OK, MessageBoxImage.Information);
            RestoreSelection(carName, newName);
        }
        catch (GameRunningException ex)
        {
            MessageBox.Show(this, ex.Message, "Close the game first", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not create preset", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------------- preset right-click menu

    /// <summary>The preset a context-menu item was invoked on (the menu inherits the row's DataContext).</summary>
    private static PresetVm? MenuPreset(object sender) => (sender as FrameworkElement)?.DataContext as PresetVm;

    private void PresetExport_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null || MenuPreset(sender) is not { } preset) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export tune",
            Filter = "Tune (*.json)|*.json|All files (*.*)|*.*",
            FileName = Sanitize($"{_vm.SelectedCar.Name}_{preset.Name}") + ".json",
        };
        if (dialog.ShowDialog(this) != true) return;

        try { _vm.ExportPreset(preset.Name, dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void PresetRename_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null || MenuPreset(sender) is not { } preset) return;
        var carName = _vm.SelectedCar.Name;   // a successful write reloads and clears the selection
        var oldName = preset.Name;

        var taken = _vm.SelectedCar.Presets.Select(p => p.Name)
            .Where(n => !string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prompt = new TextPromptDialog("Rename preset", $"New name for “{oldName}”:", oldName, taken) { Owner = this };
        if (prompt.ShowDialog() != true) return;

        if (!ConfirmWriteDespiteRunning(out bool force)) return;
        try
        {
            _vm.RenamePresetOnSelectedCar(oldName, prompt.EnteredText, force);
            RestoreSelection(carName, null);
        }
        catch (GameRunningException ex)
        {
            MessageBox.Show(this, ex.Message, "Close the game first", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Rename failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PresetDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedCar is null || MenuPreset(sender) is not { } preset) return;
        var carName = _vm.SelectedCar.Name;
        var name = preset.Name;

        var confirm = MessageBox.Show(this,
            $"Delete preset “{name}” from {carName}?\n\nThis can't be undone — but a backup of your profile is saved first.",
            "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        if (!ConfirmWriteDespiteRunning(out bool force)) return;
        try
        {
            _vm.DeletePresetOnSelectedCar(name, force);
            RestoreSelection(carName, null);
        }
        catch (GameRunningException ex)
        {
            MessageBox.Show(this, ex.Message, "Close the game first", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string UniqueName(string basis, ISet<string> taken)
    {
        if (!taken.Contains(basis)) return basis;
        for (int i = 2; ; i++)
        {
            var candidate = $"{basis} {i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Writing while the game or Steam runs is risky — the game can overwrite the file on its next
    /// save and Steam Cloud can re-sync a stale copy. Warn and let the user proceed at their own risk.
    /// Returns false if the user cancelled; otherwise <paramref name="force"/> says whether to override.
    /// </summary>
    private bool ConfirmWriteDespiteRunning(out bool force)
    {
        force = false;
        if (!_vm.IsGameOrSteamRunning) return true;

        var choice = MessageBox.Show(this,
            "Wreckfest 2 or Steam is running.\n\n" +
            "Writing now is risky: the game can overwrite this change on its next save, and " +
            "Steam Cloud can re-sync an older copy back over it. Closing both first is safest.\n\n" +
            "A timestamped backup is taken either way. Write anyway?",
            "Game or Steam is running", MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes) return false;
        force = true;
        return true;
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
    }
}
