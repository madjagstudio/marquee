using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A player's frames are scaled into buffers that are reused, not allocated anew for every frame, and a buffer is only
/// reused once nothing holds the image in it any more.
public sealed class FrameBuffersTests : IDisposable
{
    readonly SKBitmap _source = new(new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Opaque));
    readonly SKPixmap _pixels;
    readonly FrameBuffers _buffers = new(new FrameScaler(ImageFit.Stretch, new SKSizeI(4, 4), SKRect.Create(8, 8)));

    public FrameBuffersTests() => _pixels = _source.PeekPixels();

    public void Dispose()
    {
        _buffers.Dispose();
        _pixels.Dispose();
        _source.Dispose();
    }

    SKImage Scale(SKColor color)
    {
        _source.Erase(color);
        return _buffers.Scale(_pixels);
    }

    static SKColor ColorOf(SKImage image) => image.PeekPixels().GetPixelColor(1, 1);

    [Fact]
    public void A_buffer_is_reused_once_the_image_in_it_is_released()
    {
        for (var i = 0; i < 20; i++)
        {
            using var frame = Scale(i % 2 == 0 ? SKColors.Red : SKColors.Blue);
            Assert.Equal(i % 2 == 0 ? SKColors.Red : SKColors.Blue, ColorOf(frame));
        }

        Assert.Equal(1, _buffers.Created);
    }

    [Fact]
    public void A_held_image_keeps_its_pixels_however_many_frames_are_scaled_after_it()
    {
        using var held = Scale(SKColors.Red);

        for (var i = 0; i < 10; i++) Scale(SKColors.Blue).Dispose();

        Assert.Equal(SKColors.Red, ColorOf(held));
        Assert.Equal(2, _buffers.Created);
    }

    [Fact]
    public void An_image_still_held_when_the_buffers_are_disposed_stays_readable_until_it_is_released()
    {
        var held = Scale(SKColors.Lime);

        _buffers.Dispose();

        Assert.Equal(SKColors.Lime, ColorOf(held));
        held.Dispose(); // frees its buffer now, rather than returning it
    }
}
