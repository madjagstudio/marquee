using Marquee.Core.Devices;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Windows' JPEG encoder makes the same picture Skia's does, and Skia takes over when it can't.
public class WicJpegTests
{
    /// A panel-sized frame with smooth gradients, hard edges and colour, like a theme's.
    static SKImage Frame(SKColorType colorType)
    {
        using var surface = SKSurface.Create(new SKImageInfo(480, 1920, colorType, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        using (var gradient = new SKPaint())
        {
            gradient.Shader = SKShader.CreateLinearGradient(new(0, 0), new(480, 1920),
                [new SKColor(20, 0, 60), new SKColor(200, 30, 30), new SKColor(10, 10, 10)], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, 480, 1920, gradient);
        }
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var (color, y) in new[] { (SKColors.Red, 300), (SKColors.Lime, 700), (SKColors.Blue, 1100) })
        {
            paint.Color = color;
            canvas.DrawRect(40, y, 400, 200, paint);
        }
        paint.Color = SKColors.White;
        using var font = new SKFont(SKTypeface.Default, 64);
        canvas.DrawText("7:30:33 PM", 30, 1600, font, paint);
        return surface.Snapshot();
    }

    static double Psnr(SKImage original, byte[] jpeg)
    {
        using var decoded = SKBitmap.Decode(jpeg);
        using var expected = new SKBitmap(new SKImageInfo(original.Width, original.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        original.ReadPixels(expected.Info, expected.GetPixels(), expected.RowBytes, 0, 0);
        using var actual = decoded.Copy(SKColorType.Rgba8888);
        double sum = 0;
        long count = 0;
        var (a, b) = (expected.Bytes, actual.Bytes);
        for (var i = 0; i < a.Length; i++)
        {
            if (i % 4 == 3) continue;
            var d = a[i] - b[i];
            sum += d * d;
            count++;
        }
        return 10 * Math.Log10(255.0 * 255 / (sum / count));
    }

    [Theory]
    [InlineData(SKColorType.Rgba8888)] // a panel from ToPanel
    [InlineData(SKColorType.Bgra8888)] // a portrait canvas sent as it is
    public void Windows_jpeg_matches_skia_quality_with_colours_in_place(SKColorType colorType)
    {
        using var frame = Frame(colorType);
        using var wic = new WicJpeg(new ListLog());

        var windows = wic.Encode(frame, 90)!;
        var skia = FrameEncoder.EncodeJpeg(frame, maxBytes: int.MaxValue);

        Assert.True(Math.Abs(Psnr(frame, windows) - Psnr(frame, skia)) <= 0.5,
            $"Windows {Psnr(frame, windows):0.0} dB, Skia {Psnr(frame, skia):0.0} dB");
        using var decoded = SKImage.FromEncodedData(windows);
        Pixels.AssertNear(SKColors.Red, Images.PixelAt(decoded, 240, 400), 8);
        Pixels.AssertNear(SKColors.Lime, Images.PixelAt(decoded, 240, 800), 8);
        Pixels.AssertNear(SKColors.Blue, Images.PixelAt(decoded, 240, 1200), 8);
    }

    [Fact]
    public void A_see_through_pixel_comes_out_as_over_black_as_with_skia()
    {
        using var surface = SKSurface.Create(new SKImageInfo(480, 1920, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(new SKColor(255, 255, 255, 128));
        using var frame = surface.Snapshot();
        using var wic = new WicJpeg(new ListLog());

        using var windows = SKImage.FromEncodedData(wic.Encode(frame, 90)!);
        using var skia = SKImage.FromEncodedData(FrameEncoder.EncodeJpeg(frame));

        Pixels.AssertNear(Images.PixelAt(skia, 240, 960), Images.PixelAt(windows, 240, 960), 2);
        Pixels.AssertNear(new SKColor(128, 128, 128), Images.PixelAt(windows, 240, 960), 2);
    }

    [Fact]
    public void Quality_still_steps_down_until_the_frame_fits()
    {
        using var noise = Images.Noise(seed: 42);
        using var wic = new WicJpeg(new ListLog());
        var unconstrained = FrameEncoder.EncodeJpeg(noise, wic, maxBytes: int.MaxValue).Length;

        var fitted = FrameEncoder.EncodeJpeg(noise, wic, maxBytes: unconstrained - 1);

        Assert.True(fitted.Length <= unconstrained - 1);
    }

    [Fact]
    public void A_failure_is_logged_once_and_skia_encodes_from_then_on()
    {
        var log = new ListLog();
        using var wic = new WicJpeg(log);
        using var frame = Frame(SKColorType.Rgba8888);
        using var tooWide = SKImage.Create(new SKImageInfo(70_000, 1, SKColorType.Rgba8888, SKAlphaType.Premul)); // past JPEG's 65,535

        Assert.NotNull(tooWide);
        Assert.NotNull(wic.Encode(frame, 90));
        Assert.Null(wic.Encode(tooWide, 90));
        Assert.Null(wic.Encode(frame, 90));
        var jpeg = FrameEncoder.EncodeJpeg(frame, wic);

        Assert.Single(log.Entries, e => e.StartsWith("warn: Encoding frames with Windows' JPEG encoder failed"));
        using var decoded = SKImage.FromEncodedData(jpeg); // made by Skia
        Pixels.AssertNear(SKColors.Red, Images.PixelAt(decoded, 240, 400), 8);
    }

    [Fact]
    public void The_screen_sends_frames_encoded_by_windows()
    {
        var usb = new FakeUsbTransport();
        var log = new ListLog();
        using var device = new UniversalScreen88Device(() => usb, Orientation.Portrait, log: log);
        device.Open();
        using var frame = Frame(SKColorType.Bgra8888);
        using var wic = new WicJpeg(new ListLog());
        using var panel = FrameEncoder.ToPanel(frame, Orientation.Portrait);
        var expected = wic.Encode(panel, 90)!;

        device.PushFrame(frame);

        Assert.Equal(expected, usb.Writes[^1][^expected.Length..]);
        Assert.Empty(log.Entries);
    }
}
