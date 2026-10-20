using Marquee.Core.Rendering;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A text widget's spans drawn: runs in their own sizes, colours and letter effects, side by side on a shared baseline,
/// broken into lines and placed in the box as plain text is.
public class SpanRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static readonly SKRectI Everything = new(0, 0, 1920, 480);

    static SKImage Render(string layout, params (string, object)[] values) => Frames.Render(layout, TestSensors.Of(values));

    static void AssertSamePixels(SKImage expected, SKImage actual)
    {
        using var a = SKBitmap.FromImage(expected);
        using var b = SKBitmap.FromImage(actual);
        Assert.True(a.Pixels.SequenceEqual(b.Pixels), "The two frames differ");
    }

    /// The smallest rectangle holding every pixel in the region that matches; empty when none do.
    static SKRectI Lit(SKImage image, SKRectI region, Func<SKColor, bool> matches)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        for (var y = region.Top; y < region.Bottom; y++)
            for (var x = region.Left; x < region.Right; x++)
                if (matches(bitmap.GetPixel(x, y)))
                    (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x + 1), Math.Max(bottom, y + 1));
        return left == int.MaxValue ? SKRectI.Empty : new SKRectI(left, top, right, bottom);
    }

    static bool Reddish(SKColor c) => c.Red > 150 && c.Green < 100 && c.Blue < 100;

    static bool Greenish(SKColor c) => c.Green > 150 && c.Red < 100 && c.Blue < 100;

    static bool Bluish(SKColor c) => c.Blue > 150 && c.Red < 100 && c.Green < 100;

    /// One span with no style of its own is the widget's text in the widget's style, so it draws exactly as `text:` does,
    /// whatever the style: one line, lines broken by `\n` or by wrapping, any alignment and any letter effect.
    [Theory]
    [InlineData("CPU 64°C", "size: 48")]
    [InlineData("Centred and spaced", "size: 40, align: center, letter_spacing: 6, color: \"#36D1DC\"")]
    [InlineData("Right\\nthree\\nlines", "size: 40, align: right, line_height: 1.5")]
    [InlineData("A long line of text that wraps inside its box", "size: 40, wrap: true, padding: 10, fill: \"#1B2440\", radius: 8")]
    [InlineData("Outlined", "size: 72, weight: bold, italic: true, underline: true, text_stroke: { width: 3, color: \"#FF0000\" }, glow: { color: \"#36A2FF\", radius: 12 }, shadow: { x: 4, y: 4 }")]
    [InlineData("gradient case", "size: 64, case: upper, strike: true, text_fill: { linear: [\"#36D1DC\", \"#FF5A36\"] }")]
    public void One_span_draws_exactly_as_the_same_text(string text, string style)
    {
        const string Widget = "  - { type: text, at: [40, 40], size: [600, 300], ";

        using var plain = Render(Landscape + "widgets:\n" + Widget + $"text: \"{text}\", style: {{ {style} }} }}\n");
        using var spans = Render(Landscape + "widgets:\n" + Widget + $"spans: [{{ text: \"{text}\" }}], style: {{ {style} }} }}\n");

        AssertSamePixels(plain, spans);
    }

    /// Spans with no style of their own are the widget's text in pieces, so any number of them draw exactly as the text
    /// does, letter spacing at the joins included.
    [Theory]
    [InlineData("HEL|LO", "size: 80, letter_spacing: 30")]
    [InlineData("H|E|L|L|O", "size: 80, letter_spacing: 30")]
    [InlineData("HE|LLO", "size: 80, letter_spacing: 30, align: center")]
    [InlineData("HELL|O", "size: 80, letter_spacing: 30, align: right")]
    [InlineData("Spaced |text| that wraps", "size: 40, letter_spacing: 12, wrap: true")]
    public void Unstyled_spans_draw_exactly_as_the_same_text(string pieces, string style)
    {
        const string Widget = "  - { type: text, at: [40, 40], size: [600, 300], ";
        var spans = string.Join(", ", pieces.Split('|').Select(piece => $"{{ text: \"{piece}\" }}"));

        using var plain = Render(Landscape + "widgets:\n" + Widget + $"text: \"{pieces.Replace("|", "")}\", style: {{ {style} }} }}\n");
        using var split = Render(Landscape + "widgets:\n" + Widget + $"spans: [{spans}], style: {{ {style} }} }}\n");

        AssertSamePixels(plain, split);
    }

    /// Title case starts a word after white space only, so a word split between spans keeps one capital, as it would in
    /// one span.
    [Theory]
    [InlineData("cpu|temp now")]
    [InlineData("gpu |at (|hot)")]
    public void Title_case_starts_words_only_after_spaces_across_spans(string pieces)
    {
        const string Widget = "  - { type: text, at: [40, 40], size: [800, 200], ";
        var spans = string.Join(", ", pieces.Split('|').Select(piece => $"{{ text: \"{piece}\" }}"));

        using var plain = Render(Landscape + "widgets:\n" + Widget + $"text: \"{pieces.Replace("|", "")}\", style: {{ size: 60, case: title }} }}\n");
        using var split = Render(Landscape + "widgets:\n" + Widget + $"spans: [{spans}], style: {{ size: 60, case: title }} }}\n");

        AssertSamePixels(plain, split);
    }

    /// A widget's own style, a span's style that sets it again, and a span's own style written out are the same thing.
    [Fact]
    public void A_spans_style_over_the_widgets_draws_as_the_widgets_style_alone()
    {
        using var widget = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 120], spans: [{ text: "Hello" }], style: { size: 60, color: "#FF0000" } }
            """);
        using var span = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 120], spans: [{ text: "Hello", style: { size: 60, color: "#FF0000" } }] }
            """);

        AssertSamePixels(widget, span);
    }

    [Fact]
    public void A_big_number_and_a_small_unit_share_a_baseline()
    {
        using var frame = Render(Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [600, 200]
                source: cpu.temp
                style: { size: 120, color: "#FF0000" }
                spans:
                  - { text: "{cpu.temp:0}" }
                  - { text: "°C", style: { size: 40, color: "#0000FF" } }
            """, ("cpu.temp", 52));

        var number = Lit(frame, Everything, Reddish);
        var unit = Lit(frame, Everything, Bluish);
        using var big = new SKFont(BundledFonts.Regular, 120);
        using var small = new SKFont(BundledFonts.Regular, 40);

        // The unit's C sits on the digits' baseline, give or take its round bottom's overshoot.
        Assert.InRange(unit.Bottom - number.Bottom, -2, 2);
        Assert.True(unit.Height < number.Height / 2, $"The unit {unit} should be much smaller than the number {number}");
        // The unit starts where the number's advance ends, with no gap but the font's own side bearings.
        var digitsEnd = 40 + big.MeasureText("52");
        Assert.InRange(unit.Left, digitsEnd - 2, digitsEnd + small.Size / 4);
        // The line is centred on the tallest span: the number sits where it would alone.
        var alone = 40 + 100 - (big.Metrics.Ascent + big.Metrics.Descent) / 2;
        Assert.InRange(number.Bottom, alone - 2, alone + 2);
    }

    [Fact]
    public void The_unit_follows_the_number_as_it_grows()
    {
        const string Layout = Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [800, 200]
                source: cpu.temp
                style: { size: 120, color: "#FF0000" }
                spans: [{ text: "{cpu.temp:0}" }, { text: "°C", style: { size: 40, color: "#0000FF" } }]
            """;

        using var two = Render(Layout, ("cpu.temp", 52));
        using var three = Render(Layout, ("cpu.temp", 152));

        // It moves right by the new digit's advance.
        using var big = new SKFont(BundledFonts.Regular, 120);
        var moved = Lit(three, Everything, Bluish).Left - Lit(two, Everything, Bluish).Left;
        Assert.InRange(moved, big.MeasureText("152") - big.MeasureText("52") - 1, big.MeasureText("152") - big.MeasureText("52") + 1);
    }

    [Fact]
    public void Three_colours_run_left_to_right()
    {
        using var frame = Render(Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [1200, 160]
                style: { size: 80, weight: bold }
                spans:
                  - { text: "Red ", style: { color: "#FF0000" } }
                  - { text: "Green ", style: { color: "#00FF00" } }
                  - { text: "Blue", style: { color: "#0000FF" } }
            """);

        var red = Lit(frame, Everything, Reddish);
        var green = Lit(frame, Everything, Greenish);
        var blue = Lit(frame, Everything, Bluish);
        Assert.True(red.Right < green.Left && green.Right < blue.Left, $"Expected red {red}, then green {green}, then blue {blue}");
        Assert.InRange(green.Bottom - red.Bottom, -1, 1);
        Assert.InRange(blue.Bottom - red.Bottom, -1, 1);
    }

    [Fact]
    public void Wrapping_breaks_at_spaces_across_spans()
    {
        // "CPU " and "64°C" fit on the first line; " running hot" breaks onto the second at its space.
        using var frame = Render(Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [300, 300]
                style: { size: 48, wrap: true }
                spans:
                  - { text: "CPU ", style: { color: "#FF0000" } }
                  - { text: "64°C", style: { color: "#0000FF" } }
                  - { text: " running hot", style: { color: "#00FF00" } }
            """);

        var red = Lit(frame, Everything, Reddish);
        var blue = Lit(frame, Everything, Bluish);
        var green = Lit(frame, Everything, Greenish);
        Assert.InRange(blue.Bottom - red.Bottom, -2, 2);
        Assert.True(green.Top > red.Bottom, $"The green span {green} should wrap below the first line {red}");
        Assert.InRange(green.Left, 40, 46);
        Assert.True(green.Right <= 340, $"The wrapped text reaches x={green.Right}, past its 300 px box");
    }

    [Fact]
    public void A_line_is_as_tall_as_its_largest_span()
    {
        // Two lines: a small one, then a large one. The second line's baseline is further down than two small lines'.
        const string Widget = "  - { type: text, at: [40, 0], size: [800, 480], style: { size: 30, color: \"#FF0000\" }, ";
        using var mixed = Render(Landscape + "widgets:\n" + Widget + """
            spans: [{ text: "small\n" }, { text: "BIG", style: { size: 90, color: "#0000FF" } }] }
            """ + "\n");
        using var small = Render(Landscape + "widgets:\n" + Widget + """
            spans: [{ text: "small\n" }, { text: "BIG", style: { color: "#0000FF" } }] }
            """ + "\n");

        var smallGap = Lit(small, Everything, Bluish).Bottom - Lit(small, Everything, Reddish).Bottom;
        var mixedGap = Lit(mixed, Everything, Bluish).Bottom - Lit(mixed, Everything, Reddish).Bottom;
        // Baselines are half of each line's height apart and half of the next one's, plus the difference in their
        // letters' centring: 1.2 × (30 + 90) / 2 = 72 against 36 for two small lines.
        Assert.True(mixedGap > smallGap + 25, $"Expected the big line further down: {mixedGap} against {smallGap}");
        Assert.True(Lit(mixed, Everything, Bluish).Top > Lit(mixed, Everything, Reddish).Bottom, "The big line overlaps the small one");
    }

    [Fact]
    public void A_rule_recolours_every_span()
    {
        static string Layout(int above) => Landscape + $$"""
            widgets:
              - type: text
                at: [40, 40]
                size: [800, 160]
                source: cpu.temp
                style: { size: 80, weight: bold }
                spans:
                  - { text: "{cpu.temp:0}", style: { color: "#00FF00" } }
                  - { text: "°C", style: { size: 40, color: "#0000FF" } }
                when:
                  - { above: {{above}}, style: { color: "#FF0000" } }
            """;

        using var cool = Render(Layout(95), ("cpu.temp", 92));
        using var hot = Render(Layout(80), ("cpu.temp", 92));

        Assert.True(Pixels.Count(cool, Everything, Greenish) > 500);
        Assert.True(Pixels.Count(cool, Everything, Bluish) > 100);
        Assert.Equal(0, Pixels.Count(hot, Everything, Greenish));
        Assert.Equal(0, Pixels.Count(hot, Everything, Bluish));
        Assert.True(Pixels.Count(hot, Everything, Reddish) > 600);
        // The rule changes colour, not size: the unit keeps its own.
        static bool Lit150(SKColor c) => Math.Max(c.Red, Math.Max(c.Green, c.Blue)) > 150;
        Assert.Equal(Lit(cool, Everything, Lit150), Lit(hot, Everything, Lit150));
    }

    /// Each placeholder that has no value shows `—` on its own, and the widget's stale rule matches.
    [Fact]
    public void A_missing_value_shows_a_dash_and_matches_the_stale_rule()
    {
        using var spans = Render(Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [800, 160]
                style: { size: 60 }
                spans: [{ text: "{cpu.temp:0}" }, { text: " / " }, { text: "{gpu.temp:0}" }]
                when:
                  - { stale: true, style: { color: "#FF0000" } }
            """, ("gpu.temp", 61));
        using var text = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [800, 160], text: "— / 61", style: { size: 60, color: "#FF0000" } }
            """);

        AssertSamePixels(text, spans);
    }

    /// In a value widget's spans `{0}` is its value: they draw as a text widget's spans with the sensor key in its place,
    /// and their other placeholders still read keys.
    [Fact]
    public void A_value_widgets_spans_draw_its_value()
    {
        using var value = Render(Landscape + """
            widgets:
              - type: value
                at: [40, 40]
                size: [800, 200]
                source: cpu.temp
                style: { size: 100 }
                spans: [{ text: "{0:0}" }, { text: "°C", style: { size: 40 } }, { text: " {gpu.temp:0}", style: { size: 40 } }]
            """, ("cpu.temp", 52.4), ("gpu.temp", 61));
        using var text = Render(Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [800, 200]
                style: { size: 100 }
                spans: [{ text: "52" }, { text: "°C", style: { size: 40 } }, { text: " 61", style: { size: 40 } }]
            """);

        AssertSamePixels(text, value);
    }

    /// `{0}` is the value after `smooth`, as a value widget's format shows it.
    [Fact]
    public void A_value_widgets_spans_show_its_smoothed_value()
    {
        using var folder = new TempTheme(Landscape + """
            widgets:
              - type: value
                at: [40, 40]
                size: [600, 200]
                source: cpu.temp
                smooth: 1000
                style: { size: 100 }
                spans: [{ text: "{0:0}" }, { text: "°C", style: { size: 40 } }]
            """);
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme);

        using (renderer.Render(TestSensors.Of(("cpu.temp", 0)), TimeSpan.Zero)) { }
        using (renderer.Render(TestSensors.Of(("cpu.temp", 100)), TimeSpan.FromSeconds(1))) { }
        using var easing = renderer.Render(TestSensors.Of(("cpu.temp", 100)), TimeSpan.FromSeconds(1.6));
        using var text = Render(Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [600, 200]
                style: { size: 100 }
                spans: [{ text: "94" }, { text: "°C", style: { size: 40 } }]
            """);

        // Cubic ease-out is 93.6% of the way at 60% of the duration.
        AssertSamePixels(text, easing);
    }

    /// A value widget with no value shows one dash, in its first span's style, rather than a dash beside the unit.
    [Fact]
    public void A_value_widgets_missing_value_is_one_dash_in_the_first_spans_style()
    {
        using var value = Render(Landscape + """
            widgets:
              - type: value
                at: [40, 40]
                size: [600, 200]
                source: cpu.temp
                style: { size: 100, color: "#FF0000" }
                spans: [{ text: "{0:0}", style: { color: "#0000FF" } }, { text: "°C", style: { size: 40 } }]
            """);
        using var text = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 200], text: "—", style: { size: 100, color: "#0000FF" } }
            """);

        AssertSamePixels(text, value);
    }

    [Fact]
    public void A_value_widgets_span_font_file_is_loaded_with_the_theme()
    {
        using var folder = new TempTheme(Landscape + """
            widgets:
              - { type: value, at: [0, 0], size: [10, 10], source: cpu.temp, spans: [{ text: "{0}", style: { font: fonts/Mine.ttf } }] }
            """);
        folder.AddFile("fonts/Mine.ttf", BundledFonts.ReadBytes("Inter-Bold.ttf"));

        using var theme = folder.Load();

        Assert.NotSame(BundledFonts.Regular, theme.Assets.Typeface("fonts/Mine.ttf", 400, italic: false));
        Assert.Empty(theme.Warnings);
    }

    [Fact]
    public void An_outline_and_italic_stay_with_their_span()
    {
        using var frame = Render(Landscape + """
            widgets:
              - type: text
                at: [40, 40]
                size: [1000, 160]
                style: { size: 80, weight: bold, color: "#FF0000" }
                spans:
                  - { text: "Plain " }
                  - { text: "Outlined", style: { italic: true, text_stroke: { width: 4, color: "#0000FF" } } }
            """);

        var plain = Lit(frame, Everything, Reddish);
        var outline = Lit(frame, Everything, Bluish);
        using var font = new SKFont(BundledFonts.Bold, 80);
        var plainEnd = 40 + font.MeasureText("Plain ");
        Assert.True(outline.Left > plainEnd - 8, $"The outline {outline} reaches into the first span, which ends at {plainEnd}");
        Assert.True(outline.Right > plain.Right, "The outlined span's letters should be drawn inside its outline");
    }

    [Theory]
    [InlineData("right")]
    [InlineData("center")]
    public void Align_places_the_whole_line(string align)
    {
        using var frame = Render(Landscape + $$"""
            widgets:
              - type: text
                at: [40, 40]
                size: [1000, 160]
                style: { size: 80, align: {{align}}, padding: 20 }
                spans:
                  - { text: "88", style: { color: "#FF0000" } }
                  - { text: "°C", style: { size: 40, color: "#0000FF" } }
            """);

        var red = Lit(frame, Everything, Reddish);
        var blue = Lit(frame, Everything, Bluish);
        using var big = new SKFont(BundledFonts.Regular, 80);
        using var small = new SKFont(BundledFonts.Regular, 40);
        var width = big.MeasureText("88") + small.MeasureText("°C");
        var left = align == "right" ? 1020 - width : 540 - width / 2;
        Assert.InRange(red.Left, left, left + 10);
        Assert.InRange(blue.Left - red.Right, 0, 12);
        if (align == "right") Assert.InRange(blue.Right, 1020 - 8, 1020);
    }

    [Fact]
    public void A_spans_font_file_is_loaded_with_the_theme()
    {
        using var folder = new TempTheme(Landscape + """
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], spans: [{ text: a }, { text: b, style: { font: fonts/Mine.ttf } }] }
            """);
        folder.AddFile("fonts/Mine.ttf", BundledFonts.ReadBytes("Inter-Bold.ttf"));

        using var theme = folder.Load();

        Assert.NotSame(BundledFonts.Regular, theme.Assets.Typeface("fonts/Mine.ttf", 400, italic: false));
        Assert.Empty(theme.Warnings);
    }

    [Fact]
    public void Spans_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              panel: { fill: "#1B2440", radius: 12, padding: 12, color: "#F4F6FA" }
              unit: { size: 36, color: "#36D1DC" }
            background:
              - fill: "#0B0F1A"
            widgets:
              # A big number with a small unit, left, centred and right.
              - { type: text, at: [20, 20], size: [440, 140], source: cpu.temp, style: { extends: panel, size: 96, weight: bold }, spans: [{ text: "{cpu.temp:0}" }, { text: "°C", style: unit }] }
              - { type: text, at: [20, 170], size: [440, 140], source: gpu.clock, style: { extends: panel, size: 96, weight: bold, align: center }, spans: [{ text: "{gpu.clock:0.0}" }, { text: " GHz", style: unit }] }
              - { type: text, at: [20, 320], size: [440, 140], source: cpu.load, style: { extends: panel, size: 96, weight: bold, align: right }, spans: [{ text: "{cpu.load:0}" }, { text: "%", style: unit }] }
              # Three colours, and letter effects on one span.
              - type: text
                at: [480, 20]
                size: [700, 140]
                style: { extends: panel, size: 64, weight: bold, align: center }
                spans:
                  - { text: "Red ", style: { color: "#FF5A36" } }
                  - { text: "Green ", style: { color: "#3DDC84" } }
                  - { text: "Blue", style: { color: "#5B86E5" } }
              - type: text
                at: [480, 170]
                size: [700, 140]
                style: { extends: panel, size: 52, align: center }
                spans:
                  - { text: "Plain " }
                  - { text: "outlined", style: { italic: true, weight: bold, color: "#1B2440", text_stroke: { width: 3, color: "#FFE259" } } }
                  - { text: " and ", style: { size: 32 } }
                  - { text: "gradient", style: { underline: true, text_fill: { linear: ["#36D1DC", "#A770EF"] } } }
              # A rule recolours every span.
              - type: text
                at: [480, 320]
                size: [700, 140]
                source: gpu.temp
                style: { extends: panel, size: 80, weight: bold, align: center }
                spans: [{ text: "GPU ", style: { size: 40, color: "#8892A6" } }, { text: "{gpu.temp:0}" }, { text: "°C", style: unit }]
                when:
                  - { above: 80, style: { color: "#FF5A36", glow: { color: "#FF5A36", radius: 12 } } }
              # Wrapped across spans, and a line break inside a span.
              - type: text
                at: [1200, 20]
                size: [700, 220]
                style: { extends: panel, size: 44, wrap: true, align: center }
                spans:
                  - { text: "Spans wrap at spaces ", style: { color: "#F4F6FA" } }
                  - { text: "across their boundaries", style: { color: "#FFE259", weight: bold } }
                  - { text: " and keep their own styles", style: { color: "#36D1DC", italic: true } }
              - type: text
                at: [1200, 250]
                size: [700, 210]
                style: { extends: panel, size: 32, align: right }
                spans:
                  - { text: "Fan\n", style: { color: "#8892A6", case: upper, letter_spacing: 4 } }
                  - { text: "{gpu.fan:0}", style: { size: 96, weight: bold } }
                  - { text: " rpm", style: unit }
            """, ("cpu.temp", 52), ("gpu.clock", 2.6), ("cpu.load", 7), ("gpu.temp", 86), ("gpu.fan", 1450));

        Snapshots.Match(frame, "spans");
    }

    [Fact]
    public void Value_spans_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              panel: { fill: "#1B2440", radius: 12, padding: 12, color: "#F4F6FA", size: 96, weight: bold, align: center }
              unit: { size: 36, weight: 400, color: "#36D1DC" }
            background:
              - fill: "#0B0F1A"
            widgets:
              # A value with a small unit, a value with a missing reading beside it, and a missing value: one dash.
              - { type: value, at: [20, 20], size: [600, 200], source: cpu.temp, style: panel, spans: [{ text: "{0:0}" }, { text: "°C", style: unit }] }
              - { type: value, at: [660, 20], size: [600, 200], source: gpu.clock, style: panel, spans: [{ text: "{0:0.0}" }, { text: " GHz", style: unit }, { text: " {gpu.fan:0} rpm", style: unit }] }
              - { type: value, at: [1300, 20], size: [600, 200], source: ram.used_pct, style: panel, spans: [{ text: "{0:0}", style: { color: "#8892A6" } }, { text: "%", style: unit }] }
              # A rule recolours every span.
              - type: value
                at: [20, 260]
                size: [600, 200]
                source: gpu.temp
                style: panel
                spans: [{ text: "GPU ", style: unit }, { text: "{0:0}" }, { text: "°C", style: unit }]
                when:
                  - { above: 80, style: { color: "#FF5A36", glow: { color: "#FF5A36", radius: 12 } } }
            """, ("cpu.temp", 52), ("gpu.clock", 2.6), ("gpu.temp", 86));

        Snapshots.Match(frame, "value-spans");
    }
}
