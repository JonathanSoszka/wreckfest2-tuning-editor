using System.Windows;

namespace Wf2App;

/// <summary>
/// A small single-line text prompt with inline validation: the entered text must be non-blank and
/// not one of <see cref="_taken"/> (case-insensitive). Used for naming a duplicated preset.
/// </summary>
public partial class TextPromptDialog : Window
{
    private readonly ISet<string> _taken;

    public TextPromptDialog(string title, string message, string initialText, ISet<string> takenNames)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        _taken = takenNames;

        Input.Text = initialText;
        Input.SelectAll();
        Input.Focus();
        Validate();
    }

    /// <summary>The confirmed, trimmed text. Valid only when the dialog returned true.</summary>
    public string EnteredText => Input.Text.Trim();

    private void Input_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Validate();

    private void Validate()
    {
        // Error is a helper; OkButton may not be created yet during the first TextChanged in InitializeComponent.
        if (OkButton is null) return;

        var text = Input.Text.Trim();
        string? error = text.Length == 0
            ? "Enter a name."
            : _taken.Contains(text) ? "That name is already used by this car."
            : null;

        OkButton.IsEnabled = error is null;
        Error.Text = error ?? "";
        Error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
