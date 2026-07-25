using System.Linq;

namespace Wf2Core;

/// <summary>
/// The <b>exact</b> per-parameter schema, for the absolute (<c>armt</c>) parameters whose
/// <c>.ctms</c> definition we have pinned. Because the stored value is
/// <c>min + aux × (max − min) / steps</c> (see <c>docs/PARAM_MAP.md</c> "The arithmetic — SOLVED"),
/// these give the exact legal range and on-step values — no empirical guessing.
///
/// <para>The tuning <c>.ctms</c> files are shared across cars, so an absolute parameter's range is a
/// fixed game constant; this table is baked from them. <b>Relative</b> parameters (springs/dampers =
/// <c>prmt</c>, ride height = <c>rrmt</c>) are deliberately absent: their min/max are offsets against
/// a car-specific base we have not cracked, so no fixed range exists — those fall back to the observed
/// ranges in <see cref="ParamRanges"/>.</para>
/// </summary>
public static class TuningSchema
{
    // paramIndex → schema (all armt / absolute). Values from the game's shared .ctms, cross-checked
    // against every stored record by TuningArithmeticTests.
    private static readonly Dictionary<uint, TuningParameter> Map = new()
    {
        [0]  = A(0f, 1f, 100),        // Braking Balance          brakes_full
        [1]  = A(50f, 150f, 20),      // Braking Pressure         brakes_full
        [3]  = A(0f, 1f, 20),         // Front Diff — power       differential_fwd_full
        [4]  = A(5f, 150f, 29),       // Front Diff — preload     differential_fwd_full
        [14] = A(0f, 1f, 20),         // Differential — power     differential_rwd_full
        [15] = A(0f, 1f, 20),         // Differential — coast     differential_rwd_full
        [16] = A(5f, 150f, 29),       // Differential — preload   differential_rwd_full
        [25] = A(0f, 100000f, 40),    // Anti-roll bar — front    anti_roll_bar_front
        [26] = A(-5f, 2f, 28),        // Front Camber             suspension_full
        [27] = A(-2f, 2f, 80),        // Front Toe                suspension_full
        [33] = A(0f, 100000f, 40),    // Anti-roll bar — rear     anti_roll_bar_rear
        [34] = A(-5f, 2f, 28),        // Rear Camber              suspension_full
        [35] = A(-2f, 2f, 80),        // Rear Toe                 suspension_full
        [40] = A(2.2f, 6.1f, 39),     // Gearbox — final drive    gearbox_*_full
        [41] = A(0.48f, 6f, 100),     // Gearbox — gear 1         gearbox_*_full
        [42] = A(0.48f, 6f, 100),     // Gearbox — gear 2
        [43] = A(0.48f, 6f, 100),     // Gearbox — gear 3
        [44] = A(0.48f, 6f, 100),     // Gearbox — gear 4
        [45] = A(0.48f, 6f, 100),     // Gearbox — gear 5
        [46] = A(0.48f, 6f, 100),     // Gearbox — gear 6
        [55] = A(0f, 100f, 20),       // Ackerman                 suspension_full
    };

    private static TuningParameter A(float min, float max, uint steps) => new("armt", min, max, steps);

    /// <summary>Every parameter index with an exact schema — i.e. everything that can be edited on a
    /// slider, whether or not the preset currently stores it. Ascending order.</summary>
    public static IReadOnlyList<uint> EditableIndices { get; } = Map.Keys.OrderBy(k => k).ToArray();

    /// <summary>The exact schema for <paramref name="paramIndex"/>, or null when it is unknown or relative.</summary>
    public static TuningParameter? For(uint paramIndex) =>
        Map.TryGetValue(paramIndex, out var p) ? p : null;

    /// <summary>
    /// True when <paramref name="value"/> falls outside the exact legal range for
    /// <paramref name="paramIndex"/>. Returns false (with no bounds) when there is no exact schema —
    /// the caller should fall back to <see cref="ParamRanges"/>.
    /// </summary>
    public static bool IsOutsideExact(uint paramIndex, float value, out float min, out float max)
    {
        min = 0; max = 0;
        var p = For(paramIndex);
        if (p is null) return false;
        min = p.Min;
        max = p.Max;
        // A small tolerance so a legitimate min/max value is never flagged by float rounding.
        float eps = Math.Max(1e-4f, (max - min) * 1e-5f);
        return value < min - eps || value > max + eps;
    }
}
