using System.Buffers.Binary;
using System.Text;

namespace Wf2Core;

/// <summary>
/// A Bugbear "bbag" container: a 20-byte header followed by a raw LZ4 block. The same layout is
/// used by <c>.sgfi</c> saves, <c>.upgr</c> parts, <c>.ctms</c> tuning definitions <em>and</em> by
/// the containers nested inside a save's chunk chain.
///
/// <code>
/// offset  size  field
/// 0x00    u32   RootValue         always 7
/// 0x04    4CC   tag               stored reversed: 'ifgs'=sgfi, 'srcc'=ccrs, 'smtc'=ctms
/// 0x08    u32   reserved          varies per container; preserved verbatim
/// 0x0C    u32   compressedLength  == payload length (total size - 20)
/// 0x10    u32   CRC-32C of the DECOMPRESSED payload
/// 0x14    ...   raw LZ4 block (no stored size)
/// </code>
///
/// The header CRC at 0x10 is <b>not</b> cosmetic: a container whose CRC does not match its content
/// is silently discarded by the game (for the cars container that is the classic "loads but strips
/// every car and tune" symptom). <see cref="Serialize"/> always recomputes both the length and the
/// CRC, so a caller can never forget.
/// </summary>
public sealed class BbagContainer
{
    /// <summary>Fixed header size in bytes; the LZ4 payload begins at this offset.</summary>
    public const int HeaderSize = 20;

    /// <summary>The value observed at offset 0x00 in every known container.</summary>
    public const uint DefaultRootValue = 7;

    private readonly byte[]? _originalContent;
    private readonly byte[]? _originalCompressed;
    private byte[] _content;

    private BbagContainer(uint rootValue, string tag, uint reserved, byte[] content,
                          uint storedCrc, int storedCompressedLength, byte[]? originalCompressed)
    {
        RootValue = rootValue;
        Tag = tag;
        Reserved = reserved;
        _content = content;
        StoredCrc = storedCrc;
        StoredCompressedLength = storedCompressedLength;
        StoredCrcValid = Crc32C.Compute(content) == storedCrc;
        _originalContent = (byte[])content.Clone();
        _originalCompressed = originalCompressed;
    }

    /// <summary>uint32 at 0x00. Always 7 in observed files.</summary>
    public uint RootValue { get; set; }

    /// <summary>The 4CC tag at 0x04 exactly as stored (reversed), e.g. <c>"srcc"</c>.</summary>
    public string Tag { get; }

    /// <summary>uint32 at 0x08. Meaning unknown; preserved verbatim.</summary>
    public uint Reserved { get; set; }

    /// <summary>
    /// The container's payload exactly as it was read, still compressed, or <c>null</c> if this
    /// container was created rather than parsed.
    ///
    /// <para>Needed because a payload may continue into further LZ4 blocks held in the chunk
    /// trailer; those blocks back-reference this one's output and can only be decoded by
    /// concatenating the compressed bytes (see <see cref="Lz4Block.DecodeChained"/>).</para>
    /// </summary>
    internal byte[]? OriginalCompressed => _originalCompressed;

