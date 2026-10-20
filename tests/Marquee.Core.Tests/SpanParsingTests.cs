using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A text widget's `spans:`, which it takes instead of `text:`: 1 to 32 runs, each a template with a style of its own
/// that may set only the text keys.
public class SpanParsingTests
{
    const string Header = """
        canvas: { orientation: landscape }
        styles:
          unit: { size: 30, color: "#36D1DC" }
          boxed: { size: 20, italic: true, fill: "#102030", padding: 8, align: right, opacity: 0.5 }
        widgets:

        """;

    static SpansWidget Spans(string widget) => Assert.IsType<SpansWidget>(Assert.Single(Layouts.Parse(Header + widget).Widgets));

    /// Line numbers in errors count from the top of the file; the header is 5 lines long, so the widget starts on line 6.
    static ThemeException Error(string widget) => Layouts.Error(Header + widget);

    static string Entries(int count) => string.Concat(Enumerable.Range(0, count).Select(i => $"    - {{ text: \"{i}\" }}\n"));

    [Fact]
    public void Spans_with_their_text_and_styles()
    {
        var widget = Spans("""
            - type: text
              at: [140, 500]
              size: [300, 90]
              style: { size: 72, color: "#F4F6FA", align: center }
              spans:
                - { text: "{cpu.temp:0}" }
                - { text: "°C", style: { size: 30, color: "#36D1DC" } }
                - { text: " max", style: unit }
            """);

        Assert.Equal(SKRect.Create(140, 500, 300, 90), widget.Bounds);
        Assert.Equal(72f, widget.Style.Size);
        Assert.Equal(["52", "°C", " max"], widget.Spans.Select(s => s.Text.Render(TestSensors.Of(("cpu.temp", 52.2))).Text));
        Assert.Equal(StyleSpec.Empty, widget.Spans[0].Style);
        Assert.Equal(new StyleSpec { Size = 30, Color = new SKColor(0x36, 0xD1, 0xDC) }, widget.Spans[1].Style);
        Assert.Equal(widget.Spans[1].Style, widget.Spans[2].Style);
    }

    [Fact]
    public void A_span_takes_every_text_key()
    {
        var style = Assert.Single(Spans("""
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - text: x
                  style:
                    font: Inter
                    size: 30
                    weight: bold
                    color: "#FF0000"
                    text_fill: "#00FF00"
                    text_stroke: { width: 2, color: "#000000" }
                    italic: true
                    underline: true
                    strike: true
                    case: upper
                    letter_spacing: 2
                    shadow: { x: 1, y: 1 }
                    glow: { radius: 4 }
            """).Spans).Style;

        Assert.Equal("Inter", style.Font);
        Assert.Equal(30f, style.Size);
        Assert.Equal(700, style.Weight);
        Assert.Equal(SKColors.Red, style.Color);
        Assert.Equal(new SolidPaint(SKColors.Lime), style.TextFill);
        Assert.Equal(new TextStrokeSpec(2, new SolidPaint(SKColors.Black)), style.TextStroke);
        Assert.Equal(true, style.Italic);
        Assert.Equal(true, style.Underline);
        Assert.Equal(true, style.Strike);
        Assert.Equal(LetterCase.Upper, style.Case);
        Assert.Equal(2f, style.LetterSpacing);
        Assert.Equal(new ShadowSpec(X: 1, Y: 1), style.Shadow);
        Assert.Equal(new GlowSpec(Radius: 4), style.Glow);
    }

    [Fact]
    public void Named_styles_and_extends_work_in_spans()
    {
        var widget = Spans("""
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - { text: a, style: unit }
                - { text: b, style: { extends: unit, italic: true } }
            """);

        Assert.Equal(new StyleSpec { Size = 30, Color = new SKColor(0x36, 0xD1, 0xDC) }, widget.Spans[0].Style);
        Assert.Equal(new StyleSpec { Size = 30, Color = new SKColor(0x36, 0xD1, 0xDC), Italic = true }, widget.Spans[1].Style);
    }

