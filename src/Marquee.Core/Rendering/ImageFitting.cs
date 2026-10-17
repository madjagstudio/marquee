using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

static class ImageFitting
{
    /// Which part of the image to draw, and where, for each fit mode. With None the image may overflow the box,
    /// so callers clip.
    public static (SKRect Source, SKRect Destination) Fit(ImageFit fit, int width, int height, SKRect box)
    {
        var full = SKRect.Create(width, height);
        switch (fit)
        {
            case ImageFit.Stretch:
                return (full, box);
            case ImageFit.Contain:
            {
                var scale = Math.Min(box.Width / width, box.Height / height);
                return (full, Centered(box, width * scale, height * scale));
            }
            case ImageFit.Cover:
            {
                var scale = Math.Max(box.Width / width, box.Height / height);
                var (w, h) = (box.Width / scale, box.Height / scale);
                return (SKRect.Create((width - w) / 2, (height - h) / 2, w, h), box);
            }
            default:
                return (full, Centered(box, width, height));
        }
    }

    static SKRect Centered(SKRect box, float width, float height) =>
        SKRect.Create(box.MidX - width / 2, box.MidY - height / 2, width, height);
}
