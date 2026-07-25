using System.Buffers.Binary;

namespace Wf2Core;

/// <summary>
/// One tunable parameter declared by a .ctms tuning definition: its node type and the
/// inclusive value range the game accepts.
/// </summary>
/// <param name="Type">
/// Node 4CC as stored. <c>armt</c> = absolute (min/max are the real stored units);
/// <c>prmt</c> / <c>rrmt</c> = relative to the fitted part's base, so their min/max are an offset
/// range (e.g. −60…+60 %) rather than stored units.
/// </param>
/// <param name="Min">Minimum accepted value (slider position 0).</param>
/// <param name="Max">Maximum accepted value (slider position <paramref name="Steps"/>).</param>
/// <param name="Steps">
/// Number of slider increments between <paramref name="Min"/> and <paramref name="Max"/>. A preset's
/// <c>aux</c> word is the slider position, so the stored value is
/// <c>Min + aux × (Max − Min) / Steps</c> — see <see cref="StepSize"/> / <see cref="ValueAt"/>.
/// </param>
public sealed record TuningParameter(string Type, float Min, float Max, uint Steps)
{
    /// <summary>True for <c>armt</c>: min/max are already in stored units.</summary>
    public bool IsAbsolute => Type == "armt";

    /// <summary>The value change per slider increment, <c>(Max − Min) / Steps</c>.</summary>
    public float StepSize => Steps == 0 ? 0 : (Max - Min) / Steps;

    /// <summary>
    /// The value the game stores for slider position <paramref name="aux"/>. Exact for
    /// <see cref="IsAbsolute"/> parameters; for relative ones the same arithmetic applies but against
    /// the car's base-derived min/max, not these.
    /// </summary>
    public float ValueAt(uint aux) => Min + aux * StepSize;
}

/// <summary>
/// A decoded <c>.ctms</c> tuning definition — the game's authoritative schema for what is tunable
/// on an adjustable part and within what limits. Adjustable parts (and only those) reference one
/// of these via an <c>smtc</c> node, which is what gates tuning in the garage.
///
/// Container layout (shared with .sgfi and .upgr):
///   [u32 RootValue=7][4CC tag][u32 reserved][u32 compressedLength][u32 CRC-32C of decompressed][LZ4 payload]
///
/// Decompressed payload layout:
///   "cmtc" [u32 version] [u32 parameterCount] [u32 ...]
///   then parameterCount records, each: [4CC type] ... [f32 min @ tag+0x0C] [f32 max @ tag+0x10] ...
/// </summary>
public sealed class CtmsFile
{
    public const int HeaderSize = 20;
    /// <summary>Root tag of a tuning definition ("ctms" stored reversed).</summary>
    public static readonly byte[] Magic = "smtc"u8.ToArray();

    public uint RootValue { get; private init; }
    public uint Reserved { get; private init; }
    /// <summary>CRC-32C stored in the header, over the decompressed payload.</summary>
    public uint Crc { get; private init; }
    /// <summary>True when the stored CRC matches the decompressed payload.</summary>
    public bool CrcValid { get; private init; }
    /// <summary>Parameter count declared in the <c>cmtc</c> header.</summary>
    public int DeclaredParameterCount { get; private init; }
    public IReadOnlyList<TuningParameter> Parameters { get; private init; } = [];

    public static CtmsFile Load(string path) => Parse(File.ReadAllBytes(path));

    public static CtmsFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new SgfiFormatException($"Too small to be a .ctms file ({data.Length} bytes).");

        uint root = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        var tag = data[4..8];
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(data[8..12]);
        uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(data[12..16]);
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(data[16..20]);

        if (!tag.SequenceEqual(Magic))
            throw new SgfiFormatException($"Not a .ctms file: tag is '{System.Text.Encoding.ASCII.GetString(tag)}', expected 'smtc'.");
        if (compressedLength != data.Length - HeaderSize)
            throw new SgfiFormatException(
                $"Header length {compressedLength} != actual payload {data.Length - HeaderSize}.");

        byte[] payload = Lz4Block.Decode(data[HeaderSize..], "the .ctms payload");
        var parameters = ParseParameters(payload, out int declared);

        return new CtmsFile
        {
            RootValue = root,
            Reserved = reserved,
            Crc = crc,
            CrcValid = Crc32C.Compute(payload) == crc,
            DeclaredParameterCount = declared,
            Parameters = parameters,
        };
    }

    private static List<TuningParameter> ParseParameters(ReadOnlySpan<byte> d, out int declared)
    {
        declared = d.Length >= 12 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(d[8..12]) : 0;

        var list = new List<TuningParameter>();
        // Walk 4-byte words; every lowercase 4CC other than the "cmtc" container starts a parameter
        // record whose min/max/steps sit at fixed offsets from the tag.
        for (int i = 0; i + 0x18 <= d.Length; i += 4)
        {
            var w = d.Slice(i, 4);
            if (!IsLowerTag(w)) continue;
            if (w.SequenceEqual("cmtc"u8)) continue;

            float min = BitConverter.ToSingle(d.Slice(i + 0x0C, 4));
            float max = BitConverter.ToSingle(d.Slice(i + 0x10, 4));
            uint steps = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(i + 0x14, 4));
            list.Add(new TuningParameter(System.Text.Encoding.ASCII.GetString(w), min, max, steps));
        }
        return list;
    }

    private static bool IsLowerTag(ReadOnlySpan<byte> w)
    {
        foreach (byte b in w)
            if (b is < (byte)'a' or > (byte)'z') return false;
        return true;
    }
}
