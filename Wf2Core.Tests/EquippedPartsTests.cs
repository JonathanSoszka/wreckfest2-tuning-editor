using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

/// <summary>
/// G6 — resolving fitted part paths for display. Name resolution needs the game install (integration,
/// not unit-tested here); the path parsing, grouping, adjustable flag, and fallback naming are pure
/// and covered below.
/// </summary>
public class EquippedPartsTests
{
    [Theory]
    [InlineData("data/vehicle/car02/part/gearbox/adjustable_full_6.upgr", "gearbox", "adjustable_full_6")]
    [InlineData("data/vehicle/shared/part/engine/air_filter/stock.upgr", "engine/air_filter", "stock")]
    [InlineData("data/vehicle/car02/part/engine/stock/parts/cooling/racing_high_flow_radiator.upgr",
                "engine/stock/parts/cooling", "racing_high_flow_radiator")]
    public void SplitPath_SeparatesCategoryAndVariant(string path, string category, string variant)
    {
        var part = Assert.Single(EquippedParts.Resolve([path], gameInstallDir: null));
        Assert.Equal(category, part.Category);
        Assert.Equal(variant, part.Variant);
    }

    [Fact]
    public void Resolve_WithNoInstall_FallsBackToPrettifiedVariant()
    {
        var part = Assert.Single(EquippedParts.Resolve(
            ["data/vehicle/car02/part/engine/stock/parts/cooling/racing_high_flow_radiator.upgr"], null));

        Assert.Null(part.Name);                                    // no install → no resolved name
        Assert.Equal("Racing High Flow Radiator", part.Display);   // …but a readable fallback
        Assert.Equal("Engine / Stock / Parts / Cooling", part.CategoryLabel);
    }

    [Theory]
    [InlineData("data/vehicle/car02/part/gearbox/adjustable_full_6.upgr", PartGroup.Performance)]
    [InlineData("data/vehicle/car02/part/brakes/adjustable_brakes_disc_14.upgr", PartGroup.Performance)]
    [InlineData("data/vehicle/car02/part/suspension/race_springs.upgr", PartGroup.Performance)]
    [InlineData("data/vehicle/car02/part/door/door_l_default.upgr", PartGroup.Other)]
    [InlineData("data/vehicle/car02/part/livery/livery01.upgr", PartGroup.Other)]
    public void Resolve_GroupsPerformanceApartFromCosmetic(string path, PartGroup group)
    {
        Assert.Equal(group, EquippedParts.Resolve([path], null).Single().Group);
    }

    [Theory]
    [InlineData("data/vehicle/car02/part/roll_bar/front_antiroll_bar_adjustable.upgr", true)]
    [InlineData("data/vehicle/car02/part/roll_bar/front_antiroll_bar_stock.upgr", false)]
    public void Resolve_FlagsAdjustableParts(string path, bool adjustable)
    {
        Assert.Equal(adjustable, EquippedParts.Resolve([path], null).Single().IsAdjustable);
    }

    [Fact]
    public void Resolve_MapsEveryPathOfACar()
    {
        var save = SaveFile.Parse(Fixtures.Bytes("BACKUP_20260722_012434.sgfi"));
        var car = save.Cars.First(c => c.Parts.Count > 0);

        var resolved = EquippedParts.Resolve(car, gameInstallDir: null);

        Assert.Equal(car.Parts.Count, resolved.Count);
        Assert.All(resolved, p => Assert.NotEmpty(p.Display));   // every part shows *something*
    }
}
