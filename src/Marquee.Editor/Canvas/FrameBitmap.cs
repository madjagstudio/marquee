using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marquee.Editor.Resources;
using SkiaSharp;

namespace Marquee.Editor.Canvas;

/// Frames onto the canvas. Skia's BGRA premultiplied layout is WPF's Pbgra32, so a frame goes into the bitmap in one
/// copy, as the preview window copies into GDI+ (Marquee.Preview's FrameBitmap).
public static class FrameBitmap
{
    /// Copies `image` into `bitmap`, or into a new bitmap when there is none or its size differs, and returns the
    /// bitmap that holds it. Reusing the bitmap keeps the Image showing it, and WPF redraws only what was copied.
    public static WriteableBitmap Copy(SKImage image, WriteableBitmap? bitmap)
    {
        if (bitmap is null || bitmap.PixelWidth != image.Width || bitmap.PixelHeight != image.Height)
            bitmap = new WriteableBitmap(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null);

        bitmap.Lock();
        try
        {
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            if (!image.ReadPixels(info, bitmap.BackBuffer, bitmap.BackBufferStride, 0, 0))
                throw new InvalidOperationException(Strings.CouldNotReadFramePixels);
            bitmap.AddDirtyRect(new Int32Rect(0, 0, image.Width, image.Height));
        }
        finally
        {
            bitmap.Unlock();
        }

        return bitmap;
    }
}