    /// <summary>
    /// The decompressed payload — the form to read and edit. Never patch the compressed bytes:
    /// LZ4 literals are reused as back-reference sources by later matches, so a byte poke inside
    /// the stream silently corrupts unrelated data further along.
    /// </summary>
    public byte[] Content
    {
        get => _content;
        set => _content = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The CRC-32C stored at 0x10 when this container was parsed.</summary>
    public uint StoredCrc { get; }

    /// <summary>True when <see cref="StoredCrc"/> matched the payload at parse time.</summary>
    public bool StoredCrcValid { get; }

    /// <summary>The compressed length declared at 0x0C when this container was parsed.</summary>
    public int StoredCompressedLength { get; }

    /// <summary>True once <see cref="Content"/> differs from the bytes this container was parsed from.</summary>
    public bool IsModified => _originalContent is null || !_content.AsSpan().SequenceEqual(_originalContent);

    /// <summary>Recompute the CRC-32C of the current <see cref="Content"/>.</summary>
    public uint ComputeCrc() => Crc32C.Compute(_content);

    /// <summary>
    /// Parse a container that occupies <paramref name="data"/> exactly (a whole file).
    /// </summary>
    /// <exception cref="SgfiFormatException">Malformed header, length mismatch or bad LZ4.</exception>
    public static BbagContainer Parse(ReadOnlySpan<byte> data)
    {
        var container = ParseAt(data, 0, out int consumed);
        if (consumed != data.Length)
            throw new SgfiFormatException(
                $"Container declares {consumed - HeaderSize} payload bytes but {data.Length - HeaderSize} follow the header.");
        return container;
    }

    /// <summary>
    /// Parse a container starting at <paramref name="offset"/> inside a larger buffer (the nested
    /// case). <paramref name="bytesConsumed"/> receives the container's total size, header included.
    /// </summary>
    /// <exception cref="SgfiFormatException">Malformed header, truncated payload or bad LZ4.</exception>
    public static BbagContainer ParseAt(ReadOnlySpan<byte> data, int offset, out int bytesConsumed)
    {
        if (offset < 0 || offset + HeaderSize > data.Length)
            throw new SgfiFormatException($"Container header at 0x{offset:X} runs past the end of the buffer.");

        var header = data.Slice(offset, HeaderSize);
        uint rootValue = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        string tag = Encoding.Latin1.GetString(header[4..8]);
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]);
        uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(header[12..16]);
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]);

        if (compressedLength == 0 || offset + HeaderSize + compressedLength > (uint)data.Length)
            throw new SgfiFormatException(
                $"Container '{tag}' at 0x{offset:X} declares {compressedLength} compressed bytes, " +
                $"but only {data.Length - offset - HeaderSize} remain.");

        var compressed = data.Slice(offset + HeaderSize, (int)compressedLength);
        byte[] content = Lz4Block.Decode(compressed, $"container '{tag}' at 0x{offset:X}");

        bytesConsumed = HeaderSize + (int)compressedLength;
        return new BbagContainer(rootValue, tag, reserved, content, crc, (int)compressedLength, compressed.ToArray());
    }

    /// <summary>
    /// Create a brand-new container around <paramref name="content"/>.
    /// </summary>
    public static BbagContainer Create(string tag, byte[] content, uint reserved = 0,
                                       uint rootValue = DefaultRootValue)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(content);
        if (Encoding.Latin1.GetByteCount(tag) != 4)
            throw new ArgumentException($"Container tag must be exactly 4 bytes, got '{tag}'.", nameof(tag));
        return new BbagContainer(rootValue, tag, reserved, content, Crc32C.Compute(content), 0, null);
    }

    /// <summary>
    /// Serialize header + LZ4 payload, recomputing <c>compressedLength</c> (0x0C) and the payload
    /// CRC-32C (0x10) from the current <see cref="Content"/>.
    ///
    /// When the content is unchanged the container re-emits the exact compressed bytes it was
    /// parsed from, which keeps an untouched save byte-identical. Once the content changes the
    /// payload is re-encoded, and the new compressed size is very unlikely to match the original —
    /// see the size-change caveat on <see cref="SaveFile.Serialize"/>.
    /// </summary>
    public byte[] Serialize()
    {
        byte[] payload = !IsModified && _originalCompressed is not null
            ? _originalCompressed
            : Lz4Block.Encode(_content);

        var buffer = new byte[HeaderSize + payload.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], RootValue);
        Encoding.Latin1.GetBytes(Tag, span[4..8]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..12], Reserved);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..16], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..20], Crc32C.Compute(_content));
        payload.CopyTo(span[HeaderSize..]);
        return buffer;
    }
}
