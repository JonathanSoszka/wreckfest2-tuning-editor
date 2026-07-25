namespace Wf2Core;

/// <summary>
/// What one <c>paramIndex</c> means: a display name, the unit the file stores, and the factor that
/// converts the stored value to the number the UI shows.
/// </summary>
/// <param name="Name">Human name, e.g. <c>"Braking Balance"</c>.</param>
/// <param name="StoredUnit">The SI unit actually stored, e.g. <c>"N/m"</c>. Empty when unitless.</param>
/// <param name="DisplayUnit">The unit the UI labels it with, e.g. <c>"kN/m"</c>. Empty when unitless.</param>
/// <param name="DisplayFactor">Multiply the stored value by this to get the UI number.</param>
/// <param name="Confirmed">
/// False when the mapping is inferred rather than observed. See the caveats in
/// <c>docs/PARAM_MAP.md</c> — index 56 in particular is only probable.
/// </param>
public sealed record ParamInfo(string Name, string StoredUnit, string DisplayUnit,
                               double DisplayFactor, bool Confirmed)
{
    /// <summary>The stored value rendered the way the UI would show it, e.g. <c>"47.7 kN/m"</c>.</summary>
    public string Display(float storedValue)
    {
        double n = storedValue * DisplayFactor;
        string unit = DisplayUnit.Length == 0 ? "" : " " + DisplayUnit;
        return $"{n:0.####}{unit}";
    }
}

/// <summary>
/// The canonical <c>paramIndex</c> → parameter map, transcribed from <c>docs/PARAM_MAP.md</c> (the
/// CALIB calibration run on the Hurricane, 2026-07-22).
///
/// <para><b>This is informational only.</b> Everything that actually edits a save keys off the
/// numeric <c>paramIndex</c>. Names exist so exported JSON is readable and so a diff means something
/// to a human — they are regenerated on read and never trusted on import. A parameter missing from
/// this table is still perfectly editable; it just has no friendly name yet.</para>
///
/// <para><b>Units.</b> Stored values are physical SI — metres, N/m, N·s/m — and the game can display
/// metric or imperial. A number read off the screen is therefore never the stored number. See
/// <see cref="ParamInfo.DisplayFactor"/>.</para>
/// </summary>
public static class ParamMap
{
    private static readonly Dictionary<uint, ParamInfo> Map = new()
    {
        [0]  = new("Braking Balance",           "",     "",       100,   true),
        [1]  = new("Braking Pressure",          "",     "",       1,     true),
        [2]  = new("Front Balancer",            "",     "",       1,     false),
        // 3 & 4 appear only on FWD cars (Crusader, Phaser), always together, never with the RWD
        // differential (14/15/16) — the front-differential signature. idx 4's 20–150 range matches
        // differential_fwd_full.ctms 5→150. Category is certain; the exact power-vs-lock label is not.
        [3]  = new("Front Differential - power",   "",  "",       100,   false),
        [4]  = new("Front Differential - preload", "",  "",       1,     false),
        [14] = new("Differential - power",      "",     "",       100,   true),
        [15] = new("Differential - coast",      "",     "",       100,   true),
        [16] = new("Differential - preload",    "",     "",       1,     true),
        [20] = new("Front Bump (low speed)",    "N.s/m", "kN.s/m", 0.001, true),
        [21] = new("Front Bump (high speed)",   "N.s/m", "kN.s/m", 0.001, true),
        [22] = new("Front Rebound (low speed)", "N.s/m", "kN.s/m", 0.001, true),
        [23] = new("Front Rebound (high speed)","N.s/m", "kN.s/m", 0.001, true),
        [24] = new("Springs - front",           "N/m",   "kN/m",   0.001, true),
        [25] = new("Anti-roll bar - front",     "N/m",   "kN/m",   0.001, true),
        [26] = new("Front Camber",              "deg",   "deg",    1,     true),
        [27] = new("Front Toe",                 "deg",   "deg",    1,     true),
        [28] = new("Rear Bump (low speed)",     "N.s/m", "kN.s/m", 0.001, true),
        [29] = new("Rear Bump (high speed)",    "N.s/m", "kN.s/m", 0.001, true),
        [30] = new("Rear Rebound (low speed)",  "N.s/m", "kN.s/m", 0.001, true),
        [31] = new("Rear Rebound (high speed)", "N.s/m", "kN.s/m", 0.001, true),
        [32] = new("Springs - rear",            "N/m",   "kN/m",   0.001, true),
        [33] = new("Anti-roll bar - rear",      "N/m",   "kN/m",   0.001, true),
        [34] = new("Rear Camber",               "deg",   "deg",    1,     false),
        [35] = new("Rear Toe",                  "deg",   "deg",    1,     true),
        [40] = new("Gearbox - final drive",     "",      "",       1,     true),
        [41] = new("Gearbox - gear 1",          "",      "",       1,     true),
        [42] = new("Gearbox - gear 2",          "",      "",       1,     true),
        [43] = new("Gearbox - gear 3",          "",      "",       1,     true),
        [44] = new("Gearbox - gear 4",          "",      "",       1,     true),
        [45] = new("Gearbox - gear 5",          "",      "",       1,     true),
        [46] = new("Gearbox - gear 6",          "",      "",       1,     true),
        [53] = new("Ride Height - front",       "m",     "cm",     100,   true),
        [54] = new("Ride Height - rear",        "m",     "cm",     100,   true),
        [55] = new("Ackerman",                  "",      "%",      1,     true),
        [56] = new("Oversteer Bias",            "",      "",       1,     false),
    };

    /// <summary>Every mapped index, ascending.</summary>
    public static IEnumerable<uint> KnownIndices => Map.Keys.OrderBy(k => k);

    /// <summary>The parameter's details, or null when the index is not in the map.</summary>
    public static ParamInfo? Lookup(uint paramIndex) =>
        Map.TryGetValue(paramIndex, out var info) ? info : null;

    /// <summary>The parameter's name, or <c>"parameter N"</c> when unmapped.</summary>
    public static string NameOf(uint paramIndex) =>
        Lookup(paramIndex)?.Name ?? $"parameter {paramIndex}";

    /// <summary>The stored value as the UI would render it, or the bare number when unmapped.</summary>
    public static string DisplayOf(uint paramIndex, float storedValue) =>
        Lookup(paramIndex)?.Display(storedValue) ?? storedValue.ToString("0.####");
}
