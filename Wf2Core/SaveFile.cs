using System.Buffers.Binary;
using System.Text;

namespace Wf2Core;

/// <summary>
/// One link in a save's chunk chain: a nested <see cref="BbagContainer"/> plus any sibling bytes
/// that follow it inside the same chunk (the cars chunk carries ~442 such bytes).
/// </summary>
public sealed class SaveChunk
{
    /// <summary>
    /// Maximum decompressed bytes per LZ4 block. The game emits large payloads in 64 KiB blocks;
    /// we match that so a rewritten save keeps the same shape.
    /// </summary>
    public const int BlockSize = 64 * 1024;

    internal SaveChunk(BbagContainer container, byte[] trailer, uint storedCrc, bool storedCrcValid)
    {
        Container = container;
        Trailer = trailer;
        StoredCrc = storedCrc;
        StoredCrcValid = storedCrcValid;
    }

    /// <summary>The container this chunk opens with.</summary>
    public BbagContainer Container { get; }

    /// <summary>
    /// Sibling node bytes between the end of <see cref="Container"/> and the chunk's CRC field.
    /// Not yet decoded; preserved verbatim.
    /// </summary>
    public byte[] Trailer { get; set; }

    /// <summary>The chunk CRC-32C as stored when the save was parsed.</summary>
    public uint StoredCrc { get; }

    /// <summary>True when <see cref="StoredCrc"/> matched the chunk bytes at parse time.</summary>
    public bool StoredCrcValid { get; }

    /// <summary>The container's 4CC tag, e.g. <c>"srcc"</c>.</summary>
    public string Tag => Container.Tag;

    /// <summary>
    /// Number of bytes of <see cref="DecodedPayload"/> that live in <see cref="Container"/> itself
    /// (i.e. the first LZ4 block). Bytes at or beyond this offset live in a continuation block inside
    /// <see cref="Trailer"/>; both are writable via <see cref="SetDecodedPayload"/>.
    /// </summary>
    public int ContainerPayloadLength => Container.Content.Length;

    /// <summary>
    /// The chunk's <b>complete</b> logical payload, including any continuation LZ4 blocks stored in
    /// <see cref="Trailer"/>.
    ///
    /// <para>The game emits a large payload as a chain of ~64 KiB output blocks: the first sits in
    /// the container, later ones follow in the trailer framed as
    /// <c>[u32 compressedLength][u32 unknown][compressed bytes]</c>. Later blocks back-reference the
    /// earlier blocks' output, so the compressed bytes must be decoded as one stream.</para>
    ///
    /// <para><b>Why this matters:</b> <see cref="BbagContainer.Content"/> is only the first block.
    /// For a save whose cars payload spans two blocks it is a complete-looking 64 KiB prefix whose
    /// CRC even validates — so cars stored in the second block simply vanish from any code that
    /// reads <c>Content</c> alone.</para>
    ///
    /// <para><b>Continuation blocks are decoded with the previous block's output as an LZ4
    /// dictionary</b> — they are not independently decodable, and must NOT be decoded by
    /// concatenating compressed bytes (that silently yields misaligned output). Each block's
    /// framing word is the CRC-32C of that block's decompressed bytes and is verified here.</para>
    ///
    /// <para><b>Writing:</b> use <see cref="SetDecodedPayload"/>, which re-splits the payload across
    /// the container block and continuation blocks and recomputes every block CRC.</para>
    /// </summary>
    public byte[] DecodedPayload
    {
        get
        {
            if (Container.IsModified || Trailer.Length == 0)
                return Container.Content;

            var payload = Container.Content;
            foreach (var (compressed, storedCrc) in ContinuationBlocks())
            {
                byte[] decoded;
                try
                {
                    decoded = Lz4Block.DecodeWithDictionary(compressed, payload, $"chunk '{Tag}' continuation block");
                }
                catch (SgfiFormatException)
                {
                    break;   // trailer was not a continuation block after all
                }
                if (Crc32C.Compute(decoded) != storedCrc)
                    break;   // refuse to surface a block we cannot verify

                var combined = new byte[payload.Length + decoded.Length];
                payload.CopyTo(combined, 0);
                decoded.CopyTo(combined, payload.Length);
                payload = combined;
            }
            return payload;
        }
    }

