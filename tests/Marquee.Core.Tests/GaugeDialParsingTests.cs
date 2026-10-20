using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A gauge's `needle:` and `ticks:` blocks, and `thickness: 0` for a dial without an arc.
public class GaugeDialParsingTests
{
    const string Header = """
        canvas: { orientation: landscape }
        widgets:

        """;

    /// The header is 2 lines long, so the gauge starts on line 3 and its extra keys on line 7.
    static string Layout(string extra) => Header + $"""
        - type: gauge
          at: [0, 0]
          size: [200, 200]
          source: gpu.temp
          {extra}
        """;

    static GaugeWidget Gauge(string extra) => Assert.IsType<GaugeWidget>(Assert.Single(Layouts.Parse(Layout(extra)).Widgets));

    static ThemeException Error(string extra) => Layouts.Error(Layout(extra));

    [Fact]
    public void A_gauge_has_no_needle_or_ticks_unless_it_asks()
    {
        var gauge = Gauge("thickness: 12");

        Assert.Null(gauge.Needle);
        Assert.Null(gauge.Ticks);
    }

    [Fact]
    public void An_empty_needle_takes_its_defaults()
    {
        Assert.Equal(new NeedleSpec(4, 0.8f, null, 0), Gauge("needle: {}").Needle);
    }

    [Fact]
    public void A_needle_reads_every_key()
    {
        var needle = Gauge("needle: { width: 6, length: 0.65, color: \"#FF5A36\", cap: 10 }").Needle;

        Assert.Equal(new NeedleSpec(6, 0.65f, new SKColor(0xFF, 0x5A, 0x36), 10), needle);
    }

    [Fact]
    public void Empty_ticks_take_their_defaults()
    {
        Assert.Equal(new TickSpec(10, 0, 10, 2, null, null), Gauge("ticks: {}").Ticks);
    }

    [Fact]
    public void Ticks_read_every_key()
    {
        var ticks = Gauge("ticks: { major: 8, minor: 4, length: 14, width: 3, color: \"#FFFFFF80\", labels: \"{0:0}°\" }").Ticks;

        Assert.Equal(new TickSpec(8, 4, 14, 3, new SKColor(0xFF, 0xFF, 0xFF, 0x80), "{0:0}°"), ticks);
    }

    [Theory]
    [InlineData("labels: false")]
    [InlineData("labels: \"false\"")]
    public void Labels_false_means_no_labels(string labels)
    {
        Assert.Null(Gauge($"ticks: {{ {labels} }}").Ticks!.Labels);
    }

    [Fact]
    public void Labels_take_a_helper_name_as_a_value_widgets_format_does()
    {
        Assert.Equal("bytes", Gauge("ticks: { labels: bytes }").Ticks!.Labels);
    }

    [Fact]
    public void Thickness_0_is_a_dial_without_an_arc()
    {
        Assert.Equal(0f, Gauge("thickness: 0").Thickness);
    }

    [Theory]
    [InlineData("needle: { width: 1 }")]
    [InlineData("needle: { length: 0.1 }")]
    [InlineData("needle: { length: 1 }")]
    [InlineData("needle: { cap: 0 }")]
    [InlineData("ticks: { major: 1 }")]
    [InlineData("ticks: { major: 20 }")]
    [InlineData("ticks: { minor: 0 }")]
    [InlineData("ticks: { minor: 10 }")]
    [InlineData("ticks: { length: 1 }")]
    [InlineData("ticks: { width: 1 }")]
    public void Values_at_the_ends_of_each_range_parse(string extra)
    {
        Assert.NotNull(Gauge(extra));
    }

