using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

/// Frames of the open theme, with the values the session is given (an empty store until the value sources are), and
/// the theme swapped for each change: the new one loaded first, the old one disposed after.
public class EditorSessionRenderTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "background:\n  - fill: \"#000000\"\n" +
        "widgets:\n" +
        "  - { type: shape, shape: rect, at: [10, 10], size: [40, 40], style: { fill: \"#FF0000\" } }\n" +
        "  - { type: bar, at: [100, 10], size: [200, 20], source: x, style: { fill: \"#00FF00\", track: \"#0000FF\" } }\n";

    [Fact]
    public void A_frame_is_the_theme_drawn_at_its_size()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        using var frame = session.RenderFrame(new SensorStore().Snapshot(), TimeSpan.Zero)!;

        Assert.Equal(new SKSizeI(1920, 480), new SKSizeI(frame.Width, frame.Height));
        Frames.AssertNear(SKColors.Red, Frames.PixelAt(frame, 30, 30));
        Frames.AssertNear(SKColors.Black, Frames.PixelAt(frame, 70, 30));
    }

    [Fact]
    public void Frames_read_the_values_the_session_is_given_and_an_empty_store_until_then()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();

        Frames.AssertNear(SKColors.Blue, Frames.PixelAt(session, 290, 20));

        var store = new SensorStore();
        store.Set("x", 100);
        session.Values = store.Snapshot;

        Frames.AssertNear(SKColors.Lime, Frames.PixelAt(session, 290, 20));
    }

    [Fact]
    public void A_change_loads_the_new_theme_then_disposes_the_old_one()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var old = session.Theme!;
        var oldRenderer = session.Renderer;

        session.Apply(new SetValue(YamlPath.Parse("widgets[0].style.fill"), "#FFFFFF"));

        Assert.True(old.IsDisposed);
        Assert.False(session.Theme!.IsDisposed);
        Assert.NotSame(old, session.Theme);
        Assert.NotSame(oldRenderer, session.Renderer);
        Frames.AssertNear(SKColors.White, Frames.PixelAt(session, 30, 30));
    }

    /// The showcase's video widget, as the theme draws it: the clock group's fourth child.
    static VideoWidget Radar(LoadedTheme theme) => (VideoWidget)((GroupWidget)theme.Spec.Widgets[0]).Children[3];

    [Fact]
    public void A_clip_both_themes_play_carries_over_the_swap()
    {
        using var folder = TempTheme.Example("example-showcase");
        using var session = folder.Open();
        var old = session.Theme!;
        var player = old.Assets.Video(Radar(old));
        Assert.NotNull(player);

        // Moving the group the clip is in leaves the clip's own box, in the group's coordinates, as it was.
        session.Apply(new SetValue(YamlPath.Parse("widgets[0].at[0]"), "30"));

        Assert.True(old.IsDisposed);
        Assert.Same(player, session.Theme!.Assets.Video(Radar(session.Theme)));
        using var frame = session.RenderFrame(TimeSpan.Zero);
        Assert.NotNull(frame);
    }

    [Fact]
    public void The_locale_set_on_the_session_goes_to_every_renderer()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        var locale = new Marquee.Core.Expressions.TimeLocale(TimeZoneInfo.Utc, System.Globalization.CultureInfo.InvariantCulture);

        session.Locale = locale;
        Assert.Same(locale, session.Renderer!.Locale);

        session.Apply(new SetValue(YamlPath.Parse("widgets[0].style.fill"), "#FFFFFF"));
        Assert.Same(locale, session.Renderer!.Locale);
    }
}