    /// <summary>
    /// Number of LZ4 blocks making up <see cref="DecodedPayload"/>: 1 for a payload that fits in
    /// <see cref="BlockSize"/>, more when it continues into the trailer.
    /// </summary>
    public int BlockCount => 1 + ContinuationBlocks().Count;

    /// <summary>
    /// Compressed continuation blocks parsed out of <see cref="Trailer"/>, in order. Empty when the
    /// trailer holds no recognisable block framing.
    /// </summary>
    private List<(byte[] Compressed, uint Crc)> ContinuationBlocks()
    {
        var result = new List<(byte[], uint)>();
        int pos = 0;
        // [u32 compressedLength][u32 CRC-32C of this block's decompressed bytes][compressed bytes]
        while (pos + 8 <= Trailer.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(Trailer.AsSpan(pos, 4));
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(Trailer.AsSpan(pos + 4, 4));
            if (length <= 0 || pos + 8 + length > Trailer.Length)
                break;
            result.Add((Trailer[(pos + 8)..(pos + 8 + length)], crc));
            pos += 8 + length;
        }
        return result;
    }

    /// <summary>
    /// Replace the chunk's complete logical payload, re-splitting it across the container block and
    /// any continuation blocks in <see cref="Trailer"/>.
    ///
    /// <para>The payload is cut into <see cref="BlockSize"/> slices: the first becomes the
    /// container's content, each remaining slice is appended to the trailer as
    /// <c>[u32 compressedLength][u32 CRC-32C of the slice][compressed bytes]</c>.</para>
    ///
    /// <para><b>Continuation blocks are written self-contained</b> (compressed without a
    /// dictionary). The game decodes them <em>with</em> the previous output as a dictionary, which is
    /// harmless: a self-contained block never emits back-references reaching into the dictionary, so
    /// it decodes identically either way — verified against a real save. This avoids needing LZ4's
    /// <c>compress_usingDict</c>, which <c>K4os</c> does not expose. The only cost is a slightly
    /// larger block than the game itself would produce.</para>
    ///
    /// <para>Any trailing bytes in the original trailer that were not part of a continuation block
    /// are preserved after the rewritten blocks.</para>
    /// </summary>
    public void SetDecodedPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
            throw new SgfiFormatException("Cannot set an empty chunk payload.");

        int residualAt = ContinuationBlocksEnd();
        byte[] residual = Trailer[residualAt..];

        int first = Math.Min(BlockSize, payload.Length);
        Container.Content = payload[..first].ToArray();

        var rebuilt = new List<byte>();
        var header = new byte[8];
        for (int offset = first; offset < payload.Length; offset += BlockSize)
        {
            int length = Math.Min(BlockSize, payload.Length - offset);
            var slice = payload.Slice(offset, length);
            byte[] compressed = Lz4Block.Encode(slice);

            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), (uint)compressed.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), Crc32C.Compute(slice));
            rebuilt.AddRange(header);
            rebuilt.AddRange(compressed);
        }
        rebuilt.AddRange(residual);
        Trailer = rebuilt.ToArray();
    }

    /// <summary>Offset in <see cref="Trailer"/> just past the last parsable continuation block.</summary>
    private int ContinuationBlocksEnd()
    {
        int pos = 0;
        while (pos + 8 <= Trailer.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(Trailer.AsSpan(pos, 4));
            if (length <= 0 || pos + 8 + length > Trailer.Length) break;
            pos += 8 + length;
        }
        return pos;
    }

    /// <summary>The chunk's bytes: the serialized container followed by <see cref="Trailer"/>.</summary>
    public byte[] Serialize()
    {
        var container = Container.Serialize();
        var buffer = new byte[container.Length + Trailer.Length];
        container.CopyTo(buffer, 0);
        Trailer.CopyTo(buffer, container.Length);
        return buffer;
    }
}

