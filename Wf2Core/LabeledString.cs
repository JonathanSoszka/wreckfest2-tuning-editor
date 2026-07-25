using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Wf2Core;

/// <summary>
/// Reads a display string that the game stores next to its localization key in a node tree —
/// <c>VEHICLE_NAME_&lt;hash&gt;_&lt;len&gt;</c>, <c>VEHICLE_UPGRADE_NAME_…</c>, etc., followed by a
/// u32-length-prefixed literal. Shared by <see cref="GuideExporter"/> and <see cref="EquippedParts"/>.
///
/// <para>Works on the file's raw bytes: even in a compressed <c>.upgr</c>/<c>.cavs</c>, these strings
/// appear as LZ4 literals, so no decompression is needed. The key's trailing <c>_&lt;len&gt;</c> is a
/// cross-check on the declared length.</para>
/// </summary>
internal static class LabeledString
{
    public static string? Read(byte[] bytes, Regex keyPattern)
    {
        // Latin-1 view so byte offsets line up 1:1 with the ASCII key match.
        var text = Encoding.Latin1.GetString(bytes);
        var m = keyPattern.Match(text);
        if (!m.Success) return null;

        var expectedLen = int.Parse(m.Groups[1].Value);
        var afterKey = m.Index + m.Length; // byte offset just past the key string

        // Expect a u32 length prefix immediately after the key, then the literal.
        if (afterKey + 4 <= bytes.Length)
        {
            var declared = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(afterKey, 4));
            if (declared == expectedLen && afterKey + 4 + declared <= bytes.Length)
                return Encoding.UTF8.GetString(bytes, afterKey + 4, declared).Trim();
        }

        // Fallback: scan forward for the next run of printable chars of the expected length.
        for (var i = afterKey; i < bytes.Length - expectedLen; i++)
        {
            var slice = bytes.AsSpan(i, expectedLen);
            if (IsLikelyText(slice) && (i == 0 || bytes[i - 1] < 0x20))
                return Encoding.UTF8.GetString(slice).Trim();
        }
        return null;
    }

    private static bool IsLikelyText(ReadOnlySpan<byte> s)
    {
        foreach (var b in s)
            if (b < 0x20 || b > 0x7e) return false;
        return true;
    }
}