    [Theory]
    [InlineData("thickness: -1", "thickness must be at least 0")]
    [InlineData("needle: { width: 0.9 }", "needle.width must be at least 1")]
    [InlineData("needle: { length: 0.09 }", "needle.length must be between 0.1 and 1")]
    [InlineData("needle: { length: 1.01 }", "needle.length must be between 0.1 and 1")]
    [InlineData("needle: { cap: -1 }", "needle.cap must be at least 0")]
    [InlineData("needle: { color: red }", "needle.color: 'red' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("needle: { size: 4 }", "Unknown key 'size' in needle. Expected one of: width, length, color, cap")]
    [InlineData("needle: 4", "needle must be a mapping of keys to values")]
    [InlineData("ticks: { major: 0 }", "ticks.major must be between 1 and 20")]
    [InlineData("ticks: { major: 21 }", "ticks.major must be between 1 and 20")]
    [InlineData("ticks: { major: 2.5 }", "ticks.major must be a whole number")]
    [InlineData("ticks: { minor: -1 }", "ticks.minor must be between 0 and 10")]
    [InlineData("ticks: { minor: 11 }", "ticks.minor must be between 0 and 10")]
    [InlineData("ticks: { length: 0.5 }", "ticks.length must be at least 1")]
    [InlineData("ticks: { width: 0 }", "ticks.width must be at least 1")]
    [InlineData("ticks: { color: \"#12\" }", "ticks.color: '#12' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("ticks: { labels: \"°C\" }", "ticks.labels: format \"°C\" never shows the value; include {0}, e.g. \"{0:0.0}°C\"")]
    [InlineData("ticks: { labels: true }", "ticks.labels takes a format such as \"{0:0}\", or false for no labels")]
    [InlineData("ticks: { labels: False }", "ticks.labels takes a format such as \"{0:0}\", or false for no labels")]
    [InlineData("ticks: { labels: no }", "ticks.labels takes a format such as \"{0:0}\", or false for no labels")]
    [InlineData("ticks: { labels: YES }", "ticks.labels takes a format such as \"{0:0}\", or false for no labels")]
    [InlineData("ticks: { lables: \"{0}\" }", "Unknown key 'lables' in ticks. Expected one of: major, minor, length, width, color, labels")]
    [InlineData("ticks: [10]", "ticks must be a mapping of keys to values")]
    public void Dial_errors_point_at_their_line(string extra, string reason)
    {
        var error = Error(extra);

        Assert.Equal(reason, error.Reason);
        Assert.Equal(7, error.Line);
    }

    [Fact]
    public void A_key_on_its_own_line_is_reported_at_that_line()
    {
        var error = Layouts.Error(Header + """
            - type: gauge
              at: [0, 0]
              size: [200, 200]
              source: gpu.temp
              ticks:
                major: 10
                minor: 40
            """);

        Assert.Equal((9, "ticks.minor must be between 0 and 10"), (error.Line, error.Reason));
    }

    [Fact]
    public void Needle_and_ticks_are_gauge_keys_only()
    {
        var error = Layouts.Error(Header + "- { type: bar, at: [0, 0], size: [10, 10], source: x, needle: {} }");

        Assert.StartsWith("Unknown key 'needle' in bar widget.", error.Reason);
    }

    /// `marquee validate` reports a broken dial and still checks the widgets after it.
    [Fact]
    public void Validate_reports_each_dials_mistake()
    {
        var (spec, errors) = Layouts.Collect(Header + """
            - { type: gauge, at: [0, 0], size: [200, 200], source: gpu.temp, needle: { length: 2 } }
            - { type: gauge, at: [0, 0], size: [200, 200], source: gpu.temp, ticks: { major: 30 } }
            - { type: gauge, at: [0, 0], size: [200, 200], source: gpu.temp, needle: {}, ticks: { labels: "{0:0}" } }
            """);

        Assert.Equal(
            ["3: needle.length must be between 0.1 and 1", "4: ticks.major must be between 1 and 20"],
            Layouts.Lines(errors));
        Assert.NotNull(Assert.IsType<GaugeWidget>(Assert.Single(spec.Widgets)).Needle);
    }
}
