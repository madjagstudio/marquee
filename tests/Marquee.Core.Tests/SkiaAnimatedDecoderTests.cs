using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

public sealed class SkiaAnimatedDecoderTests : IDisposable
{
    static readonly SKColor[] Palette = [SKColors.Black, SKColors.Red, SKColors.Lime, SKColors.Blue];
    const byte Red = 1, Green = 2, Blue = 3;

    const int Width = 16, Height = 8;
    static readonly SKRectI Whole = SKRectI.Create(Width, Height);

    readonly string _folder = Path.Combine(Path.GetTempPath(), "marquee-clips-" + Guid.NewGuid().ToString("N")[..8]);

    public SkiaAnimatedDecoderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    string Save(string name, byte[] contents)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    SkiaAnimatedDecoder Gif(params GifFrame[] frames) =>
        new(Save("clip.gif", GifWriter.Write(Width, Height, Palette, frames)));

    static DecodedFrame Next(IVideoDecoder decoder) => decoder.Next() ?? throw new Xunit.Sdk.XunitException("The clip ended early.");

    static SKColor At(DecodedFrame frame, int x, int y) => frame.Pixels.GetPixelColor(x, y);

    [Fact]
    public void Frames_come_in_order_at_the_clip_size()
    {
        using var decoder = Gif(GifFrame.Solid(Whole, Red, 5), GifFrame.Solid(Whole, Green, 5), GifFrame.Solid(Whole, Blue, 5));

        Assert.Equal(new SKSizeI(Width, Height), decoder.Size);
        Assert.Equal(SKColors.Red, At(Next(decoder), 3, 3));
        Assert.Equal(SKColors.Lime, At(Next(decoder), 3, 3));
        var last = Next(decoder);
        Assert.Equal(SKColors.Blue, At(last, 3, 3));
        Assert.Equal((Width, Height), (last.Pixels.Width, last.Pixels.Height));
        Assert.Null(decoder.Next());
        Assert.Null(decoder.Next());
    }

    [Fact]
    public void Starts_accumulate_the_durations_before_them()
    {
        using var decoder = Gif(GifFrame.Solid(Whole, Red, 20), GifFrame.Solid(Whole, Green, 50), GifFrame.Solid(Whole, Blue, 30));

        var frames = new[] { Next(decoder), Next(decoder), Next(decoder) };

        Assert.Equal(new[] { 0, 200, 700 }, frames.Select(f => (int)f.Start.TotalMilliseconds));
        Assert.Equal(new[] { 200, 500, 300 }, frames.Select(f => (int)f.Duration.TotalMilliseconds));
    }

    [Fact]
    public void Delays_of_10_ms_or_less_play_as_100_ms()
    {
        using var decoder = Gif(GifFrame.Solid(Whole, Red, 0), GifFrame.Solid(Whole, Green, 1), GifFrame.Solid(Whole, Blue, 2));

        Assert.Equal(new[] { 100, 100, 20 }, new[] { Next(decoder), Next(decoder), Next(decoder) }
            .Select(f => (int)f.Duration.TotalMilliseconds));
    }

    [Fact]
    public void Rewind_restarts_at_the_first_frame()
    {
        using var decoder = Gif(GifFrame.Solid(Whole, Red, 5), GifFrame.Solid(Whole, Green, 5));
        Next(decoder);
        Next(decoder);
        Assert.Null(decoder.Next());

        decoder.Rewind();

        var first = Next(decoder);
        Assert.Equal(SKColors.Red, At(first, 3, 3));
        Assert.Equal(TimeSpan.Zero, first.Start);
        Assert.Equal(TimeSpan.FromMilliseconds(50), Next(decoder).Start);
    }

    [Fact]
    public void A_partial_frame_draws_over_the_frames_kept_before_it()
    {
        using var decoder = Gif(
            GifFrame.Solid(Whole, Red, 5),
            GifFrame.Solid(new SKRectI(2, 2, 6, 6), Green, 5),
            GifFrame.Solid(new SKRectI(10, 2, 14, 6), Blue, 5));
        Next(decoder);

        var second = Next(decoder);
        Assert.Equal(SKColors.Lime, At(second, 3, 3));
        Assert.Equal(SKColors.Red, At(second, 0, 0));
        Assert.Equal(SKColors.Red, At(second, 11, 3));

        var third = Next(decoder);
        Assert.Equal(SKColors.Lime, At(third, 3, 3));
        Assert.Equal(SKColors.Blue, At(third, 11, 3));
        Assert.Equal(SKColors.Red, At(third, 15, 7));
    }

