using System.Text;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// What a GIF player does with a frame's rectangle before drawing the next frame (the graphic control extension's
/// disposal method, GIF89a section 23).
enum GifDisposal { Unspecified = 0, DoNotDispose = 1, RestoreBackground = 2, RestorePrevious = 3 }

/// One frame. Pixels are palette indices, row by row, filling Rect, which may be any part of the canvas. Delay is in
/// hundredths of a second, as GIF stores it.
sealed record GifFrame(SKRectI Rect, byte[] Pixels, int Delay, GifDisposal Disposal = GifDisposal.DoNotDispose)
{
    /// A frame of one palette color over Rect.
    public static GifFrame Solid(SKRectI rect, byte index, int delay, GifDisposal disposal = GifDisposal.DoNotDispose) =>
        new(rect, Enumerable.Repeat(index, rect.Width * rect.Height).ToArray(), delay, disposal);
}

/// Writes small animated GIF89a files for decoder tests: Skia can read GIFs but not write them. The output is the
/// plainest valid file: a 256-entry global color table (the palette, padded with black), a NETSCAPE2.0 extension that
/// loops forever, and per frame a graphic control extension (delay, disposal, no transparency) and an image descriptor
/// (its rectangle, no local color table, not interlaced) followed by LZW data. Section numbers refer to the GIF89a
/// specification.
static class GifWriter
{
    /// Every pixel is a full byte, so codes start at 9 bits: 0–255 are the pixels themselves, 256 clears, 257 ends.
    const int MinCodeSize = 8;
    const int ClearCode = 1 << MinCodeSize;
    const int EndCode = ClearCode + 1;
    const int CodeWidth = MinCodeSize + 1;

    /// A decoder adds a table entry for each code after the first one following a clear, starting at 258, and widens
    /// codes to 10 bits once the next entry would be 512: after 255 codes. A clear every 254 pixels keeps every code at
    /// 9 bits and lets each code simply be its pixel's index. That is valid LZW, only without compression.
    const int PixelsPerClear = (1 << MinCodeSize) - 2;

    public static byte[] Write(int width, int height, IReadOnlyList<SKColor> palette, params GifFrame[] frames)
    {
        if (palette.Count is 0 or > 256)
            throw new ArgumentException("A GIF palette has 1 to 256 colors.", nameof(palette));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream); // little-endian, as GIF is

        writer.Write(Encoding.ASCII.GetBytes("GIF89a"));

        // Logical screen descriptor (18): global color table present, 8 bits of color resolution, 2^(7+1) = 256 entries.
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write((byte)0b1_111_0_111);
        writer.Write((byte)0); // background color index
        writer.Write((byte)0); // no pixel aspect ratio

        for (var i = 0; i < 256; i++)
        {
            var color = i < palette.Count ? palette[i] : SKColors.Black;
            writer.Write(color.Red);
            writer.Write(color.Green);
            writer.Write(color.Blue);
        }

        // Application extension (26) with Netscape's looping block: loop count 0, forever.
        writer.Write([0x21, 0xFF, 11]);
        writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
        writer.Write([3, 1, 0, 0, 0]);

        foreach (var frame in frames)
        {
            if (frame.Pixels.Length != frame.Rect.Width * frame.Rect.Height)
                throw new ArgumentException("A frame needs one pixel for each point of its rectangle.", nameof(frames));

            // Graphic control extension (23): disposal in bits 2–4, no user input, no transparent color.
            writer.Write([0x21, 0xF9, 4, (byte)((int)frame.Disposal << 2)]);
            writer.Write((ushort)frame.Delay);
            writer.Write([0, 0]); // transparent color index (unused), block terminator

            // Image descriptor (20).
            writer.Write((byte)0x2C);
            writer.Write((ushort)frame.Rect.Left);
            writer.Write((ushort)frame.Rect.Top);
            writer.Write((ushort)frame.Rect.Width);
            writer.Write((ushort)frame.Rect.Height);
            writer.Write((byte)0); // no local color table, not interlaced

            // Table-based image data (22): the minimum code size, then the codes in sub-blocks of at most 255 bytes.
            writer.Write((byte)MinCodeSize);
            var data = Lzw(frame.Pixels);
            for (var offset = 0; offset < data.Length; offset += 255)
            {
                var length = Math.Min(255, data.Length - offset);
                writer.Write((byte)length);
                writer.Write(data, offset, length);
            }
            writer.Write((byte)0); // block terminator
        }

        writer.Write((byte)0x3B); // trailer
        writer.Flush();
        return stream.ToArray();
    }

    static byte[] Lzw(byte[] pixels)
    {
        var codes = new BitPacker();
        for (var i = 0; i < pixels.Length; i++)
        {
            if (i % PixelsPerClear == 0)
                codes.Add(ClearCode, CodeWidth);
            codes.Add(pixels[i], CodeWidth);
        }

        codes.Add(EndCode, CodeWidth);
        return codes.ToArray();
    }

    /// Packs codes into bytes least significant bit first, as GIF's LZW data is laid out (Appendix F).
    sealed class BitPacker
    {
        readonly List<byte> _bytes = [];
        int _buffer;
        int _bits;

        public void Add(int code, int width)
        {
            _buffer |= code << _bits;
            _bits += width;
            while (_bits >= 8)
            {
                _bytes.Add((byte)_buffer);
                _buffer >>= 8;
                _bits -= 8;
            }
        }

        public byte[] ToArray() => _bits > 0 ? [.. _bytes, (byte)_buffer] : [.. _bytes];
    }
}