/// <summary>
/// A complete Wreckfest 2 <c>profile.sgfi</c> career save.
///
/// <para><b>Layout.</b> The file is a <see cref="BbagContainer"/> (tag <c>"ifgs"</c>) whose
/// decompressed payload — "the tree" — is a 12-byte root node followed by a chain of chunks:</para>
/// <code>
/// [4CC rootTag "ubas"][u32 rootKind][u32 chunkCount]
/// [u32 len0][chunk 0 ...][u32 CRC-32C of chunk 0]
/// [u32 len1][chunk 1 ...][u32 CRC-32C of chunk 1]
/// ...
/// </code>
/// where every <c>len</c> is <c>chunkLength + 4</c> (the chunk plus its trailing CRC) and every CRC
/// covers the chunk from its first byte up to — not including — the CRC field. A real save has four
/// chunks: <c>forp</c> (profile), <c>srcc</c> (all cars), <c>sspu</c> and <c>sdia</c> (driving aids).
///
/// <para><b>Integrity.</b> Four layers must be recomputed on every write, and
/// <see cref="Serialize"/> does all four unconditionally:</para>
/// <list type="number">
///   <item>each nested container's header CRC over its decompressed content,</item>
///   <item>every chunk CRC in the chain,</item>
///   <item>the outer header's <c>compressedLength</c>,</item>
///   <item>the outer header CRC over the whole decompressed tree.</item>
/// </list>
/// Getting (1) or (2) wrong makes the game load the save but silently strip every car and tune;
/// getting a length wrong is a fatal read error.
/// </summary>
public sealed class SaveFile
{
    /// <summary>Fixed outer header size in bytes.</summary>
    public const int HeaderSize = BbagContainer.HeaderSize;

    /// <summary>The outer 4CC as stored — the reversed FourCC of the <c>.sgfi</c> extension.</summary>
    public const string SaveTag = "ifgs";

    /// <summary>Guard against a corrupt chunk count sending the parser into a huge loop.</summary>
    private const int MaxChunks = 1024;

    private readonly byte[]? _originalTree;
    private readonly byte[]? _originalCompressed;

    private SaveFile(uint rootValue, string tag, uint reserved, uint storedCrc, bool storedCrcValid,
                     string rootNodeTag, uint rootNodeKind, List<SaveChunk> chunks, byte[] rootTrailer,
                     byte[] originalTree, byte[] originalCompressed)
    {
        RootValue = rootValue;
        Tag = tag;
        Reserved = reserved;
        StoredCrc = storedCrc;
        StoredCrcValid = storedCrcValid;
        RootNodeTag = rootNodeTag;
        RootNodeKind = rootNodeKind;
        Chunks = chunks;
        RootTrailer = rootTrailer;
        _originalTree = originalTree;
        _originalCompressed = originalCompressed;
        var carsChunk = chunks.Find(c => c.Tag == CarCollection.ContainerTag);
        // Parsed from the FULL payload: the cars list continues into LZ4 continuation blocks held
        // in the chunk trailer, so the container alone omits any car stored past the first block.
        Cars = new CarCollection(carsChunk);
    }

    /// <summary>uint32 at 0x00 of the outer header. Always 7.</summary>
    public uint RootValue { get; set; }

    /// <summary>The outer 4CC at 0x04, as stored (<c>"ifgs"</c>).</summary>
    public string Tag { get; }

    /// <summary>uint32 at 0x08 of the outer header. Preserved verbatim.</summary>
    public uint Reserved { get; set; }

    /// <summary>The outer header CRC-32C (0x10) as stored when the save was parsed.</summary>
    public uint StoredCrc { get; }

    /// <summary>True when <see cref="StoredCrc"/> matched the decompressed tree at parse time.</summary>
    public bool StoredCrcValid { get; }

    /// <summary>The tree's root node 4CC, <c>"ubas"</c> in every observed save.</summary>
    public string RootNodeTag { get; }

    /// <summary>The root node's kind word (offset 4 of the tree). Observed 0.</summary>
    public uint RootNodeKind { get; }

    /// <summary>The chunk chain, in file order.</summary>
    public IReadOnlyList<SaveChunk> Chunks { get; }

