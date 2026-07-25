namespace Wf2Core;

/// <summary>An observed value range for one parameter, in stored SI units.</summary>
/// <param name="Min">Smallest value seen.</param>
/// <param name="Max">Largest value seen.</param>
/// <param name="Samples">How many records contributed — low counts mean low confidence.</param>
public readonly record struct ParamRange(float Min, float Max, int Samples)
{
    /// <summary>True when the range is wide enough and well-sampled enough to warn against.</summary>
    public bool IsUsable => Samples >= MinSamplesToWarn && Max > Min;

    /// <summary>Below this many samples we do not trust the range enough to flag values.</summary>
    public const int MinSamplesToWarn = 10;
}

/// <summary>
/// Empirical <b>stored-unit</b> value ranges per <c>paramIndex</c>, from <c>wf2 calibrate</c> over the
/// live save + all fixtures (241 presets, 2026-07-23). Regenerate anytime with that command and paste
/// the min/max back here.
///
/// <para><b>Why not the <c>.ctms</c> min/max?</b> Those are per-parameter offset/percentage scales
/// with car-specific bases — springs store 17120–84160 N/m while <c>suspension_full.ctms</c> says
/// <c>-60→60</c>, so a <c>.ctms</c> bound cannot be compared to a stored value. Observed stored-unit
/// ranges are the only basis that works for validation.</para>
///
/// <para><b>These are a lower bound</b> — they cover what players actually set, not the game's true
/// legal extremes. So a value outside a range is "outside anything seen before", a <i>warning</i>, not
/// proof of illegality. A full in-game min/max sweep would tighten them.</para>
/// </summary>
public static class ParamRanges
{
    private static readonly Dictionary<uint, ParamRange> Ranges = new()
    {
        [0]  = new(0.11f, 1f, 136),
        [1]  = new(75f, 150f, 104),
        [2]  = new(0.45f, 1f, 38),
        [3]  = new(0f, 1f, 38),
        [4]  = new(20f, 150f, 38),
        [14] = new(0.15f, 1f, 72),
        [15] = new(0f, 0.7f, 82),
        [16] = new(10f, 140f, 75),
        [20] = new(560f, 5780f, 39),
        [21] = new(894f, 5536f, 42),
        [22] = new(1832f, 7948f, 36),
        [23] = new(2110f, 6800f, 39),
        [24] = new(17120f, 84160f, 119),
        [25] = new(17500f, 80000f, 115),
        [26] = new(-3.75f, 0f, 126),
        [27] = new(-0.75f, 0.05f, 100),
        [28] = new(894f, 6768f, 39),
        [29] = new(360f, 4904f, 42),
        [30] = new(1962f, 6800f, 42),
        [31] = new(1760f, 5648f, 42),
        [32] = new(12160f, 79600f, 113),
        [33] = new(20000f, 80000f, 113),
        [34] = new(-3f, 0f, 121),
        [35] = new(-0.35f, 0.3f, 87),
        [40] = new(2.2f, 5f, 116),
        [41] = new(2.3016f, 3.2952f, 22),
        [42] = new(1.8048f, 2.688f, 29),
        [43] = new(1.3632f, 2.9088f, 24),
        [44] = new(1.0872f, 3.1848f, 27),
        [45] = new(0.9216f, 4.1232f, 24),
        [46] = new(0.756f, 4.896f, 22),
        [51] = new(696f, 800f, 57),
        [52] = new(11.2f, 13.95f, 44),
        [53] = new(0.1575f, 0.2875f, 132),
        [54] = new(0.16f, 0.3231f, 129),
        [55] = new(0f, 100f, 107),
        [56] = new(0f, 1f, 89),
        // 57 (all 0) and 58 (single 0.5) are degenerate / under-sampled — deliberately omitted so we
        // do not warn against a range we have not actually observed vary.
        [59] = new(0.3333f, 1f, 14),
    };

    /// <summary>The observed range for <paramref name="paramIndex"/>, or null when unknown.</summary>
    public static ParamRange? For(uint paramIndex) =>
        Ranges.TryGetValue(paramIndex, out var r) ? r : null;

    /// <summary>
    /// True when <paramref name="value"/> is outside the usable observed range for
    /// <paramref name="paramIndex"/>. False when in range, or when there is no usable range to judge by
    /// (unknown or under-sampled index) — a warn-only check never blocks on missing data.
    /// </summary>
    public static bool IsOutsideObserved(uint paramIndex, float value, out ParamRange range)
    {
        range = default;
        var r = For(paramIndex);
        if (r is null || !r.Value.IsUsable) return false;
        range = r.Value;
        return value < range.Min || value > range.Max;
    }
}
