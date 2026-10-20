using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class WidgetParsingTests
{
    const string Header = """
        canvas: { orientation: landscape }
        styles:
          base: { size: 32, color: "#FFFFFF" }
          hot: { color: "#FF0000" }
        widgets:

        """;

    static IReadOnlyList<WidgetSpec> Widgets(string widgets) => Layouts.Parse(Header + widgets).Widgets;

    static WidgetSpec Widget(string widget) => Assert.Single(Widgets(widget));

    /// Line numbers in errors count from the top of the file; the header is 5 lines long.
    static ThemeException Error(string widgets) => Layouts.Error(Header + widgets);

    [Fact]
    public void Text_widget_with_common_fields()
    {
        var text = Assert.IsType<TextWidget>(Widget("""
            - type: text
              at: [40, 20]
              size: [300, 60]
              text: "{claude.sessions.busy}/{claude.sessions.total} working"
              style: base
              smooth: 250
            """));

        Assert.Equal(SKRect.Create(40, 20, 300, 60), text.Bounds);
        Assert.Equal("2/5 working", text.Text.Render(TestSensors.Of(("claude.sessions.busy", 2), ("claude.sessions.total", 5))).Text);
        Assert.Equal(32f, text.Style.Size);
        Assert.Equal(250f, text.SmoothMs);
        Assert.Null(text.Source);
        Assert.Empty(text.When);
    }

    [Fact]
    public void Value_widget()
    {
        var value = Assert.IsType<ValueWidget>(Widget("""
            - type: value
              at: [0, 0]
              size: [100, 50]
              source: cpu.temp
              format: "{0:0}°C"
            """));

        Assert.Equal("{0:0}°C", value.Format);
        Assert.Equal(72, value.Source!.Evaluate(TestSensors.Of(("cpu.temp", 72)))?.Number);
        Assert.Same(StyleSpec.Empty, value.Style);
    }

    [Fact]
    public void Bar_defaults_and_options()
    {
        var widgets = Widgets("""
            - { type: bar, at: [0, 0], size: [200, 20], source: gpu.load }
            - { type: bar, at: [0, 0], size: [20, 200], source: cpu.power, min: 10, max: 250, direction: vertical }
            """);

        Assert.Equal(new BarWidget(0, 100, BarDirection.Horizontal), widgets[0] with { Bounds = default, Source = null });
        var vertical = Assert.IsType<BarWidget>(widgets[1]);
        Assert.Equal((10, 250, BarDirection.Vertical), (vertical.Min, vertical.Max, vertical.Direction));
    }

    [Fact]
    public void Gauge_defaults_and_options()
    {
        var widgets = Widgets("""
            - { type: gauge, at: [0, 0], size: [200, 200], source: gpu.temp }
            - { type: gauge, at: [0, 0], size: [200, 200], source: gpu.temp, min: 20, max: 95, start: -90, sweep: 360, thickness: 30 }
            """);

        var standard = Assert.IsType<GaugeWidget>(widgets[0]);
        Assert.Equal((0, 100, 135f, 270f, 12f), (standard.Min, standard.Max, standard.StartAngle, standard.SweepAngle, standard.Thickness));
        var ring = Assert.IsType<GaugeWidget>(widgets[1]);
        Assert.Equal((20, 95, -90f, 360f, 30f), (ring.Min, ring.Max, ring.StartAngle, ring.SweepAngle, ring.Thickness));
    }

    [Fact]
    public void Graph_reads_one_key()
    {
        var graph = Assert.IsType<GraphWidget>(Widget("""
            - { type: graph, at: [0, 0], size: [400, 100], source: gpu.load, max: 100, points: 120 }
            """));

        Assert.Equal(("gpu.load", (double?)null, (double?)100, 120), (graph.Series.Single().Key, graph.Min, graph.Max, graph.Points));
    }

    [Fact]
    public void Graph_defaults_to_60_points_and_auto_scale()
    {
        var graph = Assert.IsType<GraphWidget>(Widget("- { type: graph, at: [0, 0], size: [400, 100], source: gpu.load }"));

        Assert.Equal((60, (double?)null, (double?)null), (graph.Points, graph.Min, graph.Max));
    }

    [Fact]
    public void Image_widget_defaults_to_contain()
    {
        var image = Assert.IsType<ImageWidget>(Widget("- { type: image, at: [0, 0], size: [64, 64], src: icons/cpu.png }"));

        Assert.Equal(new ImageWidget("icons/cpu.png", ImageFit.Contain), image with { Bounds = default });
    }

    [Fact]
    public void Group_children_are_relative_to_the_group()
    {
        var group = Assert.IsType<GroupWidget>(Widget("""
            - type: group
              at: [1000, 0]
              size: [400, 480]
              style: { color: "#AAAAFF" }
              children:
                - { type: text, at: [10, 10], size: [100, 40], text: GPU }
                - type: group
                  at: [0, 100]
                  size: [400, 100]
                  children:
                    - { type: value, at: [0, 0], size: [100, 40], source: gpu.temp }
            """));

        Assert.Equal(2, group.Children.Count);
        Assert.Equal(SKRect.Create(10, 10, 100, 40), group.Children[0].Bounds);
        var inner = Assert.IsType<GroupWidget>(group.Children[1]);
        Assert.IsType<ValueWidget>(Assert.Single(inner.Children));
    }

    [Fact]
    public void When_rules_keep_their_order_and_resolve_styles()
    {
        var value = Widget("""
            - type: value
              at: [0, 0]
              size: [100, 50]
              source: cpu.temp
              when:
                - { above: 85, style: hot }
                - { below: 30, style: { color: "#0000FF" } }
                - { equals: idle, style: hot }
                - { stale: true, style: { opacity: 0.5 } }
            """);

        Assert.Equal(
            [WhenCondition.Above, WhenCondition.Below, WhenCondition.Equals, WhenCondition.Stale],
            value.When.Select(r => r.Condition));
        Assert.Equal(85, value.When[0].Number);
        Assert.Equal(SKColors.Red, value.When[0].Style.Color);
        Assert.Equal(SKColors.Blue, value.When[1].Style.Color);
        Assert.Equal("idle", value.When[2].Text);
        Assert.Equal(0.5f, value.When[3].Style.Opacity);
    }

    [Fact]
    public void Text_widgets_without_a_source_can_still_use_stale_rules()
    {
        var text = Widget("""
            - type: text
              at: [0, 0]
              size: [100, 50]
              text: "{cpu.temp}"
              when: [{ stale: true, style: hot }]
            """);

        Assert.Equal(WhenCondition.Stale, Assert.Single(text.When).Condition);
    }

    [Theory]
    [InlineData(WhenCondition.Above, 85, null, 90.0, false, true)]
    [InlineData(WhenCondition.Above, 85, null, 85.0, false, false)]
    [InlineData(WhenCondition.Above, 85, null, 90.0, true, false)]
    [InlineData(WhenCondition.Below, 30, null, 20.0, false, true)]
    [InlineData(WhenCondition.Below, 30, null, null, false, false)]
    [InlineData(WhenCondition.Equals, 3, "3", 3.0, false, true)]
    [InlineData(WhenCondition.Equals, 3, "3", 3.5, false, false)]
    [InlineData(WhenCondition.Equals, double.NaN, "idle", "idle", false, true)]
    [InlineData(WhenCondition.Equals, double.NaN, "idle", "busy", false, false)]
    [InlineData(WhenCondition.Equals, double.NaN, "idle", 3.0, false, false)]
    [InlineData(WhenCondition.Above, 85, null, "hot", false, false)]
    [InlineData(WhenCondition.Stale, double.NaN, null, null, true, true)]
    [InlineData(WhenCondition.Stale, double.NaN, null, 50.0, false, false)]
    public void Rule_matching(WhenCondition condition, double number, string? text, object? value, bool stale, bool matches)
    {
        var rule = new WhenRule(condition, number, text, StyleSpec.Empty);
        SensorValue? sensor = value switch
        {
            double d => SensorValue.Of(d),
            string s => SensorValue.Of(s),
            _ => null,
        };

        Assert.Equal(matches, rule.Matches(sensor, stale));
    }

    [Theory]
    [InlineData("- at: [0, 0]", 6, "widget is missing 'type'")]
    [InlineData("- type: dial", 6, "widget type must be one of: text, value, bar, gauge, graph, image, video, shape, donut, group; not 'dial'")]
    [InlineData("- text", 6, "Each widget must be a mapping with a type, e.g. - type: value")]
    [InlineData("- { type: value, size: [10, 10], source: x }", 6, "value widget is missing 'at'")]
    [InlineData("- { type: value, at: [0, 0], size: [0, 10], source: x }", 6, "size must be at least 1")]
    [InlineData("- { type: value, at: [0], size: [10, 10], source: x }", 6, "at must be a pair of numbers like [10, 20]")]
    [InlineData("- { type: value, at: [0, 0], size: [10, 10] }", 6, "value widget is missing 'source'")]
    [InlineData("- { type: gauge, at: [0, 0], size: [10, 10] }", 6, "gauge widget is missing 'source'")]
    [InlineData("- type: value\n  at: [0, 0]\n  size: [10, 10]\n  source: cpu.temp +", 9, "source: Expression ended unexpectedly at character 11")]
    [InlineData("- type: text\n  at: [0, 0]\n  size: [10, 10]\n  text: \"{cpu.temp\"", 9, "text: Unclosed '{' at character 1")]
    [InlineData("- type: value\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  format: \"°C\"", 10, "format \"°C\" never shows the value; include {0}, e.g. \"{0:0.0}°C\"")]
    [InlineData("- type: value\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  format: \"{0:F99}\"", 10, "number format 'F99' asks for too many digits (the limit is 15)")]
    [InlineData("- type: value\n  at: [0, 0]\n  size: [10, 10]\n  source: x\n  format: \"{0,1000}\"", 10, "format \"{0,1000}\" pads the value to 1000 characters (the limit is 100)")]
    [InlineData("- { type: text, at: [0, 0], size: [10, 10], text: hi, format: x }", 6, "Unknown key 'format' in text widget. Expected one of: type, at, size, source, style, when, smooth, rotate, hidden, locked, name, text")]
    [InlineData("- { type: value, at: [0, 0], size: [10, 10], source: x, style: hott }", 6, "Unknown style 'hott'. Defined styles: base, hot")]
    [InlineData("- { type: bar, at: [0, 0], size: [10, 10], source: x, min: 50, max: 50 }", 6, "bar widget max must be greater than min")]
    [InlineData("- { type: bar, at: [0, 0], size: [10, 10], source: x, direction: up }", 6, "direction must be one of: horizontal, vertical; not 'up'")]
    [InlineData("- { type: gauge, at: [0, 0], size: [10, 10], source: x, sweep: 0 }", 6, "sweep must not be 0")]
    [InlineData("- { type: graph, at: [0, 0], size: [10, 10], source: gpu.load / 100 }", 6, "A graph's source must be a single sensor key, such as gpu.load")]
    [InlineData("- { type: graph, at: [0, 0], size: [10, 10], source: x, points: 1 }", 6, "points must be between 2 and 600")]
    [InlineData("- { type: image, at: [0, 0], size: [10, 10] }", 6, "image widget is missing 'src'")]
    [InlineData("- { type: group, at: [0, 0], size: [10, 10], children: {} }", 6, "children must be a list")]
    public void Widget_errors(string widgets, int line, string reason)
    {
        var ex = Error(widgets);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Theory]
    [InlineData("- { above: 85 }", "when rule is missing 'style'")]
    [InlineData("- { style: hot }", "when rule needs one of: above, below, equals, stale")]
    [InlineData("- { above: 85, below: 10, style: hot }", "when rule can only have one of: above, below")]
    [InlineData("- { above: warm, style: hot }", "above must be a number, not 'warm'")]
    [InlineData("- { stale: false, style: hot }", "stale only takes true; remove the rule instead of setting it to false")]
    [InlineData("- { stale: maybe, style: hot }", "stale must be true or false, not 'maybe'")]
    [InlineData("- { above: 1, style: nope }", "Unknown style 'nope'. Defined styles: base, hot")]
    public void When_rule_errors(string rule, string reason)
    {
        var ex = Error($"""
            - type: value
              at: [0, 0]
              size: [10, 10]
              source: x
              when:
                {rule}
            """);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(11, ex.Line);
    }

    [Fact]
    public void Comparing_rules_need_a_source()
    {
        var ex = Error("""
            - type: text
              at: [0, 0]
              size: [10, 10]
              text: hi
              when:
                - { above: 85, style: hot }
            """);

        Assert.Equal("when 'above' compares the widget's source, but this widget has no source", ex.Reason);
        Assert.Equal(11, ex.Line);
    }

    [Fact]
    public void Errors_inside_groups_point_at_the_child()
    {
        var ex = Error("""
            - type: group
              at: [0, 0]
              size: [400, 400]
              children:
                - { type: text, at: [0, 0], size: [10, 10], text: ok }
                - { type: value, at: [0, 0], size: [10, 10] }
            """);

        Assert.Equal("value widget is missing 'source'", ex.Reason);
        Assert.Equal(11, ex.Line);
    }

    const string TinyText = "{ type: text, at: [0, 0], size: [1, 1], text: x }";

    static string ManyWidgets(int count) => string.Concat(Enumerable.Repeat($"- {TinyText}\n", count));

    /// `levels` groups, each inside the one before, with a text widget at the bottom. Group k starts on line 6 + 4k.
    static string NestedGroups(int levels)
    {
        var yaml = new System.Text.StringBuilder();
        for (var i = 0; i < levels; i++)
        {
            var indent = new string(' ', i * 4);
            yaml.Append($"{indent}- type: group\n{indent}  at: [0, 0]\n{indent}  size: [1, 1]\n{indent}  children:\n");
        }

        return yaml.Append($"{new string(' ', levels * 4)}- {TinyText}\n").ToString();
    }

    [Fact]
    public void A_layout_may_have_2000_widgets() =>
        Assert.Equal(2000, Widgets(ManyWidgets(2000)).Count);

    [Fact]
    public void More_than_2000_widgets_is_an_error()
    {
        var ex = Error(ManyWidgets(2001));

        Assert.Equal("The layout has more than 2000 widgets", ex.Reason);
        Assert.Equal(5 + 2001, ex.Line);
    }

    [Fact]
    public void Aliased_groups_count_every_copy()
    {
        var ex = Error("""
            - &a { type: group, at: [0, 0], size: [1, 1], children: [ {{TinyText}}, {{TinyText}}, {{TinyText}}, {{TinyText}} ] }
            - &b { type: group, at: [0, 0], size: [1, 1], children: [ *a, *a, *a, *a, *a, *a, *a, *a, *a, *a ] }
            - &c { type: group, at: [0, 0], size: [1, 1], children: [ *b, *b, *b, *b, *b, *b, *b, *b, *b, *b ] }
            - { type: group, at: [0, 0], size: [1, 1], children: [ *c, *c, *c, *c, *c, *c, *c, *c, *c, *c ] }
            """.Replace("{{TinyText}}", TinyText));

        Assert.Equal("The layout has more than 2000 widgets", ex.Reason);
    }

    [Fact]
    public void Groups_may_nest_16_levels_deep() =>
        Assert.Single(Widgets(NestedGroups(16)));

    [Fact]
    public void Groups_nested_more_than_16_levels_deep_are_an_error()
    {
        var ex = Error(NestedGroups(17));

        Assert.Equal("Groups are nested more than 16 levels deep", ex.Reason);
        Assert.Equal(6 + 4 * 16, ex.Line);
    }
}
