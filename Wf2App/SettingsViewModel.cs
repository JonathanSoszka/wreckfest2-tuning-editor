using System.Windows.Media;

namespace Wf2App;

/// <summary>
/// Drives the settings dialog. Changing the font or size applies immediately (the whole app updates
/// behind the dialog), so what you see is what you get; the dialog reverts on cancel.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(AppSettings current)
    {
        Fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _selectedFont = Fonts.FirstOrDefault(f => string.Equals(f.Source, current.FontFamily, StringComparison.OrdinalIgnoreCase))
                        ?? Fonts.FirstOrDefault();
        _fontSize = current.FontSize;
    }

    /// <summary>Installed font families, name-sorted; each renders in its own face in the list.</summary>
    public IReadOnlyList<FontFamily> Fonts { get; }

    /// <summary>Selectable base sizes, in points.</summary>
    public IReadOnlyList<double> Sizes { get; } = [10, 11, 12, 13, 14, 15, 16, 18, 20];

    private FontFamily? _selectedFont;
    public FontFamily? SelectedFont
    {
        get => _selectedFont;
        set { if (Set(ref _selectedFont, value)) ApplyLive(); }
    }

    private double _fontSize;
    public double FontSize
    {
        get => _fontSize;
        set { if (Set(ref _fontSize, value)) ApplyLive(); }
    }

    /// <summary>Preview line rendered in the chosen font/size (the dialog itself also updates live).</summary>
    public string PreviewText => "Springs — front  56.8 kN/m   ·   Gearbox 2.30 → 4.90";

    private void ApplyLive() => AppTheme.Apply(ToSettings());

    public AppSettings ToSettings() => new()
    {
        FontFamily = _selectedFont?.Source ?? "Segoe UI",
        FontSize = _fontSize,
    };
}
