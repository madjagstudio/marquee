using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The video widget's keys, and the limit on videos a theme plays, which its video layers and video widgets share.
public class VideoWidgetParsingTests
{
    const string Header = "canvas: { orientation: landscape }\n";

    static string Video(string src = "clip.mp4", string extra = "") =>
        $"  - {{ type: video, at: [0, 0], size: [320, 180], src: {src}{extra} }}\n";

    static ThemeSpec Parse(string widgets, string background = "") =>
        Layouts.Parse(Header + (background.Length > 0 ? "background:\n" + background : "") + "widgets:\n" + widgets);

    static VideoWidget Widget(string widget) => Assert.IsType<VideoWidget>(Assert.Single(Parse(widget).Widgets));

    [Fact]
    public void Defaults_to_cover_like_a_video_layer()
    {
        var video = Widget(Video("video/tunnel.mp4"));

        Assert.Equal(new VideoWidget("video/tunnel.mp4", ImageFit.Cover), video with { Bounds = default });
        Assert.Equal(SKRect.Create(0, 0, 320, 180), video.Bounds);
    }

    [Fact]
    public void Takes_a_fit_and_the_common_keys()
    {
        var video = Widget("""
              - type: video
                at: [470, 130]
                size: [360, 210]
                src: video/tunnel.mp4
                fit: contain
                rotate: -8
                locked: true
                style: { radius: 8, stroke: { width: 2, color: "#FFFFFF40" } }

            """);

        Assert.Equal(ImageFit.Contain, video.Fit);
        Assert.Equal(SKRect.Create(470, 130, 360, 210), video.Bounds);
        Assert.Equal(-8f, video.Rotate);
        Assert.True(video.Locked);
        Assert.Equal(8f, video.Style.Radius);
        Assert.Equal(2f, video.Style.Stroke?.Width);
    }

    [Theory]
    [InlineData("a.mp4")]
    [InlineData("a.M4V")]
    [InlineData("clips/a.mov")]
    [InlineData("a.wmv")]
    [InlineData("loops/a.gif")]
    [InlineData("a.Webp")]
    public void Takes_the_clips_a_video_layer_does(string path) => Assert.Equal(path, Widget(Video(path)).Path);

    [Theory]
    [InlineData("  - { type: video, at: [0, 0], size: [10, 10] }", 3, "video widget is missing 'src'")]
    [InlineData("  - { type: video, at: [0, 0], size: [10, 10], src: clip.avi }", 3,
        "src must be a .mp4, .m4v, .mov, .wmv, .gif or .webp file, not 'clip.avi'")]
    [InlineData("  - { type: video, at: [0, 0], size: [10, 10], src: images/a.png }", 3,
        "src must be a .mp4, .m4v, .mov, .wmv, .gif or .webp file, not 'images/a.png'")]
    [InlineData("  - { type: video, at: [0, 0], size: [10, 10], src: [a.mp4] }", 3, "src must be a single value, not a list or mapping")]
    [InlineData("  - { type: video, at: [0, 0], size: [10, 10], src: a.mp4, fit: tile }", 3,
        "fit must be one of: cover, contain, stretch, none; not 'tile'")]
    [InlineData("  - { type: video, at: [0, 0], size: [10, 10], src: a.mp4, loop: false }", 3,
        "Unknown key 'loop' in video widget. Expected one of: type, at, size, source, style, when, smooth, rotate, hidden, locked, src, fit")]
    public void Errors(string widget, int line, string reason)
    {
        var ex = Layouts.Error(Header + "widgets:\n" + widget + "\n");

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Theory]
    [InlineData(1920, 480)]
    [InlineData(480, 1920)]
    [InlineData(960, 960)]
    public void A_box_can_cover_as_many_pixels_as_the_canvas(int width, int height) =>
        Assert.Equal(SKRect.Create(0, 0, width, height), Widget($"  - {{ type: video, at: [0, 0], size: [{width}, {height}], src: a.mp4 }}\n").Bounds);

