using System.IO.Compression;
using System.Text;

namespace Marquee.Core.Tests.TestSupport;

/// Writes zips for the zip validation tests, so no binary is committed to the repo.
static class Zips
{
    /// One file entry per (name, contents); text is UTF-8, a byte[] is written as is, and null makes a folder entry.
    public static void Write(string path, params (string Name, object? Contents)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, contents) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (contents is null) continue;
            using var stream = entry.Open();
            stream.Write(contents as byte[] ?? Encoding.UTF8.GetBytes((string)contents));
        }
    }

    /// Bytes that don't compress, so an entry of them is never mistaken for a zip bomb.
    public static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    /// Rewrites the zip's first entry to claim `length` bytes uncompressed, in both its local header and the central
    /// directory, as a zip made to get past a size check would. The data and its checksum stay as they were. The first
    /// entry's records are the first of each kind in the file, so a search from the start finds them.
    public static void Understate(string path, uint length)
    {
        var bytes = File.ReadAllBytes(path);
        BitConverter.TryWriteBytes(bytes.AsSpan(Find(bytes, 0x04034b50) + 22), length); // local file header
        BitConverter.TryWriteBytes(bytes.AsSpan(Find(bytes, 0x02014b50) + 24), length); // central directory header
        File.WriteAllBytes(path, bytes);
    }

    /// Rewrites the zip's end-of-directory record to say it has `count` entries, whatever its directory holds, as a zip
    /// made to get past an entry-count check would. A zip without a comment ends with that 22-byte record.
    public static void UnderstateCount(string path, ushort count)
    {
        var bytes = File.ReadAllBytes(path);
        var end = bytes.Length - 22;
        if (BitConverter.ToUInt32(bytes, end) != 0x06054b50) throw new InvalidOperationException("The zip has a comment.");
        BitConverter.TryWriteBytes(bytes.AsSpan(end + 8), count); // entries on this disk
        BitConverter.TryWriteBytes(bytes.AsSpan(end + 10), count); // entries in all
        File.WriteAllBytes(path, bytes);
    }

    /// Bytes from only 16 values: they deflate to about half, far from a zip bomb's ratio.
    public static byte[] HalfNoise(int length) => [.. Noise(length).Select(b => (byte)(b & 0x0F))];

    static int Find(byte[] bytes, uint signature)
    {
        for (var i = 0; i + 4 <= bytes.Length; i++)
            if (BitConverter.ToUInt32(bytes, i) == signature) return i;
        throw new InvalidOperationException($"No record with signature {signature:X8}.");
    }
}
