using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The theme editor's drag preview: a frame drawn with some of the theme's widgets moved, resized or turned in memory,
/// without loading the theme again. A widget standing in for one of the theme's keeps what its original was loaded
/// with: its clip, drawn in the new box, and its smoothing.
public class RendererStandInTests
{
    const string Landscape = "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#203040\"\nwidgets:\n";
    static readonly SKColor Background = new(0x20, 0x30, 0x40);
    static readonly SKColor[] Rgb = [SKColors.Red, SKColors.Lime, SKColors.Blue];

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    static Dictionary<WidgetSpec, WidgetSpec> StandIns(params (WidgetSpec StandIn, WidgetSpec Original)[] pairs)
    {
        var map = new Dictionary<WidgetSpec, WidgetSpec>(ReferenceEqualityComparer.Instance);
        foreach (var (standIn, original) in pairs) map[standIn] = original;
        return map;
    }

    [Fact]
    public void The_widgets_given_are_drawn_instead_of_the_themes_own()
    {
        using var folder = new TempTheme(Landscape + "  - { type: shape, shape: rect, at: [0, 0], size: [40, 40], style: { fill: \"#FF0000\" } }\n");
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme);
        var original = theme.Spec.Widgets[0];
        var moved = original with { Bounds = SKRect.Create(100, 0, 40, 40) };

        using (var preview = renderer.Render(TestSensors.Of(), TimeSpan.Zero, [moved], StandIns((moved, original))))
        {
            Pixels.AssertNear(SKColors.Red, At(preview, 120, 20));
            Pixels.AssertNear(Background, At(preview, 20, 20));
        }

        using var after = renderer.Render(TestSensors.Of(), TimeSpan.Zero);
        Pixels.AssertNear(SKColors.Red, At(after, 20, 20));
        Pixels.AssertNear(Background, At(after, 120, 20));
    }

    [Fact]
    public void A_moved_and_resized_video_widget_plays_its_clip_in_its_new_box()
    {
        using var folder = new TempTheme(Landscape + "  - { type: video, at: [100, 100], size: [320, 80], src: rgb.gif }\n");
        folder.AddFile("rgb.gif", GifWriter.Write(16, 4, Rgb, GifFrame.Solid(SKRectI.Create(16, 4), 0, 10)));
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme);
        var original = theme.Spec.Widgets[0];
        var moved = original with { Bounds = SKRect.Create(500, 200, 160, 40) };

        using var preview = renderer.Render(TestSensors.Of(), TimeSpan.Zero, [moved], StandIns((moved, original)));

        Pixels.AssertNear(SKColors.Red, At(preview, 501, 201));
        Pixels.AssertNear(SKColors.Red, At(preview, 658, 238));
        Pixels.AssertNear(Background, At(preview, 662, 220));
        Pixels.AssertNear(Background, At(preview, 580, 242));
        Pixels.AssertNear(Background, At(preview, 200, 140));
    }

    [Fact]
    public void A_widget_standing_in_eases_on_from_where_its_original_was()
    {
        using var folder = new TempTheme(Landscape +
            "  - { type: bar, at: [0, 0], size: [200, 20], source: x, smooth: 1000, style: { fill: \"#FF0000\", track: \"#000000\" } }\n");
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme);
        var original = theme.Spec.Widgets[0];
        var standIn = original with { };

        using (renderer.Render(TestSensors.Of(("x", 0)), TimeSpan.Zero)) { }
        using (renderer.Render(TestSensors.Of(("x", 100)), TimeSpan.FromSeconds(1), [standIn], StandIns((standIn, original)))) { }
        using var halfway = renderer.Render(TestSensors.Of(("x", 100)), TimeSpan.FromSeconds(1.5), [standIn], StandIns((standIn, original)));

        // Cubic ease-out is 87.5% of the way at half the duration: 175 of the bar's 200 pixels.
        Pixels.AssertNear(SKColors.Red, At(halfway, 170, 10));
        Pixels.AssertNear(SKColors.Black, At(halfway, 180, 10));
    }
}
