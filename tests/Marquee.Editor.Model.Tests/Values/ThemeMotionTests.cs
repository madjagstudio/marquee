using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Tests.TestSupport;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Model.Tests.Values;

/// Whether the canvas has to be drawn at the theme's fps: while a pulse, a video, a shader or smoothing moves.
public class ThemeMotionTests
{
    const string Still =
        "canvas: { orientation: landscape }\n" +
        "background:\n  - fill: \"#000000\"\n" +
        "widgets:\n" +
        "  - { type: value, at: [0, 0], size: [100, 40], source: cpu.temp }\n";

    readonly ManualTime _time = new();

    static ThemeSpec Parse(string layout) => LayoutParser.Parse(layout);

    static ThemeSpec Example(string name) =>
        Parse(File.ReadAllText(Path.Combine(TempTheme.ExampleFolder(name), ThemeLoader.LayoutFileName)));

    static SensorSnapshot Values(string key, double value)
    {
        var store = new SensorStore();
        store.Set(key, value);
        return store.Snapshot();
    }

    ValueSources Sources(ThemeSpec spec)
    {
        var sources = new ValueSources(new FakeFeed(), new FakeLocal(), _time);
        sources.SetTheme(spec);
        return sources;
    }

    [Fact]
    public void A_theme_of_still_widgets_does_not_move()
    {
        var motion = ThemeMotion.Of(Parse(Still));

        Assert.Equal((false, false, TimeSpan.Zero), (motion.Video, motion.Shader, motion.Smoothing));
        Assert.False(motion.MovesOnItsOwn(new SensorStore().Snapshot()));
        Assert.False(motion.Clock);
    }

    [Fact]
    public void A_theme_that_reads_now_has_a_clock()
    {
        Assert.True(ThemeMotion.Of(Parse(Still + "  - { type: text, at: [0, 50], size: [100, 40], text: \"{now|time:HH:mm:ss}\" }\n")).Clock);
        Assert.True(ThemeMotion.Of(Example("example-showcase")).Clock);
    }

    [Theory]
    [InlineData("background:\n  - video: clip.mp4\n", true, false)]
    [InlineData("background:\n  - shader: s.sksl\n", false, true)]
    [InlineData("background:\n  - { shader: s.sksl, hidden: true }\n", false, false)]
    [InlineData("widgets:\n  - { type: video, at: [0, 0], size: [10, 10], src: clip.mp4 }\n", true, false)]
    [InlineData("widgets:\n  - { type: group, at: [0, 0], size: [10, 10], children: [{ type: video, at: [0, 0], size: [10, 10], src: c.mp4 }] }\n", true, false)]
    [InlineData("widgets:\n  - { type: group, at: [0, 0], size: [10, 10], hidden: true, children: [{ type: video, at: [0, 0], size: [10, 10], src: c.mp4 }] }\n", false, false)]
    public void Videos_and_shaders_always_move_unless_hidden(string section, bool video, bool shader)
    {
        var motion = ThemeMotion.Of(Parse("canvas: { orientation: landscape }\n" + section));

        Assert.Equal((video, shader), (motion.Video, motion.Shader));
        Assert.Equal(video || shader, motion.MovesOnItsOwn(new SensorStore().Snapshot()));
    }

    [Fact]
    public void A_widgets_own_pulse_always_moves()
    {
        var motion = ThemeMotion.Of(Parse(
            "canvas: { orientation: landscape }\nwidgets:\n  - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { pulse: { period: 800 } } }\n"));

        Assert.True(motion.MovesOnItsOwn(new SensorStore().Snapshot()));
    }

    [Fact]
    public void A_rules_pulse_moves_only_while_the_rule_matches()
    {
        var motion = ThemeMotion.Of(Parse(
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: value, at: [0, 0], size: [10, 10], source: cpu.temp, when: [{ above: 85, style: { pulse: { period: 800 } } }] }\n" +
            "  - { type: text, at: [0, 20], size: [10, 10], text: \"{gpu.temp}\", when: [{ stale: true, style: { pulse: { period: 800 } } }] }\n"));

        Assert.False(motion.MovesOnItsOwn(Both(cpu: 50, gpu: 50)));
        Assert.True(motion.MovesOnItsOwn(Both(cpu: 90, gpu: 50)));
        Assert.True(motion.MovesOnItsOwn(Values("cpu.temp", 50)));
    }

    static SensorSnapshot Both(double cpu, double gpu)
    {
        var store = new SensorStore();
        store.Set("cpu.temp", cpu);
        store.Set("gpu.temp", gpu);
        return store.Snapshot();
    }

    [Fact]
    public void Smoothing_is_the_longest_any_visible_widget_has()
    {
        var motion = ThemeMotion.Of(Parse(
            "canvas: { orientation: landscape }\n" +
            "widgets:\n" +
            "  - { type: value, at: [0, 0], size: [10, 10], source: a, smooth: 400 }\n" +
            "  - { type: bar, at: [0, 0], size: [10, 10], source: b, smooth: 900 }\n" +
            "  - { type: bar, at: [0, 0], size: [10, 10], source: c, smooth: 5000, hidden: true }\n"));

        Assert.Equal(TimeSpan.FromMilliseconds(900), motion.Smoothing);
    }

    [Fact]
    public void Smoothing_animates_for_its_length_after_a_change()
    {
        using var sources = Sources(Parse(Still.Replace("source: cpu.temp", "source: cpu.temp, smooth: 400")));

        Assert.True(sources.Animates);
        _time.Advance(TimeSpan.FromMilliseconds(399));
        Assert.True(sources.Animates);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(sources.Animates);

        sources.TestValues.Pin("cpu.temp", TestPin.Of(70));
        Assert.True(sources.Animates);
    }

    [Theory]
    [InlineData("example-landscape", false, false)]
    [InlineData("example-portrait", false, false)]
    [InlineData("example-agents", false, true)]
    [InlineData("example-showcase", true, false)]
    public void Each_example_animates_as_its_parts_say(string example, bool video, bool shader)
    {
        var spec = Example(example);
        var motion = ThemeMotion.Of(spec);
        using var sources = Sources(spec);

        Assert.Equal((video, shader), (motion.Video, motion.Shader));
        Assert.True(motion.Smoothing >= TimeSpan.FromMilliseconds(400), "every example smooths its readings");
        Assert.True(sources.Animates, "smoothing moves right after the theme opens");

        _time.Advance(TimeSpan.FromSeconds(5));

        // With sample values no `hot` rule matches, so only a video or a shader keeps it moving.
        Assert.Equal(video || shader, sources.Animates);
    }

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    public void A_hot_reading_pulses_the_examples(string example)
    {
        using var sources = Sources(Example(example));
        _time.Advance(TimeSpan.FromSeconds(5));

        sources.TestValues.Pin("cpu.temp", TestPin.Of(95));
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.True(sources.Animates);
    }
}
