using Microsoft.Win32;

namespace Wf2App;

/// <summary>
/// Windows-only Steam discovery that <c>Wf2Core.SaveLocator</c> cannot do (it is cross-platform):
/// the Steam install path from the registry. Fed to the locator as an extra root.
/// </summary>
internal static class WindowsSteam
{
    /// <summary>The Steam root from <c>HKCU\Software\Valve\Steam\SteamPath</c>, if present.</summary>
    public static IReadOnlyList<string> Roots()
    {
        var roots = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string path && !string.IsNullOrWhiteSpace(path))
                roots.Add(path.Replace('/', '\\'));
        }
        catch (Exception)
        {
            // Registry unavailable or access denied — fall back to the locator's default locations.
        }
        return roots;
    }
}
