using SkiaSharp;

namespace Marquee.Core.Devices.UniversalScreen88;

/// Turns a canvas (480×1920 portrait or 1920×480 landscape, per the mount orientation) into what
/// the panel firmware accepts: a 480×1920 image, encoded as JPEG no larger than 512,000 bytes.
public static class FrameEncoder
{
    public const int PanelWidth = 480;
    public const int PanelHeight = 1920;
    public const int MaxJpegBytes = 512_000;

    const int MinQuality = 30;

    public static SKImage ToPanel(SKImage canvasImage, Orientation orientation)
    {
        using var surface = CreatePanelSurface();
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        switch (orientation)
        {
            case Orientation.PortraitFlipped:
                canvas.Translate(PanelWidth, PanelHeight);
                canvas.RotateDegrees(180);
                break;
            case Orientation.Landscape:
                canvas.Translate(PanelWidth, 0);
                canvas.RotateDegrees(90);
                break;
            case Orientation.LandscapeFlipped:
                canvas.Translate(0, PanelHeight);
                canvas.RotateDegrees(-90);
                break;
        }

        var size = orientation.CanvasSize();
        canvas.DrawImage(canvasImage, SKRect.Create(size.Width, size.Height), new SKSamplingOptions(SKFilterMode.Linear));
        return surface.Snapshot();
    }

    /// The JPEG the panel is sent for a canvas image: ToPanel, then EncodeJpeg. A portrait canvas is already the panel's
    /// shape, so it is encoded as it is instead of being copied first, which gives the same picture: the copy only drew it
    /// onto black, and EncodeJpeg drops alpha the same way.
    public static byte[] EncodePanel(SKImage canvasImage, Orientation orientation) =>
        EncodePanel(canvasImage, orientation, wic: null);

    internal static byte[] EncodePanel(SKImage canvasImage, Orientation orientation, WicJpeg? wic)
    {
        if (orientation == Orientation.Portrait && canvasImage.Width == PanelWidth && canvasImage.Height == PanelHeight)
            return EncodeJpeg(canvasImage, wic);

        using var panel = ToPanel(canvasImage, orientation);
        return EncodeJpeg(panel, wic);
    }

    public static SKImage Blank(SKColor color)
    {
        using var surface = CreatePanelSurface();
        surface.Canvas.Clear(color);
        return surface.Snapshot();
    }

    public static byte[] EncodeJpeg(SKImage panel, int maxBytes = MaxJpegBytes, int quality = 90) =>
        EncodeJpeg(panel, wic: null, maxBytes, quality);

    /// With `wic`, Windows' encoder makes the JPEG (much faster), and Skia's when it can't.
    internal static byte[] EncodeJpeg(SKImage panel, WicJpeg? wic, int maxBytes = MaxJpegBytes, int quality = 90)
    {
        for (var q = quality; q >= MinQuality; q -= 10)
        {
            var jpeg = wic?.Encode(panel, q) ?? SkiaJpeg(panel, q);
            if (jpeg.Length <= maxBytes)
                return jpeg;
        }

        throw new InvalidOperationException($"Frame does not fit in {maxBytes} bytes even at JPEG quality {MinQuality}.");
    }

    static byte[] SkiaJpeg(SKImage panel, int quality)
    {
        using var data = panel.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    public static byte[] EncodePng(SKImage panel)
    {
        using var data = panel.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static SKSurface CreatePanelSurface() =>
        SKSurface.Create(new SKImageInfo(PanelWidth, PanelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
}
