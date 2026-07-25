using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

/// <summary>
/// M5 — warn-only range validation. An imported value outside the observed stored-unit range for its
/// parameter (<see cref="ParamRanges"/>) is flagged, never blocked.
/// </summary>
public class RangeValidationTests
{
    private const string Backup = "BACKUP_20260722_012434.sgfi";
    private static readonly DateTimeOffset Stamp = new(2026, 7, 23, 0, 0, 0, TimeSpan.Zero);

    private static SaveFile Load() => SaveFile.Parse(Fixtures.Bytes(Backup));

    [Fact]
    public void InRangeImport_ProducesNoRangeWarnings()
    {
        var save = Load();
        var car = save.Cars.Find("Hurricane")!;
        var export = PresetIo.Export(car, car.Find("CALIB")!, Stamp);

        var plan = PresetIo.Plan(save, "Hurricane", "CALIB", export);

        // CALIB came from the game's own sliders, so every value is within the observed range.
        Assert.Empty(plan.RangeWarnings);
    }

    [Fact]
    public void OutOfRangeValue_IsWarnedButStillApplied()
    {
        var save = Load();
        var car = save.Cars.Find("Hurricane")!;
        var preset = car.Find("CALIB")!;
        var brake = preset.Find(0)!; // Braking Balance, exact range 0..1

        // Hand-craft an import that drives Braking Balance far above the game's limit.
        var export = new PresetExport(PresetIo.CurrentFormatVersion, "", new PresetSource("x", "x", "x"),
            [], [new TuningExportValue(0, 9.0f, brake.Aux)]);

        var plan = PresetIo.Plan(save, "Hurricane", "CALIB", export);

        var warning = Assert.Single(plan.RangeWarnings);
        Assert.Equal(0u, warning.ParamIndex);
        Assert.Equal(9.0f, warning.Value);
        Assert.True(warning.IsExact);                    // idx 0 has an exact .ctms schema (0..1)
        Assert.Equal(1f, warning.Max);
        Assert.False(plan.IsEmpty);                      // it is still a real change...
        Assert.Contains(plan.Applied, a => a.ParamIndex == 0 && a.ToValue == 9.0f); // ...and still applied
    }

    [Fact]
    public void ExactSchema_AllowsAValue_ThatTheObservedRangeWouldHaveFlagged()
    {
        var save = Load();
        var car = save.Cars.Find("Hurricane")!;
        var arb = car.Find("CALIB")!.Find(25)!; // Anti-roll bar front: exact 0..100000, observed 17500..80000

        // 90000 is above anything ever *seen* but well within the game's real limit.
        Assert.True(ParamRanges.IsOutsideObserved(25, 90000f, out _)); // the old empirical check would flag it
        var export = new PresetExport(PresetIo.CurrentFormatVersion, "", new PresetSource("x", "x", "x"),
            [], [new TuningExportValue(25, 90000f, arb.Aux)]);

        var plan = PresetIo.Plan(save, "Hurricane", "CALIB", export);

        Assert.Empty(plan.RangeWarnings); // exact schema (0..100000) permits it — no false positive
    }

    [Fact]
    public void TuningSchema_MatchesTheAuxLaw()
    {
        // Braking Pressure 50..150 over 20 steps → aux 9 gives 95, the real Hurricane value.
        var p = TuningSchema.For(1)!;
        Assert.Equal(50f, p.Min);
        Assert.Equal(150f, p.Max);
        Assert.Equal(95f, p.ValueAt(9));

        Assert.True(TuningSchema.IsOutsideExact(1, 200f, out _, out _));   // above 150
        Assert.False(TuningSchema.IsOutsideExact(1, 150f, out _, out _));  // exactly max is fine
        Assert.Null(TuningSchema.For(24)); // springs are relative — no exact schema, falls back to observed
    }

    [Fact]
    public void UnderSampledIndex_IsNotWarnedAgainst()
    {
        // idx 57 is all-zero / under-sampled, so ParamRanges deliberately omits it: any value passes.
        Assert.False(ParamRanges.IsOutsideObserved(57, 999f, out _));
        Assert.Null(ParamRanges.For(57));
    }

    [Fact]
    public void ObservedRanges_AreSaneAndLowerBoundedByRealData()
    {
        // A spot check that the baked table matches the calibration snapshot in docs/PARAM_MAP.md.
        Assert.Equal(new ParamRange(17120f, 84160f, 119), ParamRanges.For(24)); // Springs - front
        Assert.Equal(new ParamRange(696f, 800f, 57), ParamRanges.For(51));      // still-unnamed idx 51
        Assert.True(ParamRanges.For(24)!.Value.IsUsable);
    }
}