    /// <summary>
    /// Bytes left over after the last chunk. Empty in every observed save; preserved verbatim so an
    /// unknown variant still round-trips.
    /// </summary>
    public byte[] RootTrailer { get; }

    /// <summary>The cars, presets and tuning stored in the <c>srcc</c> chunk.</summary>
    public CarCollection Cars { get; }

    /// <summary>True when every CRC in the file matched at parse time (outer, chunks, containers).</summary>
    public bool AllCrcsValid =>
        StoredCrcValid && Chunks.All(c => c.StoredCrcValid && c.Container.StoredCrcValid);

    /// <summary>Read and parse a save from disk.</summary>
    public static SaveFile Load(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>
    /// Parse a save from its raw bytes. Structural problems throw; CRC mismatches do not — they are
    /// reported through <see cref="StoredCrcValid"/> and friends so a damaged save can still be
    /// inspected.
    /// </summary>
    /// <exception cref="SgfiFormatException">The file is not a well-formed save.</exception>
    public static SaveFile Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new SgfiFormatException($"File too small: {data.Length} bytes, need at least {HeaderSize}.");

        string tag = Encoding.Latin1.GetString(data.Slice(4, 4));
        if (tag != SaveTag)
            throw new SgfiFormatException($"Bad tag at 0x04: expected '{SaveTag}', got '{Printable(tag)}'. Not a profile.sgfi file.");

        uint rootValue = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
        uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
        uint storedCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));

        if (compressedLength != data.Length - HeaderSize)
            throw new SgfiFormatException(
                $"Length field mismatch: header 0x0C says {compressedLength} payload bytes " +
                $"but {data.Length - HeaderSize} follow the header.");

        var compressed = data[HeaderSize..].ToArray();
        byte[] tree = Lz4Block.Decode(compressed, "the save payload");
        bool crcValid = Crc32C.Compute(tree) == storedCrc;

        if (tree.Length < 12)
            throw new SgfiFormatException($"Decompressed tree is only {tree.Length} bytes — no root node.");

        string rootNodeTag = Encoding.Latin1.GetString(tree, 0, 4);
        uint rootNodeKind = BinaryPrimitives.ReadUInt32LittleEndian(tree.AsSpan(4, 4));
        uint chunkCount = BinaryPrimitives.ReadUInt32LittleEndian(tree.AsSpan(8, 4));
        if (chunkCount > MaxChunks)
            throw new SgfiFormatException($"Root node '{Printable(rootNodeTag)}' declares {chunkCount} chunks — refusing to parse.");

        var chunks = new List<SaveChunk>((int)chunkCount);
        int pos = 12;
        for (uint i = 0; i < chunkCount; i++)
        {
            if (pos + 4 > tree.Length)
                throw new SgfiFormatException($"Truncated tree: chunk {i}'s length field runs past the end.");
            uint declared = BinaryPrimitives.ReadUInt32LittleEndian(tree.AsSpan(pos, 4));
            pos += 4;

            long chunkLength = (long)declared - 4; // the field counts the chunk plus its CRC
            if (chunkLength < BbagContainer.HeaderSize || pos + chunkLength + 4 > tree.Length)
                throw new SgfiFormatException(
                    $"Chunk {i} at 0x{pos:X} declares length {declared}, which does not fit the {tree.Length}-byte tree.");

            var chunkBytes = tree.AsSpan(pos, (int)chunkLength);
            var container = BbagContainer.ParseAt(chunkBytes, 0, out int consumed);
            var trailer = chunkBytes[consumed..].ToArray();

            uint chunkCrc = BinaryPrimitives.ReadUInt32LittleEndian(tree.AsSpan(pos + (int)chunkLength, 4));
            chunks.Add(new SaveChunk(container, trailer, chunkCrc, Crc32C.Compute(chunkBytes) == chunkCrc));

            pos += (int)chunkLength + 4;
        }

        var rootTrailer = tree[pos..];
        return new SaveFile(rootValue, tag, reserved, storedCrc, crcValid,
                            rootNodeTag, rootNodeKind, chunks, rootTrailer, tree, compressed);
    }

    /// <summary>
    /// Rebuild the decompressed tree from the current chunks, recomputing every chunk length and
    /// every chunk CRC.
    /// </summary>
    public byte[] BuildTree()
    {
        var bodies = new byte[Chunks.Count][];
        int total = 12 + RootTrailer.Length;
        for (int i = 0; i < Chunks.Count; i++)
        {
            bodies[i] = Chunks[i].Serialize();
            total += 4 + bodies[i].Length + 4;
        }

        var tree = new byte[total];
        var span = tree.AsSpan();
        Encoding.Latin1.GetBytes(RootNodeTag, span[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), RootNodeKind);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), (uint)Chunks.Count);

        int pos = 12;
        foreach (var body in bodies)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), (uint)(body.Length + 4));
            pos += 4;
            body.CopyTo(span[pos..]);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos + body.Length, 4), Crc32C.Compute(body));
            pos += body.Length + 4;
        }
        RootTrailer.CopyTo(span[pos..]);
        return tree;
    }

    /// <summary>
    /// Serialize the whole save, recomputing all four integrity layers (container CRCs, chunk CRCs,
    /// outer compressed length, outer tree CRC).
    ///
    /// <para>An unmodified save re-serializes byte-identically: containers whose content is unchanged
    /// re-emit their original compressed bytes.</para>
    ///
    /// <para><b>Variable-size writes are verified.</b> When an edit changes a container's content the
    /// payload is re-compressed, and the new compressed size usually differs from the original (our
    /// LZ4 compresses better than the game's, so edits typically shrink the file). The enclosing
    /// chunk's length field shifts by the same delta. Confirmed in-game 2026-07-22: an edit that
    /// shrank a save 8668 → 8636 bytes loaded correctly with the edited value applied and all cars
    /// and presets intact.</para>
    /// </summary>
    public byte[] Serialize()
    {
        byte[] tree = BuildTree();
        byte[] payload = _originalCompressed is not null && _originalTree is not null
                         && tree.AsSpan().SequenceEqual(_originalTree)
            ? _originalCompressed
            : Lz4Block.Encode(tree);

        var buffer = new byte[HeaderSize + payload.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], RootValue);
        Encoding.Latin1.GetBytes(Tag, span[4..8]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8, 4), Reserved);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), Crc32C.Compute(tree));
        payload.CopyTo(span[HeaderSize..]);
        return buffer;
    }

    /// <summary>Serialize and write to <paramref name="path"/>. Never point this at the live save.</summary>
    public void Save(string path) => File.WriteAllBytes(path, Serialize());

    /// <summary>
    /// Compare the <em>decoded</em> content of two saves — header fields, chunk order and every
    /// container's decompressed bytes — ignoring compressed-byte differences. Two saves holding the
    /// same data can differ byte-for-byte simply because our LZ4 encoder is not the game's.
    /// </summary>
    public bool ContentEquals(SaveFile other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (RootValue != other.RootValue || Tag != other.Tag || Reserved != other.Reserved) return false;
        if (RootNodeTag != other.RootNodeTag || RootNodeKind != other.RootNodeKind) return false;
        if (!RootTrailer.AsSpan().SequenceEqual(other.RootTrailer)) return false;
        if (Chunks.Count != other.Chunks.Count) return false;

        for (int i = 0; i < Chunks.Count; i++)
        {
            SaveChunk a = Chunks[i], b = other.Chunks[i];
            if (a.Tag != b.Tag) return false;
            if (a.Container.RootValue != b.Container.RootValue) return false;
            if (a.Container.Reserved != b.Container.Reserved) return false;
            // Compare the DECODED payload, not the compressed bytes. Two saves are content-equal
            // when their logical payloads match; the compressed form legitimately differs (our LZ4
            // is not the game's, and we write continuation blocks self-contained where the game
            // uses dictionary compression). Comparing raw bytes here would fail on correct output.
            if (!a.DecodedPayload.AsSpan().SequenceEqual(b.DecodedPayload)) return false;
        }
        return true;
    }

    private static string Printable(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c is >= ' ' and < (char)0x7f ? c : '.');
        return sb.ToString();
    }
}
