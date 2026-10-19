using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Video layers drawn by the renderer. Frames are decoded ahead on the player's thread, so a later frame is waited for
/// rather than expected at once; the first frame is always ready.
public class VideoRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";
    static readonly SKColor[] Rgb = [SKColors.Red, SKColors.Lime, SKColors.Blue];

    /// Red, green, then blue, 100 ms each, in a 4:1 clip that fills the landscape canvas.
    static byte[] RgbGif() => GifWriter.Write(16, 4, Rgb,
        GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10),
        GifFrame.Solid(SKRectI.Create(16, 4), 2, 10));

    static byte[] RedSquareGif() => GifWriter.Write(8, 8, Rgb, GifFrame.Solid(SKRectI.Create(8, 8), 0, 10));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    /// Renders until the canvas centre is near `expected`, and asserts it got there.
    static void AssertCenterBecomes(ThemeRenderer renderer, TimeSpan time, SKColor expected, int tolerance = 8)
    {
        var center = SKColors.Empty;
        Wait.Until(() =>
        {
            using var frame = renderer.Render(TestSensors.Of(), time);
            center = At(frame, 960, 240);
            return Pixels.Near(center, expected, tolerance);
        });
        Pixels.AssertNear(expected, center, tolerance);
    }

    [Fact]
    public void A_GIF_background_shows_the_frame_for_the_time_and_loops()
    {
        using var folder = new TempTheme(Landscape + "background:\n  - video: clips/rgb.gif\n");
        folder.AddFile("clips/rgb.gif", RgbGif());
        using var theme = folder.Load();

        using (var first = Frames.Render(theme, TestSensors.Of(), TimeSpan.Zero))
        {
            Pixels.AssertNear(SKColors.Red, At(first, 0, 0));
            Pixels.AssertNear(SKColors.Red, At(first, 1919, 479));
        }

        using var renderer = new ThemeRenderer(theme);
        AssertCenterBecomes(renderer, TimeSpan.FromMilliseconds(150), SKColors.Lime);
        AssertCenterBecomes(renderer, TimeSpan.FromMilliseconds(250), SKColors.Blue);
        AssertCenterBecomes(renderer, TimeSpan.FromMilliseconds(350), SKColors.Red);
    }

    [Fact]
    public void Contain_letterboxes_the_clip_over_the_layers_below()
    {
        using var folder = new TempTheme(Landscape + "background:\n  - fill: \"#0000FF\"\n  - video: square.gif\n    fit: contain\n");
        folder.AddFile("square.gif", RedSquareGif());
        using var theme = folder.Load();
        using var frame = Frames.Render(theme, TestSensors.Of());

        Pixels.AssertNear(SKColors.Blue, At(frame, 100, 240));
        Pixels.AssertNear(SKColors.Blue, At(frame, 718, 240));
        Pixels.AssertNear(SKColors.Red, At(frame, 722, 240));
        Pixels.AssertNear(SKColors.Red, At(frame, 960, 240));
        Pixels.AssertNear(SKColors.Red, At(frame, 1197, 240));
        Pixels.AssertNear(SKColors.Blue, At(frame, 1202, 240));
    }

    [Fact]
    public void Opacity_and_blend_apply_as_for_images()
    {
        using var folder = new TempTheme(Landscape + """
            background:
              - fill: "#0000FF"
              - video: half.gif
                opacity: 0.5
              - fill: "#FFFFFF"
                opacity: 0.5
              - video: half.gif
                blend: multiply
            """);
        folder.AddFile("half.gif", GifWriter.Write(16, 4, [new SKColor(255, 0, 255)], GifFrame.Solid(SKRectI.Create(16, 4), 0, 10)));
        using var theme = folder.Load();
        using var frame = Frames.Render(theme, TestSensors.Of());

        // Magenta at half over blue is (128, 0, 255); white at half over that is (191, 128, 255); multiplied by
        // magenta, (191, 0, 255).
        Pixels.AssertNear(new SKColor(191, 0, 255), At(frame, 960, 240), 3);
    }

    [Fact]
    public void An_MP4_background_shows_the_frame_for_the_time()
    {
        using var folder = new TempTheme(Landscape + "background:\n  - video: clip.mp4\n    fit: stretch\n");
        if (!MediaFoundation.IsAvailable)
        {
            // Nothing can write or decode a clip here; check the one thing that can be checked: the layer says why
            // it was skipped.
            folder.AddFile("clip.mp4", [0]);
            using var unavailable = folder.Load();
            Assert.Contains("Media Feature Pack", Assert.Single(unavailable.Warnings));
            return;
        }

        SKColor red = new(200, 40, 40), green = new(40, 160, 60);
        ClipWriter.Write(Path.Combine(folder.Folder, "clip.mp4"), 128, 72, red, green, red);
        using var theme = folder.Load();
        Assert.Empty(theme.Warnings);

        // H.264 is lossy and its color conversions round, so colors are only close to the written ones.
        using (var first = Frames.Render(theme, TestSensors.Of(), TimeSpan.Zero))
            Pixels.AssertNear(red, At(first, 960, 240), 24);
        using var renderer = new ThemeRenderer(theme);
        AssertCenterBecomes(renderer, TimeSpan.FromMilliseconds(150), green, 24);
    }
}
