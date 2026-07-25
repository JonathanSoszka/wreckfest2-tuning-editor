using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Wf2App;

/// <summary>User preferences, persisted between runs. Grows as more settings are added.</summary>
public sealed class AppSettings
{
    /// <summary>The UI font family (general text — the tabular value columns stay monospace).</summary>
    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Base UI font size in points. Text with an explicit size (headings) is unaffected.</summary>
    public double FontSize { get; set; } = 13;

    public AppSettings Clone() => new() { FontFamily = FontFamily, FontSize = FontSize };
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under <c>%AppData%\Wf2App</c>.</summary>
internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wf2App", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Corrupt or unreadable settings should never stop the app — fall back to defaults.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Json));
    }
}

/// <summary>
/// Applies <see cref="AppSettings"/> to the running app. Font family/size are pushed into
/// application resources that every window binds with <c>DynamicResource</c>, so a change takes
/// effect live across the whole UI.
/// </summary>
internal static class AppTheme
{
    /// <summary>The settings currently in effect.</summary>
    public static AppSettings Current { get; private set; } = new();

    public static void Apply(AppSettings settings)
    {
        Current = settings;
        var res = Application.Current.Resources;
        res["AppFontFamily"] = new FontFamily(settings.FontFamily);
        res["AppFontSize"] = settings.FontSize;
    }
}
