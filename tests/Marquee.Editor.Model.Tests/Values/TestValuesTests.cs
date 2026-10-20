using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Model.Tests.Values;

/// The test values panel's model: the keys a theme reads plus `now`, the pins, and each key's slider range.
public class TestValuesTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: gauge, at: [0, 0], size: [100, 100], source: cpu.temp, min: 20, max: 110 }\n" +
        "  - { type: text, at: [0, 100], size: [100, 40], text: \"{gpu.temp:0}° {now|time}\" }\n" +
        "  - { type: bar, at: [0, 200], size: [100, 20], source: cpu.temp * 2, min: 10, max: 250 }\n" +
        "  - type: group\n" +
        "    at: [200, 0]\n" +
        "    size: [400, 200]\n" +
        "    children:\n" +
        "      - { type: graph, at: [0, 0], size: [200, 100], source: gpu.load, points: 90 }\n" +
        "      - { type: graph, at: [0, 100], size: [200, 100], series: [{ source: gpu.load }, { source: ram.used_pct }] }\n" +
        "      - { type: bar, at: [0, 0], size: [100, 20], source: fps, max: 240, hidden: true }\n";

    static TestValues WithLayout(string layout = Layout)
    {
        var values = new TestValues();
        values.SetTheme(LayoutParser.Parse(layout));
        return values;
    }

    [Fact]
    public void The_list_is_the_keys_the_theme_reads_then_now()
    {
        var values = WithLayout();

        Assert.Equal(["cpu.temp", "gpu.temp", "gpu.load", "ram.used_pct", "fps", "now"], values.Keys);
    }

    [Fact]
    public void The_list_follows_the_theme()
    {
        var values = WithLayout();
        var changes = 0;
        values.Changed += (_, _) => changes++;

        values.SetTheme(LayoutParser.Parse(
            "canvas: { orientation: landscape }\nwidgets:\n  - { type: value, at: [0, 0], size: [10, 10], source: codex.primary.pct }\n"));

        Assert.Equal(["codex.primary.pct", "now"], values.Keys);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Without_a_theme_the_list_is_now_alone()
    {
        var values = new TestValues();

        values.SetTheme(null);

        Assert.Equal(["now"], values.Keys);
    }

    [Fact]
    public void The_slider_range_comes_from_the_gauges_and_bars_reading_the_key()
    {
        var values = WithLayout();

        Assert.Equal(new SliderRange(10, 250), values.RangeOf("cpu.temp"));
        Assert.Equal(new SliderRange(0, 240), values.RangeOf("fps"));
    }

    [Fact]
    public void A_key_no_gauge_or_bar_reads_slides_from_0_to_100()
    {
        var values = WithLayout();

        Assert.Equal(new SliderRange(0, 100), values.RangeOf("gpu.temp"));
        Assert.Equal(new SliderRange(0, 100), values.RangeOf("gpu.load"));
        Assert.Equal(new SliderRange(0, 100), values.RangeOf("not.read"));
    }

    [Fact]
    public void Graph_keys_take_the_longest_graph_reading_them()
    {
        var values = WithLayout();

        Assert.Equal(90, values.GraphPoints("gpu.load"));
        Assert.Equal(60, values.GraphPoints("ram.used_pct"));
        Assert.Equal(0, values.GraphPoints("cpu.temp"));
        Assert.True(values.IsGraphKey("ram.used_pct"));
        Assert.False(values.IsGraphKey("cpu.temp"));
    }

    [Fact]
    public void A_key_pins_to_a_number_stale_or_missing_and_unpins()
    {
        var values = WithLayout();
        var changes = 0;
        values.Changed += (_, _) => changes++;

        values.Pin("cpu.temp", TestPin.Of(95));
        values.Pin("gpu.temp", TestPin.Stale);
        values.Pin("fps", TestPin.Missing);

        Assert.Equal(TestPin.Of(95), values.PinOf("cpu.temp"));
        Assert.Equal(PinKind.Stale, values.PinOf("gpu.temp")!.Kind);
        Assert.Equal(PinKind.Missing, values.PinOf("fps")!.Kind);
        Assert.Equal(3, values.Pins.Count);

        Assert.True(values.Unpin("cpu.temp"));
        Assert.False(values.Unpin("cpu.temp"));
        Assert.Null(values.PinOf("cpu.temp"));
        values.UnpinAll();
        Assert.Empty(values.Pins);
        Assert.Equal(5, changes);
    }

    [Fact]
    public void A_pin_must_be_a_finite_number()
    {
        var values = WithLayout();

        Assert.Throws<ArgumentException>(() => values.Pin("cpu.temp", TestPin.Of(double.NaN)));
        Assert.Throws<ArgumentException>(() => values.Pin("cpu.temp", TestPin.Of(double.PositiveInfinity)));
        Assert.Empty(values.Pins);
    }

    [Fact]
    public void Now_pins_to_a_time_and_not_to_a_number()
    {
        var values = WithLayout();
        var at = new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.FromHours(1));

        values.PinNow(at);

        Assert.Equal(at, values.PinnedNow);
        Assert.Throws<ArgumentException>(() => values.Pin("now", TestPin.Of(1)));
        Assert.True(values.Unpin("now"));
        Assert.Null(values.PinnedNow);
    }

    [Fact]
    public void Pins_outlive_a_theme_change()
    {
        var values = WithLayout();
        values.Pin("cpu.temp", TestPin.Of(70));

        values.SetTheme(LayoutParser.Parse("canvas: { orientation: landscape }\n"));
        values.SetTheme(LayoutParser.Parse(Layout));

        Assert.Equal(TestPin.Of(70), values.PinOf("cpu.temp"));
    }

    [Fact]
    public void A_steady_pattern_fills_the_graph_with_the_value()
    {
        var values = WithLayout();

        var history = values.HistoryOf("gpu.load", TestPin.Of(40, HistoryPattern.Steady));

        Assert.Equal(90, history.Count);
        Assert.All(history, point => Assert.Equal(40, point));
    }

    [Fact]
    public void A_ramp_climbs_from_the_bottom_of_the_range_to_the_value()
    {
        var values = WithLayout();

        var history = values.HistoryOf("gpu.load", TestPin.Of(80, HistoryPattern.Ramp));

        Assert.Equal(90, history.Count);
        Assert.Equal(0, history[0]);
        Assert.Equal(80, history[^1]);
        Assert.True(history.Zip(history.Skip(1)).All(pair => pair.Second >= pair.First), "it only climbs");
    }

    [Fact]
    public void Spikes_jump_from_the_value_toward_the_top_of_the_range_and_end_on_it()
    {
        var values = WithLayout();

        var history = values.HistoryOf("ram.used_pct", TestPin.Of(30, HistoryPattern.Spikes));

        Assert.Equal(60, history.Count);
        Assert.Equal(30, history[^1]);
        Assert.Contains(history, point => point >= 70);
        Assert.True(history.Count(point => point == 30) > history.Count / 2, "mostly the value");
        Assert.All(history, point => Assert.InRange(point, 0, 100));
    }

    [Fact]
    public void Spikes_at_the_top_of_the_range_dip_instead()
    {
        var values = WithLayout();

        var history = values.HistoryOf("ram.used_pct", TestPin.Of(100, HistoryPattern.Spikes));

        Assert.Contains(history, point => point <= 60);
        Assert.Equal(100, history[^1]);
    }

    [Fact]
    public void A_key_no_graph_reads_has_one_point()
    {
        var values = WithLayout();

        Assert.Equal([75.0], values.HistoryOf("cpu.temp", TestPin.Of(75, HistoryPattern.Ramp)));
    }
}