    [Fact]
    public void A_frame_restored_to_the_background_leaves_transparency()
    {
        using var decoder = Gif(
            GifFrame.Solid(Whole, Red, 5, GifDisposal.RestoreBackground),
            GifFrame.Solid(new SKRectI(2, 2, 6, 6), Green, 5));
        Next(decoder);

        var second = Next(decoder);

        Assert.Equal(SKColors.Lime, At(second, 3, 3));
        Assert.Equal(0, At(second, 11, 3).Alpha);
    }

    [Fact]
    public void A_frame_restored_to_the_previous_one_is_undone()
    {
        using var decoder = Gif(
            GifFrame.Solid(Whole, Red, 5),
            GifFrame.Solid(new SKRectI(2, 2, 6, 6), Green, 5, GifDisposal.RestorePrevious),
            GifFrame.Solid(new SKRectI(10, 2, 14, 6), Blue, 5));
        Next(decoder);
        Next(decoder);

        var third = Next(decoder);

        Assert.Equal(SKColors.Red, At(third, 3, 3));
        Assert.Equal(SKColors.Blue, At(third, 11, 3));
    }

    [Fact]
    public void Rewinding_clears_what_later_frames_drew()
    {
        // The first frame only covers part of the canvas; the rest is transparent each time round, not the last frame.
        using var decoder = Gif(GifFrame.Solid(new SKRectI(2, 2, 6, 6), Red, 5), GifFrame.Solid(Whole, Green, 5));
        Assert.Equal(0, At(Next(decoder), 11, 3).Alpha);
        Next(decoder);

        decoder.Rewind();

        var first = Next(decoder);
        Assert.Equal(SKColors.Red, At(first, 3, 3));
        Assert.Equal(0, At(first, 11, 3).Alpha);
    }

    [Fact]
    public void A_still_image_is_one_frame_of_100_ms()
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        surface.Canvas.Clear(SKColors.Blue);
        using var image = surface.Snapshot();
        using var webp = image.Encode(SKEncodedImageFormat.Webp, 100);
        using var decoder = new SkiaAnimatedDecoder(Save("still.webp", webp.ToArray()));

        var frame = Next(decoder);

        Assert.Equal(new SKSizeI(Width, Height), decoder.Size);
        Assert.Equal(SKColors.Blue, At(frame, 3, 3));
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromMilliseconds(100)), (frame.Start, frame.Duration));
        Assert.Null(decoder.Next());
    }

    [Fact]
    public void A_file_that_is_not_an_image_is_rejected()
    {
        var path = Save("notes.gif", "not a picture"u8.ToArray());

        var ex = Assert.Throws<VideoDecodeException>(() => new SkiaAnimatedDecoder(path));

        Assert.Equal("The clip is not a GIF or WebP image that can be read.", ex.Message);
    }

    [Fact]
    public void An_image_in_another_format_is_rejected()
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height));
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Save("still.gif", png.ToArray());

        var ex = Assert.Throws<VideoDecodeException>(() => new SkiaAnimatedDecoder(path));

        Assert.Equal("The clip is in PNG format, not GIF or WebP.", ex.Message);
    }

    [Fact]
    public void A_missing_file_is_rejected()
    {
        var ex = Assert.Throws<VideoDecodeException>(() => new SkiaAnimatedDecoder(Path.Combine(_folder, "missing.gif")));

        Assert.Equal("The clip was not found.", ex.Message);
    }

    [Theory]
    [InlineData(3841, 1)]
    [InlineData(1, 3841)]
    [InlineData(2161, 2161)]
    public void Clips_larger_than_4K_either_way_round_are_rejected(int width, int height)
    {
        // The header alone says how large every frame is; one tiny frame keeps the file small.
        var path = Save("huge.gif", GifWriter.Write(width, height, Palette, GifFrame.Solid(SKRectI.Create(1, 1), Red, 5)));

        var ex = Assert.Throws<VideoDecodeException>(() => new SkiaAnimatedDecoder(path));

        Assert.Equal($"The clip's frames are {width}×{height}, over the 3840×2160 limit.", ex.Message);
    }

    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(2160, 3840)]
    public void A_4K_clip_is_accepted_either_way_round(int width, int height)
    {
        using var decoder = new SkiaAnimatedDecoder(
            Save("4k.gif", GifWriter.Write(width, height, Palette, GifFrame.Solid(SKRectI.Create(1, 1), Red, 5))));

        Assert.Equal(new SKSizeI(width, height), decoder.Size);
    }
}
