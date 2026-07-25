using System.Text;
using System.Text.RegularExpressions;

namespace Wf2Core;

/// <summary>Coarse grouping of an equipped part for display.</summary>
public enum PartGroup
{
    /// <summary>Drivetrain / chassis parts that affect how the car drives.</summary>
    Performance,
    /// <summary>Body, cosmetic, and incidental parts (doors, livery, driver, …).</summary>
    Other,
}

/// <summary>
/// One part fitted to a car, resolved for display: its category and variant (from the asset path),
/// its friendly name (from the part's <c>.upgr</c>, when the game install is available), whether it
/// is adjustable, and which display group it belongs to.
/// </summary>
public sealed record EquippedPart(
    string AssetPath, string Category, string TopCategory, string Variant,
    string? Name, bool IsAdjustable, PartGroup Group)
{
    /// <summary>The name to show: the resolved <see cref="Name"/>, else a prettified variant.</summary>
    public string Display => Name ?? EquippedParts.Prettify(Variant);

    /// <summary>The category rendered for humans, e.g. <c>"Engine / Air Filter"</c>.</summary>
    public string CategoryLabel =>
        string.Join(" / ", Category.Split('/').Select(EquippedParts.Prettify));
}

/// <summary>
/// Resolves a car's fitted part paths (<see cref="CarRecord.Parts"/>) into displayable
/// <see cref="EquippedPart"/>s. Friendly names come from each part's <c>.upgr</c> in the game install;
/// with no install, resolution still works — names just fall back to a prettified variant.
/// </summary>
public static partial class EquippedParts
{
    // Top-level categories that count as "performance" (everything else is Other).
    private static readonly HashSet<string> PerformanceCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "engine", "exhaust", "tires", "clutch", "gearbox", "brakes", "suspension",
        "roll_bar", "transmission", "differential", "drivetrain", "intake", "air_filter",
    };

    /// <summary>Resolve every fitted part of <paramref name="car"/> for display.</summary>
    /// <param name="gameInstallDir">
    /// The Wreckfest 2 install directory, used to read friendly names. Null (or a missing file) is
    /// fine — names then fall back to the prettified variant.
    /// </param>
    public static IReadOnlyList<EquippedPart> Resolve(CarRecord car, string? gameInstallDir)
    {
        ArgumentNullException.ThrowIfNull(car);
        return Resolve(car.Parts, gameInstallDir);
    }

    /// <summary>Resolve a list of <c>data/vehicle/…/*.upgr</c> asset paths for display.</summary>
    public static IReadOnlyList<EquippedPart> Resolve(IEnumerable<string> assetPaths, string? gameInstallDir)
    {
        ArgumentNullException.ThrowIfNull(assetPaths);
        var result = new List<EquippedPart>();
        foreach (var path in assetPaths)
        {
            var (category, variant) = SplitPath(path);
            var top = category.Length == 0 ? "" : category.Split('/')[0];
            bool adjustable = path.Contains("adjustable", StringComparison.OrdinalIgnoreCase);
            var group = PerformanceCategories.Contains(top) ? PartGroup.Performance : PartGroup.Other;
            var name = ReadName(gameInstallDir, path);
            result.Add(new EquippedPart(path, category, top, variant, name, adjustable, group));
        }
        return result;
    }

    /// <summary>Read one part's display name from its <c>.upgr</c>, or null if unavailable.</summary>
    public static string? ReadName(string? gameInstallDir, string assetPath)
    {
        if (gameInstallDir is null) return null;
        var full = Path.Combine(gameInstallDir, assetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return null;
        try { return LabeledString.Read(File.ReadAllBytes(full), UpgradeNameKey()); }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// Split <c>data/vehicle/&lt;owner&gt;/part/&lt;category…&gt;/&lt;variant&gt;.upgr</c> into its
    /// category (path under <c>part/</c>) and variant (the filename without extension).
    /// </summary>
    internal static (string category, string variant) SplitPath(string assetPath)
    {
        const string marker = "/part/";
        int i = assetPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var tail = i < 0 ? assetPath : assetPath[(i + marker.Length)..];
        if (tail.EndsWith(".upgr", StringComparison.OrdinalIgnoreCase)) tail = tail[..^5];

        int slash = tail.LastIndexOf('/');
        return slash < 0 ? ("", tail) : (tail[..slash], tail[(slash + 1)..]);
    }

    /// <summary>Turn a snake_case variant/category into Title Case, e.g. <c>racing_clutch → Racing Clutch</c>.</summary>
    internal static string Prettify(string token)
    {
        if (string.IsNullOrEmpty(token)) return token;
        var words = token.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(char.ToUpperInvariant(w[0]));
            sb.Append(w.AsSpan(1));
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"VEHICLE_UPGRADE_NAME_\d+_(\d+)")]
    private static partial Regex UpgradeNameKey();
}