    /// A named style is shared, and may be a widget's as well as a span's, so its box keys are left out of a span rather
    /// than refused: the widget's own style places the text.
    [Fact]
    public void A_named_styles_box_keys_are_left_out_of_a_span()
    {
        var widget = Spans("""
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - { text: a, style: boxed }
                - { text: b, style: { extends: boxed, color: "#FF0000" } }
            """);

        Assert.Equal(new StyleSpec { Size = 20, Italic = true }, widget.Spans[0].Style);
        Assert.Equal(new StyleSpec { Size = 20, Italic = true, Color = SKColors.Red }, widget.Spans[1].Style);
    }

    /// Every style key is either a text key, which a span may set, or a box key, which belongs to the widget; a new style
    /// key needs a place in one of the two lists.
    [Fact]
    public void Every_style_key_is_a_text_key_or_a_box_key()
    {
        Assert.Equal(StyleTable.Keys.Order(), SpanParser.TextKeys.Concat(SpanParser.BoxKeys).Append("extends").Order());
        Assert.Empty(SpanParser.TextKeys.Intersect(SpanParser.BoxKeys));
    }

    /// The box keys are left out of a named style, and nothing else is: a style with every key keeps exactly its text
    /// keys.
    [Fact]
    public void Leaving_out_the_box_keys_keeps_every_text_key()
    {
        var full = Layouts.Parse("""
            canvas: { orientation: landscape }
            styles:
              full:
                font: Inter
                size: 30
                weight: bold
                color: "#FF0000"
                fill: "#00FF00"
                track: "#0000FF"
                stroke: { width: 2, color: "#000000" }
                shadow: { x: 1 }
                glow: { radius: 4 }
                pulse: { period: 500 }
                opacity: 0.5
                radius: 4
                padding: 4
                align: center
                blend: screen
                letter_spacing: 2
                line_height: 1.5
                wrap: true
                text_stroke: { width: 2 }
                text_fill: "#00FF00"
                italic: true
                underline: true
                strike: true
                case: upper
            widgets:
              - { type: text, at: [0, 0], size: [100, 40], text: x, style: full }
              - { type: text, at: [0, 0], size: [100, 40], spans: [{ text: x, style: full }] }
            """).Widgets;

        var whole = full[0].Style;
        var span = Assert.Single(Assert.IsType<SpansWidget>(full[1]).Spans).Style;
        Assert.Equal(whole with
        {
            Fill = null, Track = null, Stroke = null, Pulse = null, Opacity = null, Radius = null, Padding = null, Align = null,
            Blend = null, LineHeight = null, Wrap = null,
        }, span);
    }

    [Fact]
    public void A_text_widget_with_both_text_and_spans_is_an_error()
    {
        var ex = Error("- { type: text, at: [0, 0], size: [10, 10], text: hi, spans: [{ text: a }] }");

        Assert.Equal("text widget can only have one of: text, spans", ex.Reason);
        Assert.Equal(6, ex.Line);
    }

