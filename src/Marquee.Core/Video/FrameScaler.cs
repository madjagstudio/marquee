using Marquee.Core.Rendering;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Video;

/// Where a video layer's frames go on the canvas, or a video widget's in its box, and how a decoded frame is scaled to
/// fit there. A frame is scaled once, as it is decoded, to exactly the pixels it covers: frames waiting to be shown are
/// never larger than the canvas or the box, however large the clip, and drawing one is a plain copy (unless the widget
/// is rotated). Thread-safe: it holds no state but its sizes.
public sealed class FrameScaler
{
    /// Mipmaps keep a 4K clip shrunk to the canvas from shimmering, which plain linear sampling would do past 2:1.
    static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    readonly SKRect _source;

    /// `fit` places a clip of `clip` pixels in `area` as it would an image.
    public FrameScaler(ImageFit fit, SKSizeI clip, SKRect area)
    {
        (Fit, Clip, Area) = (fit, clip, area);

        var (source, destination) = ImageFitting.Fit(fit, clip.Width, clip.Height, area);

        // A clip at natural size (fit: none) can overflow the area. Only the part inside it is ever seen, so only that
        // part is kept: the same fraction of the source as the visible part is of the destination.
        var visible = SKRect.Intersect(destination, area);
        var (scaleX, scaleY) = (source.Width / destination.Width, source.Height / destination.Height);
        _source = new SKRect(
            source.Left + (visible.Left - destination.Left) * scaleX,
            source.Top + (visible.Top - destination.Top) * scaleY,
            source.Left + (visible.Right - destination.Left) * scaleX,
            source.Top + (visible.Bottom - destination.Top) * scaleY);

        // Whole pixels, so a frame lands on the canvas pixel for pixel. A sliver of a clip still gets one.
        var rounded = SKRectI.Round(visible);
        Destination = new SKRectI(rounded.Left, rounded.Top, Math.Max(rounded.Right, rounded.Left + 1),
            Math.Max(rounded.Bottom, rounded.Top + 1));
    }

    /// How the clip is placed, its size, and the box it is placed in.
    public ImageFit Fit { get; }

    public SKSizeI Clip { get; }

    public SKRect Area { get; }

    /// The canvas pixels a frame covers.
    public SKRectI Destination { get; }

    /// Draws the frame's visible part into `target`, a bitmap of the Destination's size, replacing what it held (see
    /// FrameBuffers). The pixels are only read during the call, so they may be the decoder's, valid until its next frame.
    public void Scale(SKPixmap pixels, SKBitmap target)
    {
        using var frame = SKImage.FromPixels(pixels) // wraps the pixels without copying them
            ?? throw new InvalidOperationException("Could not read a decoded video frame.");
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent); // a GIF frame can be transparent; a reused buffer holds an older frame
        canvas.DrawImage(frame, _source, SKRect.Create(target.Width, target.Height), Sampling);
    }
}
