using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

/// <summary>
/// The base→stored arithmetic, cracked 2026-07-23.
///
/// <para>A preset's <c>aux</c> word is the <b>slider position</b>, and the stored value is derived
/// from it: <c>value = min + aux × (max − min) / steps</c>, where min/max/steps all come from the
/// part's <c>.ctms</c> schema (<c>steps</c> being a u32 right after max that we had not decoded).
/// These tests assert the law holds for every record in every fixture save.</para>
/// </summary>
public class TuningArithmeticTests
{
    /// <summary>
    /// paramIndex → (min, stepSize) for the <c>armt</c> (absolute-unit) parameters, taken from the
    /// game's own .ctms schemas. Relative parameters (springs/dampers = <c>prmt</c>, ride height =
    /// <c>rrmt</c>) are deliberately absent: their min/max scale off the fitted part's base, so the
    /// same law applies but with car-specific bounds.
    /// </summary>
    private static readonly Dictionary<uint, (float Min, float Step)> Schema = new()
    {
        [0]  = (0f, 0.01f),    // Braking Balance      brakes_full        0..1     /100
        [1]  = (50f, 5f),      // Braking Pressure     brakes_full        50..150  /20
        [3]  = (0f, 0.05f),    // Front Diff power     differential_fwd   0..1     /20
        [4]  = (5f, 5f),       // Front Diff preload   differential_fwd   5..150   /29
        [14] = (0f, 0.05f),    // Differential power   differential_rwd   0..1     /20
        [15] = (0f, 0.05f),    // Differential coast   differential_rwd   0..1     /20
        [16] = (5f, 5f),       // Differential preload differential_rwd   5..150   /29
        [25] = (0f, 2500f),    // Anti-roll bar front  anti_roll_bar      0..100000/40
        [26] = (-5f, 0.25f),   // Front Camber         suspension_full   -5..2     /28
        [27] = (-2f, 0.05f),   // Front Toe            suspension_full   -2..2     /80
        [33] = (0f, 2500f),    // Anti-roll bar rear   anti_roll_bar      0..100000/40
        [34] = (-5f, 0.25f),   // Rear Camber          suspension_full   -5..2     /28
        [35] = (-2f, 0.05f),   // Rear Toe             suspension_full   -2..2     /80
        [40] = (2.2f, 0.1f),   // Gearbox final drive  gearbox            2.2..6.1 /39
        [55] = (0f, 5f),       // Ackerman             suspension_full    0..100   /20
    };

    /// <summary>
    /// For every stored record of a known absolute parameter, the value must equal
    /// <c>min + aux × step</c> exactly (to float precision).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllSaves))]
    public void StoredValue_IsAlwaysMinPlusAuxTimesStep(string saveName)
    {
        var save = SaveFile.Parse(Fixtures.Bytes(saveName));
        int checkedCount = 0;

        foreach (var car in save.Cars)
            foreach (var preset in car.Presets)
                foreach (var t in preset.Tuning)
                {
                    if (!Schema.TryGetValue(t.ParamIndex, out var s)) continue;
                    if (!float.IsFinite(t.Value) || t.Value <= -3.0e38f) continue;  // unset sentinel
                    if (t.Aux > 4_000_000_000) continue;                            // sentinel aux

                    float expected = s.Min + t.Aux * s.Step;
                    Assert.True(Math.Abs(expected - t.Value) <= Math.Max(1e-3, Math.Abs(t.Value) * 1e-5),
                        $"{car.Name}/{preset.Name} idx {t.ParamIndex}: aux {t.Aux} → expected {expected}, stored {t.Value}");
                    checkedCount++;
                }

        Assert.True(checkedCount > 0, "no records matched the known schema");
    }

    public static IEnumerable<object[]> AllSaves() => Fixtures.AllSgfi();

    [Fact]
    public void TuningParameter_ExposesStepSizeAndValueAt()
    {
        // Braking Pressure: 50..150 over 20 steps.
        var p = new TuningParameter("armt", 50f, 150f, 20);
        Assert.Equal(5f, p.StepSize);
        Assert.Equal(95f, p.ValueAt(9));   // a real stored value from the Hurricane
        Assert.True(p.IsAbsolute);

        // Springs are relative: same arithmetic, but min/max are a ±% offset, not stored units.
        Assert.False(new TuningParameter("prmt", -60f, 60f, 20).IsAbsolute);
    }
}
