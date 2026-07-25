using System.Windows;

namespace Wf2App;

/// <summary>
/// The settings dialog. Font/size changes apply live via <see cref="AppTheme"/>; Save persists them,
/// Cancel reverts to whatever was in effect when the dialog opened.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly AppSettings _original;
    private readonly SettingsViewModel _vm;

    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();
        _original = current.Clone();
        _vm = new SettingsViewModel(current);
        DataContext = _vm;
    }

    /// <summary>The settings the user confirmed (valid only when the dialog returned true).</summary>
    public AppSettings Result => _vm.ToSettings();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DialogResult != true)
            AppTheme.Apply(_original);   // reverted / cancelled — undo the live preview
    }
}
