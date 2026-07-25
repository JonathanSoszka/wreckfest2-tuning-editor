namespace Wf2Core;

/// <summary>
/// CRC-32C (Castagnoli) — the checksum stored at header offset 0x10 of every bbag container
/// (.sgfi saves, .upgr parts, .ctms tuning definitions). Computed over the DECOMPRESSED payload.
/// Reflected form: poly 0x82F63B78, init/xorout 0xFFFFFFFF.
/// </summary>
public static class Crc32C
{
    private const uint Poly = 0x82F63B78;
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? (c >> 1) ^ Poly : c >> 1;
            t[i] = c;
        }
        return t;
    }

    /// <summary>Compute CRC-32C over <paramref name="data"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFF;
    }
}
