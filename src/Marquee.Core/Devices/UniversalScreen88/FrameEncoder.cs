using SkiaSharp;

namespace Marquee.Core.Devices.UniversalScreen88;

/// Which way the landscape canvas is turned to fit the portrait panel buffer.
public enum Rotation { Clockwise, CounterClockwise }

/// Turns a 1920×480 landscape canvas into what the panel firmware accepts:
/// a 480×1920 portrait image, encoded as JPEG no larger than 512,000 bytes.
public static class FrameEncoder
{
    public const int PanelWidth = 480;
    public const int PanelHeight = 1920;
    public const int MaxJpegBytes = 512_000;

    const int CanvasWidth = PanelHeight;
    const int CanvasHeight = PanelWidth;
    const int MinQuality = 30;

    public static SKImage ToPanel(SKImage landscape, Rotation rotation)
    {
        using var surface = CreatePanelSurface();
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        if (rotation == Rotation.Clockwise)
        {
            canvas.Translate(PanelWidth, 0);
            canvas.RotateDegrees(90);
        }
        else
        {
            canvas.Translate(0, PanelHeight);
            canvas.RotateDegrees(-90);
        }

        canvas.DrawImage(landscape, SKRect.Create(CanvasWidth, CanvasHeight), new SKSamplingOptions(SKFilterMode.Linear));
        return surface.Snapshot();
    }

    public static SKImage Blank(SKColor color)
    {
        using var surface = CreatePanelSurface();
        surface.Canvas.Clear(color);
        return surface.Snapshot();
    }

    public static byte[] EncodeJpeg(SKImage panel, int maxBytes = MaxJpegBytes, int quality = 90)
    {
        for (var q = quality; q >= MinQuality; q -= 10)
        {
            using var data = panel.Encode(SKEncodedImageFormat.Jpeg, q);
            if (data.Size <= maxBytes)
                return data.ToArray();
        }

        throw new InvalidOperationException($"Frame does not fit in {maxBytes} bytes even at JPEG quality {MinQuality}.");
    }

    public static byte[] EncodePng(SKImage panel)
    {
        using var data = panel.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    static SKSurface CreatePanelSurface() =>
        SKSurface.Create(new SKImageInfo(PanelWidth, PanelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
}
