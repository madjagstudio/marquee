using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// Video widgets loaded and drawn: a clip in a box, played by the video layer's player, over the box's fill and under
/// its stroke. Frames are decoded ahead on the player's thread, so a later frame is waited for rather than expected at
/// once; the first frame is always ready.
public class VideoWidgetTests
{
    const string Landscape = "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#203040\"\nwidgets:\n";
    static readonly SKColor Background = new(0x20, 0x30, 0x40);
    static readonly SKColor[] Rgb = [SKColors.Red, SKColors.Lime, SKColors.Blue];

    /// Red, green, then blue, 100 ms each, 4:1.
    static byte[] RgbGif() => GifWriter.Write(16, 4, Rgb,
        GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10),
        GifFrame.Solid(SKRectI.Create(16, 4), 2, 10));

    static byte[] RedSquareGif() => GifWriter.Write(8, 8, Rgb, GifFrame.Solid(SKRectI.Create(8, 8), 0, 10));

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    static VideoWidget Widget(LoadedTheme theme, int index = 0) => (VideoWidget)theme.Spec.Widgets[index];

    /// Reports each cost in turn (the last one from then on) against 33 ms frames, and records where it was asked to
    /// scale to.
    sealed class FakeProbe(params double[] milliseconds)
    {
        public List<SKRectI> Calls { get; } = [];

        public DecodeCost Measure(IVideoDecoder decoder, FrameScaler scaler)
        {
            Calls.Add(scaler.Destination);
            return new DecodeCost(TimeSpan.FromMilliseconds(milliseconds[Math.Min(Calls.Count, milliseconds.Length) - 1]),
                TimeSpan.FromMilliseconds(33));
        }
    }

    [Fact]
    public void Plays_its_clip_inside_its_box_and_loops()
    {
        using var folder = new TempTheme(Landscape + "  - { type: video, at: [100, 100], size: [320, 80], src: clips/rgb.gif }\n");
        folder.AddFile("clips/rgb.gif", RgbGif());
        using var theme = folder.Load();
        Assert.Empty(theme.Warnings);

        using (var first = Frames.Render(theme, TestSensors.Of(), TimeSpan.Zero))
        {
            Pixels.AssertNear(SKColors.Red, At(first, 100, 100));
            Pixels.AssertNear(SKColors.Red, At(first, 419, 179));
            Pixels.AssertNear(Background, At(first, 99, 140));
            Pixels.AssertNear(Background, At(first, 420, 140));
            Pixels.AssertNear(Background, At(first, 260, 180));
        }

        using var renderer = new ThemeRenderer(theme);
        AssertBecomes(renderer, TimeSpan.FromMilliseconds(150), SKColors.Lime);
        AssertBecomes(renderer, TimeSpan.FromMilliseconds(250), SKColors.Blue);
        AssertBecomes(renderer, TimeSpan.FromMilliseconds(350), SKColors.Red);
    }

    /// Renders until the box's centre is near `expected`, and asserts it got there.
    static void AssertBecomes(ThemeRenderer renderer, TimeSpan time, SKColor expected)
    {
        var center = SKColors.Empty;
        Wait.Until(() =>
        {
            using var frame = renderer.Render(TestSensors.Of(), time);
            center = At(frame, 260, 140);
            return Pixels.Near(center, expected);
        });
        Pixels.AssertNear(expected, center);
    }

    [Fact]
    public void The_frame_is_over_the_fill_under_the_stroke_and_clipped_to_the_radius()
    {
        using var folder = new TempTheme(Landscape + """
              - type: video
                at: [100, 100]
                size: [400, 100]
                src: square.gif
                fit: contain
                style: { fill: "#0000FF", radius: 20, stroke: { width: 4, color: "#FFFFFF" } }

            """);
        folder.AddFile("square.gif", RedSquareGif());
        using var theme = folder.Load();
        using var frame = Frames.Render(theme, TestSensors.Of());

        Pixels.AssertNear(SKColors.Red, At(frame, 300, 150)); // the clip, letterboxed to 100×100 in the middle
        Pixels.AssertNear(SKColors.Blue, At(frame, 200, 150)); // the fill beside it
        Pixels.AssertNear(SKColors.White, At(frame, 101, 150)); // the stroke, inside the box's edge
        Pixels.AssertNear(SKColors.White, At(frame, 300, 101)); // the stroke over the clip
        Pixels.AssertNear(Background, At(frame, 101, 101)); // a rounded corner
        Pixels.AssertNear(Background, At(frame, 300, 99));
    }

    [Fact]
    public void A_cover_clip_is_clipped_to_its_box_and_radius()
    {
        using var folder = new TempTheme(Landscape + """
              - { type: video, at: [100, 100], size: [100, 100], src: rgb.gif, style: { radius: 50 } }

            """);
        folder.AddFile("rgb.gif", RgbGif());
        using var theme = folder.Load();
        using var frame = Frames.Render(theme, TestSensors.Of());

        // The 4:1 clip covers the square box, a circle with this radius, and nothing past it.
        Pixels.AssertNear(SKColors.Red, At(frame, 150, 150));
        Pixels.AssertNear(SKColors.Red, At(frame, 150, 102));
        Pixels.AssertNear(Background, At(frame, 103, 103));
        Pixels.AssertNear(Background, At(frame, 99, 150));
        Pixels.AssertNear(Background, At(frame, 201, 150));
    }

    [Fact]
    public void A_missing_clip_is_skipped_and_the_box_still_drawn()
    {
        using var folder = new TempTheme(Landscape + """
              - { type: video, at: [100, 100], size: [200, 100], src: clips/rain.mp4, style: { fill: "#00FF00" } }

            """);
        using var theme = folder.Load();

        Assert.Equal(["Video 'clips/rain.mp4' was skipped: The file was not found."], theme.Warnings);
        Assert.Null(theme.Assets.Video(Widget(theme)));
        using var frame = Frames.Render(theme, TestSensors.Of());
        Pixels.AssertNear(SKColors.Lime, At(frame, 200, 150));
    }

    [Fact]
    public void Hidden_video_widgets_are_not_loaded_but_still_validated()
    {
        using var folder = new TempTheme(Landscape + """
              - { type: video, at: [0, 0], size: [200, 100], src: shown.gif }
              - { type: video, at: [0, 0], size: [200, 100], src: hidden.gif, hidden: true }
              - type: group
                at: [0, 0]
                size: [400, 400]
                hidden: true
                children:
                  - { type: video, at: [0, 0], size: [200, 100], src: in-hidden-group.gif }

            """);
        folder.AddFile("shown.gif", RgbGif());
        using var theme = folder.Load();

        // Neither hidden clip exists, and loading doesn't look for them: no thread decodes a clip nobody sees.
        Assert.Empty(theme.Warnings);
        Assert.NotNull(theme.Assets.Video(Widget(theme)));
        Assert.Null(theme.Assets.Video(Widget(theme, 1)));
        var child = (VideoWidget)((GroupWidget)theme.Spec.Widgets[2]).Children[0];
        Assert.Null(theme.Assets.Video(child));

        Assert.Equal(
            [
                "hidden.gif:0: error: Video 'hidden.gif' was skipped: The file was not found.",
                "in-hidden-group.gif:0: error: Video 'in-hidden-group.gif' was skipped: The file was not found.",
            ],
            Lines(Validate(folder.Folder)));
    }

    [Fact]
    public void A_clip_too_slow_for_its_frames_is_skipped_with_its_cost()
    {
        using var folder = new TempTheme(Landscape + "  - { type: video, at: [0, 0], size: [320, 80], src: rgb.gif }\n");
        folder.AddFile("rgb.gif", RgbGif());
        using var theme = ThemeLoader.Load(folder.Folder, null, TestProbes.Free, new FakeProbe(45).Measure);

        Assert.Equal(["Video 'rgb.gif' was skipped: Decoding a frame takes about 45 ms, longer than the 33 ms " +
            "each frame is shown, so playback can't keep up. Use a smaller clip, or one with fewer frames per second."],
            theme.Warnings);
        Assert.Null(theme.Assets.Video(Widget(theme)));
    }

    [Fact]
    public void Frames_are_scaled_to_the_part_of_the_box_they_cover()
    {
        // Measured, decoded and kept at that size: a 4K clip in a small box costs what the box does, not the canvas.
        using var folder = new TempTheme(Landscape + """
              - { type: video, at: [100, 50], size: [400, 200], src: square.gif, fit: contain }
              - type: group
                at: [1000, 100]
                size: [400, 300]
                children:
                  - { type: video, at: [10, 20], size: [160, 90], src: square.gif }

            """);
        folder.AddFile("square.gif", RedSquareGif());
        var probe = new FakeProbe(1);
        using var theme = ThemeLoader.Load(folder.Folder, null, TestProbes.Free, probe.Measure);

        // A group's child is placed in the group, which the painter has moved to by the time it draws it.
        Assert.Equal([new SKRectI(200, 50, 400, 250), new SKRectI(10, 20, 170, 110)], probe.Calls);
        Assert.Equal(new SKRectI(200, 50, 400, 250), theme.Assets.Video(Widget(theme))!.Destination);
    }

    [Fact]
    public void Players_stop_when_the_theme_is_disposed()
    {
        using var folder = new TempTheme(Landscape + """
              - { type: video, at: [0, 0], size: [320, 80], src: rgb.gif }
              - type: group
                at: [0, 100]
                size: [400, 300]
                children:
                  - { type: video, at: [0, 0], size: [320, 80], src: rgb.gif }

            """);
        folder.AddFile("rgb.gif", RgbGif());
        var theme = folder.Load();
        VideoPlayer[] players =
        [
            theme.Assets.Video(Widget(theme))!,
            theme.Assets.Video((VideoWidget)((GroupWidget)theme.Spec.Widgets[1]).Children[0])!,
        ];
        Assert.All(players, player => Assert.NotNull(player.FrameAt(TimeSpan.Zero)));

        theme.Dispose();

        Assert.All(players, player => Assert.Null(player.FrameAt(TimeSpan.Zero)));
    }

    /// The widget's own opacity, rotation and shadow, and fits, in one picture of a real H.264 clip written by
    /// ClipWriter: a blue frame with a stripe down each side and a white disc in the middle, which shows what each fit
    /// keeps.
    [Fact]
    public void Video_widgets_snapshot()
    {
        using var folder = new TempTheme("""
            canvas: { orientation: landscape }
            background:
              - fill: { linear: ["#1E2A44", "#3A4A6A"], angle: 90 }
            widgets:
              - type: video
                at: [40, 40]
                size: [400, 400]
                src: clip.mp4
                style: { radius: 32, stroke: { width: 4, color: "#FFFFFF" } }
              - type: video
                at: [480, 40]
                size: [480, 400]
                src: clip.mp4
                fit: contain
                style: { fill: "#36D1DC40", radius: 24, stroke: { width: 3, color: "#36D1DC" } }
              - type: video
                at: [1010, 90]
                size: [420, 300]
                src: clip.mp4
                fit: stretch
                rotate: -6
                style: { radius: 16, opacity: 0.8, shadow: { color: "#000000C0", blur: 24, x: 12, y: 12 } }
              - type: group
                at: [1480, 40]
                size: [400, 400]
                style: { fill: "#FFFFFF18", radius: 24 }
                children:
                  - { type: video, at: [40, 155], size: [320, 90], src: clip.mp4, fit: none, style: { stroke: { width: 2, color: "#FFB347" } } }
            """);
        var clip = Path.Combine(folder.Folder, "clip.mp4");
        if (!MediaFoundation.IsAvailable)
        {
            // Nothing can write or decode a clip here; check the one thing that can be checked: each widget says why
            // it was skipped.
            File.WriteAllBytes(clip, [0]);
            using var unavailable = folder.Load();
            Assert.All(unavailable.Warnings, warning => Assert.Contains("Media Feature Pack", warning));
            return;
        }

        ClipWriter.Write(clip, 160, 90, 2, (canvas, i) =>
        {
            canvas.Clear(new SKColor(40, 60, 160));
            using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(220, 40, 160) };
            canvas.DrawRect(0, 0, 16, 90, paint);
            paint.Color = new SKColor(240, 150, 30);
            canvas.DrawRect(144, 0, 16, 90, paint);
            paint.Color = i == 0 ? SKColors.White : SKColors.Lime;
            canvas.DrawCircle(80, 45, 30, paint);
        });
        using var theme = folder.Load();
        Assert.Empty(theme.Warnings);

        // The first frame, which every player has decoded by the time the theme has loaded.
        using var frame = Frames.Render(theme, TestSensors.Of(), TimeSpan.Zero);
        Snapshots.Match(frame, "video-widgets");
    }
}