    [Theory]
    [InlineData("[1920, 481]", "a video widget can cover at most 921,600 pixels, as many as the canvas; this one covers 923,520")]
    [InlineData("[961, 960]", "a video widget can cover at most 921,600 pixels, as many as the canvas; this one covers 922,560")]
    [InlineData("[100000, 100000]", "a video widget can cover at most 921,600 pixels, as many as the canvas; this one covers 10,000,000,000")]
    public void A_box_larger_than_the_canvas_is_an_error(string size, string reason)
    {
        // The player scales each frame to the box and holds several, so the box's size is what a clip costs in memory.
        var ex = Layouts.Error(Header + "widgets:\n  - type: video\n    at: [0, 0]\n    size: " + size + "\n    src: a.mp4\n");

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(5, ex.Line);
    }

    [Fact]
    public void A_theme_can_have_4_video_widgets()
    {
        Assert.Equal(4, WidgetParser.MaxVideos);

        Assert.Equal(4, Parse(string.Concat(Enumerable.Repeat(Video(), 4))).Widgets.Count);
    }

    [Fact]
    public void A_fifth_video_widget_is_an_error_at_its_line()
    {
        var ex = Layouts.Error(Header + "widgets:\n" + string.Concat(Enumerable.Repeat(Video(), 5)));

        Assert.Equal("a theme can have at most 4 videos, counting its video layers and video widgets", ex.Reason);
        Assert.Equal(7, ex.Line); // the fifth widget
    }

    [Fact]
    public void Video_layers_and_video_widgets_count_together()
    {
        // Each video decodes on its own thread and holds frames in memory, wherever it is drawn; other layers don't count.
        const string layers = "  - video: a.mp4\n  - fill: \"#000\"\n  - video: b.gif\n";
        var spec = Parse(Video() + Video("c.webp"), layers);
        Assert.Equal(2, spec.Background.Count(l => l is VideoLayer));
        Assert.Equal(2, spec.Widgets.Count);

        var ex = Layouts.Error(Header + "background:\n" + layers + "widgets:\n" + Video() + Video() + Video());

        Assert.Equal("a theme can have at most 4 videos, counting its video layers and video widgets", ex.Reason);
        Assert.Equal(9, ex.Line); // the third widget, the fifth video
    }

    [Fact]
    public void Video_widgets_in_groups_and_hidden_ones_count()
    {
        // A hidden video doesn't play, but it plays the moment it is shown, and the theme must still be within the limit then.
        var ex = Layouts.Error(Header + "widgets:\n" + Video(extra: ", hidden: true") + Video() + Video() + """
              - type: group
                at: [0, 0]
                size: [400, 400]
                children:
                  - { type: video, at: [0, 0], size: [10, 10], src: a.gif }
                  - { type: video, at: [0, 0], size: [10, 10], src: b.gif }

            """);

        Assert.Equal("a theme can have at most 4 videos, counting its video layers and video widgets", ex.Reason);
        Assert.Equal(11, ex.Line); // the group's second child
    }

    [Fact]
    public void Validate_reports_the_first_video_over_the_limit_and_carries_on()
    {
        var (spec, errors) = Layouts.Collect(Header + "background:\n  - video: a.mp4\n  - video: b.mp4\n  - video: c.mp4\nwidgets:\n"
            + Video() + Video() + Video() + Video() + "  - { type: value, at: [0, 0], size: [10, 10] }\n");

        // The third layer is over the background's own limit and left out, so it doesn't count towards the theme's.
        Assert.Equal(
            [
                "5: a background can have at most 2 video layers",
                "9: a theme can have at most 4 videos, counting its video layers and video widgets",
                "11: value widget is missing 'source'",
            ],
            Layouts.Lines(errors));
        Assert.Equal(2, spec.Background.Count);
        Assert.Equal(2, spec.Widgets.Count);
    }
}
