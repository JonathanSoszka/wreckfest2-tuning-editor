using System.Windows;

namespace Wf2App;

/// <summary>
/// Interaction logic for App.xaml.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Load and apply user preferences before the main window is shown.
        AppTheme.Apply(SettingsStore.Load());
        base.OnStartup(e);
    }
}
