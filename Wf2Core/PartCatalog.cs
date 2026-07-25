namespace Wf2Core;

/// <summary>One installable part from the game data: which car, category, variant, and asset path.</summary>
/// <param name="Car">Vehicle folder name, e.g. "car01", or "shared" for shared parts.</param>
/// <param name="Category">Category path under <c>part/</c>, e.g. "clutch" or "engine/stock/parts/cooling".</param>
/// <param name="Variant">Variant name (the .upgr filename without extension), e.g. "clutch_racing".</param>
/// <param name="AssetPath">Forward-slash asset path relative to <c>data/</c> without the .upgr extension.</param>
public sealed record CatalogPart(string Car, string Category, string Variant, string AssetPath);

/// <summary>
/// The authoritative part catalog, read straight from the game's loose data files —
/// <c>&lt;install&gt;\data\vehicle\{carNN,shared}\part\&lt;category&gt;\&lt;variant&gt;.upgr</c>.
/// No archive parsing needed. Powers the editor's per-car / per-category part choices.
/// </summary>
public sealed class PartCatalog
{
    private readonly List<CatalogPart> _parts;

    private PartCatalog(List<CatalogPart> parts) => _parts = parts;

    public IReadOnlyList<CatalogPart> Parts => _parts;

    /// <summary>All car folder names present (e.g. car01…car19, shared), sorted.</summary>
    public IReadOnlyList<string> Cars =>
        _parts.Select(p => p.Car).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

    /// <summary>Variants available for a given car + category (the swap options for a slot).</summary>
    public IReadOnlyList<CatalogPart> Variants(string car, string category) =>
        _parts.Where(p => p.Car == car && p.Category == category).ToList();

    /// <summary>
    /// Load the catalog by walking <paramref name="vehicleDir"/> (…\data\vehicle) for *.upgr files.
    /// </summary>
    public static PartCatalog Load(string vehicleDir)
    {
        if (!Directory.Exists(vehicleDir))
            throw new DirectoryNotFoundException($"Vehicle data folder not found: {vehicleDir}");

        var parts = new List<CatalogPart>();
        foreach (var car in Directory.EnumerateDirectories(vehicleDir))
        {
            var carName = Path.GetFileName(car);
            var partDir = Path.Combine(car, "part");
            if (!Directory.Exists(partDir))
                continue;

            foreach (var file in Directory.EnumerateFiles(partDir, "*.upgr", SearchOption.AllDirectories))
            {
                // category = directory path under part/, using forward slashes
                var relFromPart = Path.GetRelativePath(partDir, Path.GetDirectoryName(file)!).Replace('\\', '/');
                var variant = Path.GetFileNameWithoutExtension(file);
                var assetPath = Path.GetRelativePath(Path.GetDirectoryName(vehicleDir)!, file)
                    .Replace('\\', '/');
                if (assetPath.EndsWith(".upgr", StringComparison.OrdinalIgnoreCase))
                    assetPath = assetPath[..^5];
                parts.Add(new CatalogPart(carName, relFromPart, variant, assetPath));
            }
        }
        return new PartCatalog(parts);
    }
}
