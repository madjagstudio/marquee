namespace Marquee.MakeIcon;

/// Packs PNG images into one .ico file. Windows Vista and later read PNG-compressed entries at every size, in Explorer,
/// the notification area (Shell_NotifyIcon), the compiler's ApplicationIcon and System.Drawing.Icon alike, so no entry
/// needs the older BMP form, which only Windows XP required.
static class IcoFile
{
    /// `images` are (size in pixels, PNG bytes), written in the order given.
    public static byte[] Pack(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // ICONDIR: reserved, type 1 (icon), image count.
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        // One 16-byte ICONDIRENTRY per image, then the images themselves.
        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            if (size is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(images), size, "An icon is 1 to 256 pixels.");
            writer.Write((byte)(size == 256 ? 0 : size)); // 0 means 256
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // color planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images) writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }
}
