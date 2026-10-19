using System.Buffers.Binary;

namespace Marquee.Core.Themes.Validation;

/// Counts a zip's entries from its central directory without reading them in. ZipArchive builds an object for every
/// entry the moment its entries are asked for, so a small zip listing millions of tiny entries would cost hundreds of
/// megabytes before the entry limit could refuse it. This reads the count the zip declares, then walks the directory
/// the way ZipArchive does (record after record from the directory's start, for as long as they are directory records),
/// stopping one record past the limit.
static class ZipDirectory
{
    const uint EndSignature = 0x06054b50;
    const uint Zip64LocatorSignature = 0x07064b50;
    const uint Zip64EndSignature = 0x06064b50;
    const uint EntrySignature = 0x02014b50;
    const int EndLength = 22;
    const int Zip64LocatorLength = 20;
    const int Zip64EndLength = 56;
    const int EntryLength = 46;

    /// The number of entries the zip says it has, and the number its directory holds, counted no further than one past
    /// `max`. Null when there is no end-of-directory record; opening the zip then reports the damage.
    public static (long Declared, long Found)? Count(Stream zip, int max)
    {
        if (FindEnd(zip) is not { } end) return null;

        var record = Read(zip, end, EndLength);
        long declared = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(10));
        long start = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16));

        // A zip too large for the classic record marks its fields with all ones and keeps the real values in a Zip64
        // record, which a locator just before the classic record points to.
        if ((declared == ushort.MaxValue || start == uint.MaxValue) && end >= Zip64LocatorLength)
        {
            var locator = Read(zip, end - Zip64LocatorLength, Zip64LocatorLength);
            var at = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) == Zip64LocatorSignature && zip.Length >= Zip64EndLength &&
                at <= (ulong)(zip.Length - Zip64EndLength))
            {
                var zip64 = Read(zip, (long)at, Zip64EndLength);
                if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) == Zip64EndSignature)
                {
                    declared = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32)), long.MaxValue);
                    start = (long)Math.Min(BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(48)), long.MaxValue);
                }
            }
        }

        return (declared, Walk(zip, start, max));
    }

    /// Where the end-of-directory record starts: the last one in the file, which is followed only by its comment.
    static long? FindEnd(Stream zip)
    {
        if (zip.Length < EndLength) return null;
        var tailLength = (int)Math.Min(zip.Length, EndLength + ushort.MaxValue);
        var tail = Read(zip, zip.Length - tailLength, tailLength);
        for (var i = tail.Length - EndLength; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EndSignature) return zip.Length - tailLength + i;
        return null;
    }

    /// Directory records from `start` on, up to one past `max`.
    static long Walk(Stream zip, long start, int max)
    {
        if (start < 0 || start > zip.Length) return 0;
        zip.Position = start;
        var header = new byte[EntryLength];
        var found = 0L;
        while (found <= max && zip.ReadAtLeast(header, EntryLength, throwOnEndOfStream: false) == EntryLength &&
               BinaryPrimitives.ReadUInt32LittleEndian(header) == EntrySignature)
        {
            found++;
            // The name, extra field and comment follow the fixed part.
            var rest = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28)) +
                       BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30)) +
                       BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
            zip.Seek(rest, SeekOrigin.Current);
        }

        return found;
    }

    static byte[] Read(Stream zip, long at, int length)
    {
        zip.Position = at;
        var bytes = new byte[length];
        zip.ReadExactly(bytes);
        return bytes;
    }
}