    [Fact]
    public void A_text_widget_with_neither_text_nor_spans_is_an_error()
    {
        var ex = Error("- { type: text, at: [0, 0], size: [10, 10] }");

        Assert.Equal("text widget needs one of: text, spans", ex.Reason);
        Assert.Equal(6, ex.Line);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void A_text_widget_takes_1_to_32_spans(int count)
    {
        var widget = Spans("- type: text\n  at: [0, 0]\n  size: [100, 40]\n  spans:\n" + Entries(count));

        Assert.Equal(count, widget.Spans.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void Fewer_than_1_or_more_than_32_spans_is_an_error(int count)
    {
        var ex = Error("- type: text\n  at: [0, 0]\n  size: [100, 40]\n  spans:" + (count == 0 ? " []\n" : "\n" + Entries(count)));

        Assert.Equal($"spans must have between 1 and 32 entries, not {count}", ex.Reason);
        Assert.Equal(count == 0 ? 9 : 10, ex.Line);
    }

    [Theory]
    [InlineData("fill: \"#102030\"", "fill")]
    [InlineData("track: \"#102030\"", "track")]
    [InlineData("stroke: { width: 2, color: \"#102030\" }", "stroke")]
    [InlineData("radius: 4", "radius")]
    [InlineData("padding: 4", "padding")]
    [InlineData("align: center", "align")]
    [InlineData("wrap: true", "wrap")]
    [InlineData("line_height: 1.5", "line_height")]
    [InlineData("opacity: 0.5", "opacity")]
    [InlineData("blend: screen", "blend")]
    [InlineData("pulse: { period: 500 }", "pulse")]
    public void A_box_key_on_a_span_is_an_error(string line, string key)
    {
        var ex = Error($$"""
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - { text: a }
                - text: b
                  style:
                    size: 20
                    {{line}}
            """);

        Assert.Equal($"A span's style can't set '{key}': the text widget's own style sets it for all of its spans", ex.Reason);
        Assert.Equal(14, ex.Line);
    }

    /// Every box key a span's own style sets is reported at once, so fixing one doesn't bring up the next.
    [Fact]
    public void Every_box_key_on_a_span_is_reported_together()
    {
        var ex = Error("""
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - text: a
                  style: { fill: "#102030", size: 20, padding: 4, align: right }
            """);

        Assert.Equal("A span's style can't set 'fill', 'padding' and 'align': the text widget's own style sets them for all of its spans", ex.Reason);
        Assert.Equal(11, ex.Line);
    }

    [Theory]
    [InlineData("{ text: \"{cpu.temp\" }", "text: Unclosed '{' at character 1")]
    [InlineData("{ text: \"{cpu.temp|kelvin}\" }", "text: Unknown helper 'kelvin' (use duration, bytes or time) at character 11")]
    [InlineData("{ style: unit }", "text span is missing 'text'")]
    [InlineData("{ text: a, color: \"#FF0000\" }", "Unknown key 'color' in text span. Expected one of: text, style")]
    [InlineData("{ text: a, style: nope }", "Unknown style 'nope'. Defined styles: unit, boxed")]
    [InlineData("{ text: a, style: { size: huge } }", "size must be a number, not 'huge'")]
    [InlineData("{ text: [a] }", "text must be a single value, not a list or mapping")]
    [InlineData("hello", "text span must be a mapping of keys to values")]
    public void Span_errors(string span, string reason)
    {
        var ex = Error($$"""
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - { text: a }
                - {{span}}
            """);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(11, ex.Line);
    }

    [Fact]
    public void Spans_must_be_a_list()
    {
        var ex = Error("- type: text\n  at: [0, 0]\n  size: [100, 40]\n  spans: { text: a }\n");

        Assert.Equal("spans must be a list", ex.Reason);
        Assert.Equal(9, ex.Line);
    }

    /// `marquee validate` reports a mistake in each span, not just the first, and the widget's siblings after it.
    [Fact]
    public void Validate_reports_every_bad_span()
    {
        var (spec, errors) = Layouts.Collect(Header + """
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - { text: "{cpu.temp" }
                - { text: a, style: { padding: 4 } }
                - { text: ok }
            - { type: text, at: [0, 0], size: [10, 10], text: ok, nope: 1 }
            """);

        Assert.Equal(
        [
            "10: text: Unclosed '{' at character 1",
            "11: A span's style can't set 'padding': the text widget's own style sets it for all of its spans",
            "13: Unknown key 'nope' in text widget. Expected one of: type, at, size, source, style, when, smooth, rotate, hidden, locked, name, text, spans",
        ], Layouts.Lines(errors));
        Assert.Empty(spec.Widgets);
    }

    /// A value widget takes spans instead of a format, and `{0}` in them is its value.
    [Fact]
    public void A_value_widget_takes_spans_instead_of_a_format()
    {
        var widget = Assert.IsType<ValueWidget>(Assert.Single(Layouts.Parse(Header + """
            - type: value
              at: [0, 0]
              size: [100, 40]
              source: cpu.temp
              spans: [{ text: "{0:0}" }, { text: "°C", style: unit }, { text: " {0} of {gpu.temp}" }]
            """).Widgets));

        Assert.Null(widget.Format);
        var sensors = TestSensors.Of(("gpu.temp", 61));
        Assert.Equal(["52", "°C", " 52.25 of 61"], widget.Spans!.Select(s => s.Text.Render(sensors, value: SensorValue.Of(52.25)).Text));
        Assert.Equal(new StyleSpec { Size = 30, Color = new SKColor(0x36, 0xD1, 0xDC) }, widget.Spans![1].Style);
    }

    /// Only a value widget has a value of its own, so `{0}` in a text widget's spans, as in spans copied from a value
    /// widget, is a mistake rather than the number 0. Spans are new, so no theme had it.
    [Theory]
    [InlineData("{0}")]
    [InlineData("{0:0.0}°C")]
    [InlineData("now { 0 |bytes}")]
    public void Zero_in_a_text_widgets_spans_is_an_error(string text)
    {
        var ex = Error($$"""
            - type: text
              at: [0, 0]
              size: [100, 40]
              spans:
                - { text: a }
                - { text: "{{text}}" }
            """);

        Assert.Equal("text: {0} is a value widget's own value, and a text widget has none; use a sensor key, such as {cpu.temp}", ex.Reason);
        Assert.Equal(11, ex.Line);
    }

    /// Other numbers in braces are still numbers, and `{0}` in a text widget's `text` is still the number 0, as it always was.
    [Fact]
    public void Zero_is_still_a_number_outside_a_text_widgets_spans()
    {
        var spans = Spans("- { type: text, at: [0, 0], size: [100, 40], spans: [{ text: \"{1}\" }, { text: \"{0 + 1}\" }] }");
        var text = Assert.IsType<TextWidget>(Assert.Single(Layouts.Parse(Header + "- { type: text, at: [0, 0], size: [100, 40], text: \"{0}\" }").Widgets));

        Assert.Equal(["1", "1"], spans.Spans.Select(s => s.Text.Render(TestSensors.Of()).Text));
        Assert.Equal("0", text.Text.Render(TestSensors.Of()).Text);
    }

    /// A value widget's spans must show its value somewhere, as its format must.
    [Fact]
    public void A_value_widgets_spans_without_its_value_are_an_error()
    {
        var ex = Error("""
            - type: value
              at: [0, 0]
              size: [100, 40]
              source: cpu.temp
              spans: [{ text: "{cpu.temp:0}" }, { text: "°C", style: unit }]
            """);

        Assert.Equal("spans never show the value; include {0} in one of them, e.g. \"{0:0}\"", ex.Reason);
        Assert.Equal(10, ex.Line);
    }

    [Fact]
    public void A_value_widget_with_both_format_and_spans_is_an_error()
    {
        var ex = Error("- { type: value, at: [0, 0], size: [10, 10], source: cpu.temp, format: \"{0}\", spans: [{ text: \"{0}\" }] }");

        Assert.Equal("value widget can only have one of: format, spans", ex.Reason);
        Assert.Equal(6, ex.Line);
    }

    [Fact]
    public void A_box_key_on_a_value_widgets_span_is_an_error()
    {
        var ex = Error("- { type: value, at: [0, 0], size: [10, 10], source: cpu.temp, spans: [{ text: \"{0}\", style: { align: right } }] }");

        Assert.Equal("A span's style can't set 'align': the value widget's own style sets it for all of its spans", ex.Reason);
        Assert.Equal(6, ex.Line);
    }

    [Fact]
    public void Spans_keep_the_widgets_source_rules_and_stale_rule()
    {
        var widget = Spans("""
            - type: text
              at: [0, 0]
              size: [100, 40]
              source: cpu.temp
              spans: [{ text: "{cpu.temp:0}" }, { text: "°C", style: unit }]
              when:
                - { above: 80, style: { color: "#FF0000" } }
                - { stale: true, style: { color: "#5A6275" } }
            """);

        Assert.Equal([WhenCondition.Above, WhenCondition.Stale], widget.When.Select(r => r.Condition));
    }
}
