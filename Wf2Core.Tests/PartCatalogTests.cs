namespace Wf2Core.Tests;

/// <summary>Catalog reader over a synthetic data/vehicle tree (mirrors the real layout).</summary>
public class PartCatalogTests : IDisposable
{
    private readonly string _root;      // stands in for …\data
    private readonly string _vehicleDir;

    public PartCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wf2cat_" + Guid.NewGuid().ToString("N"));
        _vehicleDir = Path.Combine(_root, "vehicle");
        // car01: clutch {stock,sport,racing}; nested cooling radiators
        MakeUpgr("car01/part/clutch/clutch_stock");
        MakeUpgr("car01/part/clutch/clutch_sport");
        MakeUpgr("car01/part/clutch/clutch_racing");
        MakeUpgr("car01/part/engine/stock/parts/cooling/stock_radiator");
        MakeUpgr("car01/part/engine/stock/parts/cooling/derby_reinforced_radiator");
        // shared parts
        MakeUpgr("shared/part/engine/air_filter/stock");
        MakeUpgr("shared/part/engine/air_filter/sport");
    }

    private void MakeUpgr(string relUnderVehicle)
    {
        var path = Path.Combine(_vehicleDir, relUnderVehicle.Replace('/', Path.DirectorySeparatorChar) + ".upgr");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void Load_FindsCars_AndSharedFolder()
    {
        var cat = PartCatalog.Load(_vehicleDir);
        Assert.Contains("car01", cat.Cars);
        Assert.Contains("shared", cat.Cars);
    }

    [Fact]
    public void Variants_ListsSwapOptionsForACategory()
    {
        var cat = PartCatalog.Load(_vehicleDir);
        var clutch = cat.Variants("car01", "clutch").Select(p => p.Variant).OrderBy(v => v).ToList();
        Assert.Equal(new[] { "clutch_racing", "clutch_sport", "clutch_stock" }, clutch);
    }

    [Fact]
    public void AssetPath_IsDataRelative_WithoutExtension()
    {
        var cat = PartCatalog.Load(_vehicleDir);
        var rad = cat.Parts.Single(p => p.Variant == "derby_reinforced_radiator");
        Assert.Equal("vehicle/car01/part/engine/stock/parts/cooling/derby_reinforced_radiator", rad.AssetPath);
        Assert.Equal("engine/stock/parts/cooling", rad.Category);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
