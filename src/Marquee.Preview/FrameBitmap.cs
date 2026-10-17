using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace Marquee.Preview;

public static class FrameBitmap
{
    /// Copies a frame into a GDI+ bitmap. Skia's BGRA premultiplied layout is GDI+'s 32bppPArgb, so it's one copy.
    public static Bitmap ToBitmap(SKImage image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            if (!image.ReadPixels(info, data.Scan0, data.Stride, 0, 0))
                throw new InvalidOperationException("Could not read the frame's pixels.");
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
