using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Wf2Core;

/// <summary>
/// Exports human-readable car + part data from the game's loose data files into JSON
/// for the guides website. Reads display names/descriptions that the game embeds next to
/// their localization keys (<c>VEHICLE_NAME_&lt;hash&gt;_&lt;len&gt;</c> followed by a
/// u32-length-prefixed literal), and reuses <see cref="PartCatalog"/> for the part walk.
///
/// This touches only loose, uncompressed files (…\data\vehicle) — no save-format or LZ4
/// work is involved.
/// </summary>
public static partial class GuideExporter
{
    public sealed record PartOption(string Variant, string? Name);
    public sealed record PartCategory(string Category, IReadOnlyList<PartOption> Options);
    public sealed record CarExport(
        string Id, string? Name, string? Description, IReadOnlyList<PartCategory> Categories);

    [GeneratedRegex(@"VEHICLE_NAME_\d+_(\d+)")]
    private static partial Regex VehicleNameKey();
    [GeneratedRegex(@"VEHICLE_DESCRIPTION_\d+_(\d+)")]
    private static partial Regex VehicleDescKey();
    [GeneratedRegex(@"VEHICLE_UPGRADE_NAME_\d+_(\d+)")]
    private static partial Regex UpgradeNameKey();

    /// <summary>
    /// Build the export for every <c>carNN</c> folder under <paramref name="vehicleDir"/>.
    /// Skips non-player entries (e.g. shared, motorhome, school_bus) unless they carry a name.
    /// </summary>
    public static IReadOnlyList<CarExport> Build(string vehicleDir)
    {
        var catalog = PartCatalog.Load(vehicleDir);
        var result = new List<CarExport>();

        foreach (var car in catalog.Cars)
        {
            if (car == "shared") continue;
            var carDir = Path.Combine(vehicleDir, car);

            var (name, description) = ReadCarNaming(carDir);

            var categories = catalog.Parts
                .Where(p => p.Car == car)
                .GroupBy(p => p.Category)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new PartCategory(
                    g.Key,
                    g.OrderBy(p => p.Variant, StringComparer.Ordinal)
                        .Select(p => new PartOption(p.Variant, ReadUpgradeName(vehicleDir, p)))
                        .ToList()))
                .ToList();

            // Only keep entries that look like real, named vehicles.
            if (name is null && categories.Count == 0) continue;
            result.Add(new CarExport(car, name, description, categories));
        }
        return result;
    }

    /// <summary>Write cars.json (roster) and parts/&lt;id&gt;.json (per-car catalog) into outDir.</summary>
    public static void Write(string vehicleDir, string outDir)
    {
        var cars = Build(vehicleDir);
        Directory.CreateDirectory(outDir);
        var partsDir = Path.Combine(outDir, "parts");
        Directory.CreateDirectory(partsDir);

        var opts = new JsonSerializerOptions { WriteIndented = true };

        // Roster — light index for authoring reference.
        var roster = cars.Select(c => new { c.Id, c.Name, c.Description, categories = c.Categories.Count }).ToList();
        File.WriteAllText(Path.Combine(outDir, "cars.json"), JsonSerializer.Serialize(roster, opts));

        // Per-car part catalog — consumed by the website.
        foreach (var c in cars)
        {
            var payload = new { c.Id, c.Name, categories = c.Categories };
            File.WriteAllText(Path.Combine(partsDir, $"{c.Id}.json"), JsonSerializer.Serialize(payload, opts));
        }
    }

    private static (string? name, string? description) ReadCarNaming(string carDir)
    {
        // The display name + description live next to their loc keys in career/default.cavs.
        var cavs = Path.Combine(carDir, "career", "default.cavs");
        if (!File.Exists(cavs)) return (null, null);
        var bytes = File.ReadAllBytes(cavs);
        var name = LabeledString.Read(bytes, VehicleNameKey());
        var desc = LabeledString.Read(bytes, VehicleDescKey());
        return (name, desc);
    }

    private static string? ReadUpgradeName(string vehicleDir, CatalogPart part)
    {
        var file = Path.Combine(Path.GetDirectoryName(vehicleDir)!, part.AssetPath + ".upgr");
        if (!File.Exists(file)) return null;
        return LabeledString.Read(File.ReadAllBytes(file), UpgradeNameKey());
    }
}
