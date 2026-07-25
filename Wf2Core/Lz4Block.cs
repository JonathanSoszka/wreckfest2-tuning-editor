using K4os.Compression.LZ4;

namespace Wf2Core;

/// <summary>
/// Raw-LZ4-block helpers shared by every Bugbear "bbag" container (.sgfi saves, .upgr parts,
/// .ctms tuning definitions, and the containers nested inside a save).
///
/// The containers store a <b>bare LZ4 block</b>: no frame, no magic and — critically — no stored
/// decompressed size. Decoding therefore has to guess an output capacity and grow it until the
/// block fits.
/// </summary>
internal static class Lz4Block
{
    /// <summary>Upper bound on a decoded block; a bigger result means the input is not really LZ4.</summary>
    private const int MaxDecodedSize = 64 * 1024 * 1024;

    /// <summary>
    /// Decode a raw LZ4 block into a right-sized array.
    /// </summary>
    /// <param name="block">The compressed bytes (exactly the block, nothing else).</param>
    /// <param name="what">Human-readable description used in the error message.</param>
    /// <exception cref="SgfiFormatException">The block is not valid LZ4.</exception>
    internal static byte[] Decode(ReadOnlySpan<byte> block, string what)
    {
        long capacity = Math.Max(64L * 1024, (long)block.Length * 8);
        while (capacity <= MaxDecodedSize)
        {
            var target = new byte[capacity];
            int decoded;
            try
            {
                decoded = LZ4Codec.Decode(block, target);
            }
            catch (Exception)
            {
                decoded = -1; // target too small (or malformed) — grow and retry
            }

            if (decoded >= 0)
                return target[..decoded];
            capacity *= 2;
        }
        throw new SgfiFormatException($"LZ4 decompression of {what} failed — not a valid raw LZ4 block.");
    }

    /// <summary>
    /// Decode a raw LZ4 block whose back-references may reach into <paramref name="dictionary"/> —
    /// the decompressed output of the preceding block (LZ4 "linked block" mode).
    ///
    /// <para>Implemented directly rather than via <c>K4os</c> because that package does not expose
    /// LZ4's <c>usingDict</c> entry point. The block format is small and fully specified: a token
    /// byte splits into literal length (high nibble) and match length (low nibble), each extended by
    /// 255-chained bytes, with a 16-bit little-endian match offset.</para>
    ///
    /// <para><b>Do not substitute concatenation.</b> Appending this block's bytes to the previous
    /// block's compressed bytes and decoding as one stream <em>appears</em> to work but silently
    /// produces misaligned output — LZ4 blocks are self-terminating (the final sequence must be
    /// literals), so the previous stream cannot simply be continued.</para>
    /// </summary>
    /// <param name="block">The compressed continuation block.</param>
    /// <param name="dictionary">Decompressed output of the preceding block(s).</param>
    /// <param name="what">Human-readable description used in the error message.</param>
    /// <exception cref="SgfiFormatException">The block is malformed.</exception>
    internal static byte[] DecodeWithDictionary(ReadOnlySpan<byte> block, ReadOnlySpan<byte> dictionary, string what)
    {
        // Lay the dictionary immediately before the output so match offsets can reach back into it,
        // exactly as LZ4_decompress_safe_usingDict does.
        int cap = Math.Max(64 * 1024, block.Length * 8);
        while (cap <= MaxDecodedSize)
        {
            var buffer = new byte[dictionary.Length + cap];
            dictionary.CopyTo(buffer);
            int outPos = dictionary.Length;
            int i = 0;
            bool overflow = false;

            while (i < block.Length)
            {
                int token = block[i++];

                int literals = token >> 4;
                if (literals == 15)
                {
                    int add;
                    do
                    {
                        if (i >= block.Length) throw new SgfiFormatException($"Truncated literal length in {what}.");
                        add = block[i++];
                        literals += add;
                    } while (add == 255);
                }

                if (i + literals > block.Length) throw new SgfiFormatException($"Truncated literals in {what}.");
                if (outPos + literals > buffer.Length) { overflow = true; break; }
                block.Slice(i, literals).CopyTo(buffer.AsSpan(outPos));
                i += literals;
                outPos += literals;

                if (i >= block.Length) break;   // final sequence is literals-only

                if (i + 2 > block.Length) throw new SgfiFormatException($"Truncated match offset in {what}.");
                int offset = block[i] | (block[i + 1] << 8);
                i += 2;
                if (offset == 0 || offset > outPos) throw new SgfiFormatException($"Invalid match offset in {what}.");

                int matchLen = token & 15;
                if (matchLen == 15)
                {
                    int add;
                    do
                    {
                        if (i >= block.Length) throw new SgfiFormatException($"Truncated match length in {what}.");
                        add = block[i++];
                        matchLen += add;
                    } while (add == 255);
                }
                matchLen += 4;

                if (outPos + matchLen > buffer.Length) { overflow = true; break; }
                int from = outPos - offset;
                for (int k = 0; k < matchLen; k++)   // byte-wise: matches may overlap the output
                    buffer[outPos + k] = buffer[from + k];
                outPos += matchLen;
            }

            if (!overflow)
                return buffer[dictionary.Length..outPos];
            cap *= 2;
        }
        throw new SgfiFormatException($"LZ4 dictionary decompression of {what} exceeded the size limit.");
    }

    /// <summary>
    /// Encode <paramref name="data"/> as a raw LZ4 block at maximum compression. The game accepts
    /// any valid block that decodes to the same bytes, so this need not reproduce its exact output.
    /// </summary>
    /// <exception cref="SgfiFormatException">Compression failed (empty input, or LZ4 refused).</exception>
    internal static byte[] Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new SgfiFormatException("Cannot LZ4-compress an empty payload.");

        var target = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
        int written = LZ4Codec.Encode(data, target, LZ4Level.L12_MAX);
        if (written <= 0)
            throw new SgfiFormatException("LZ4 compression failed.");
        return target[..written];
    }
}
