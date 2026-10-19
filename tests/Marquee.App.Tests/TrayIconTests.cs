using System.Buffers.Binary;
using System.Drawing;
using Marquee.App.Tests.TestSupport;

namespace Marquee.App.Tests;

public class TrayIconTests
{
    static readonly string IconFile = SourceTree.Path("src", "Marquee.App", "marquee.ico");

    /// Each entry's size and image bytes, from the ICO directory.
    static IEnumerable<(int Size, byte[] Image)> Entries()
    {
        var bytes = File.ReadAllBytes(IconFile);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2))); // an icon, not a cursor
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(6 + 16 * i);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            yield return (entry[0] is 0 ? 256 : entry[0], bytes.AsSpan(offset, length).ToArray());
        }
    }

    [Fact]
    public void The_icon_file_holds_every_size_windows_asks_for_each_a_png_of_that_size()
    {
        var entries = Entries().ToList();

        Assert.Equal([16, 20, 24, 32, 48, 64, 128, 256], entries.Select(entry => entry.Size));
        foreach (var (size, image) in entries)
        {
            using var png = Image.FromStream(new MemoryStream(image));
            Assert.Equal(new Size(size, size), png.Size);
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    public void Windows_reads_the_sizes_the_notification_area_uses(int size)
    {
        using var icon = new Icon(IconFile, size, size);
        using var bitmap = icon.ToBitmap();

        Assert.Equal(new Size(size, size), bitmap.Size);
        // The bar is opaque left of the middle, below the line; the corners are clear.
        Assert.Equal(255, bitmap.GetPixel(size / 4, size * 5 / 8).A);
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
    }

    [Fact]
    public void The_tray_loads_its_embedded_icon_at_the_size_asked_for()
    {
        using var icon = TrayIcons.Load(new Size(20, 20));

        Assert.Equal(new Size(20, 20), icon.Size);
    }
}
