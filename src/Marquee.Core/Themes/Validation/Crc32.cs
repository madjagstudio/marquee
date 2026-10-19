namespace Marquee.Core.Themes.Validation;

/// CRC-32 as zip files use it (IEEE 802.3, bits reflected), to check what an entry extracts to against the checksum the
/// zip records for it: .NET 8's ZipArchive doesn't check it, and System.IO.Hashing would be a package for 15 lines.
static class Crc32
{
    static readonly uint[] Table = BuildTable();

    /// The checksum of the bytes so far followed by `data`; start from 0.
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        crc = ~crc;
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var i = 0u; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[i] = value;
        }

        return table;
    }
}
