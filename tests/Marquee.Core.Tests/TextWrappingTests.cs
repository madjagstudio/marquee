using Marquee.Core.Tests.TestSupport;
using SkiaSharp;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// Text and value widgets start a new line at `\n`, wrap at spaces inside their box when their style has `wrap: true`,
/// and take `letter_spacing` and `line_height` from their style. Without `wrap`, a line too wide for its box draws on
/// one line, overflowing it, exactly as it always has.
public class TextWrappingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static SKImage Render(string layout, params (string, object)[] values) => Frames.Render(layout, TestSensors.Of(values));

    /// The smallest rectangle holding every bright (text) pixel in the region; empty when there are none.
    static SKRectI Lit(SKImage image, SKRectI region)
    {
        using var bitmap = SKBitmap.FromImage(image);
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        for (var y = region.Top; y < region.Bottom; y++)
            for (var x = region.Left; x < region.Right; x++)
                if (Pixels.IsBright(bitmap.GetPixel(x, y)))
                    (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x + 1), Math.Max(bottom, y + 1));
        return left == int.MaxValue ? SKRectI.Empty : new SKRectI(left, top, right, bottom);
    }

    static readonly SKRectI Everything = new(0, 0, 1920, 480);

    static void AssertSamePixels(SKImage expected, SKImage actual)
    {
        using var a = SKBitmap.FromImage(expected);
        using var b = SKBitmap.FromImage(actual);
        Assert.True(a.Pixels.SequenceEqual(b.Pixels), "The two frames differ");
    }

    [Fact]
    public void Default_spacing_written_out_draws_exactly_as_leaving_it_out()
    {
        const string Widget = "  - { type: text, at: [40, 40], size: [600, 100], text: \"CPU 64°C\", style: { size: 48, align: center";
        using var plain = Render(Landscape + "widgets:\n" + Widget + " } }\n");
        using var written = Render(Landscape + "widgets:\n" + Widget + ", letter_spacing: 0, line_height: 1.2 } }\n");

        AssertSamePixels(plain, written);
    }

    [Fact]
    public void Text_too_wide_for_its_box_wraps_onto_more_lines()
    {
        using var wide = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [1900, 400], text: \"one two three\", style: { size: 40, wrap: true } }\n");
        using var narrow = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [150, 400], text: \"one two three\", style: { size: 40, wrap: true } }\n");

        var oneLine = Lit(wide, Everything);
        var wrapped = Lit(narrow, Everything);

        Assert.True(wrapped.Height > 2.5 * oneLine.Height, $"Expected three lines: {wrapped} against one line's {oneLine}");
        Assert.True(wrapped.Right <= 150, $"Wrapped text reaches x={wrapped.Right}, past its 150 px box");
    }

    [Fact]
    public void Without_wrap_text_too_wide_for_its_box_draws_on_one_line_as_it_always_has()
    {
        using var wide = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [1900, 400], text: \"one two three\", style: { size: 40 } }\n");
        using var narrow = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [150, 400], text: \"one two three\", style: { size: 40 } }\n");

        AssertSamePixels(wide, narrow);
    }

    /// A value whose number grows wider than its box keeps its unit: it overflows on one line, as it always has, rather
    /// than wrapping the unit onto a line the box has no room for.
    [Fact]
    public void Without_wrap_an_overflowing_value_draws_on_one_line_as_it_always_has()
    {
        static SKImage Value(int width) => Render(Landscape + "widgets:\n  - { type: value, at: [20, 20], size: [" + width + ", 40], "
            + "source: cpu.temp, format: \"{0:0.0} °C\", style: { size: 30 } }\n", ("cpu.temp", 100.0));

        using var roomy = Value(400);
        using var tight = Value(115);

        AssertSamePixels(roomy, tight);
        Assert.True(Lit(tight, Everything).Right > 135, "Expected the unit to overflow the 115 px box");
    }

    [Fact]
    public void A_line_break_in_the_text_starts_a_new_line()
    {
        using var frame = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [1900, 400], text: \"one\\ntwo\", style: { size: 40 } }\n");
        using var single = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [1900, 400], text: \"one\", style: { size: 40 } }\n");

        Assert.True(Lit(frame, Everything).Height > 1.8 * Lit(single, Everything).Height);
    }

    [Fact]
    public void Line_height_spaces_the_lines()
    {
        static SKRectI Block(string lineHeight)
        {
            using var frame = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [1900, 480], text: \"one\\ntwo\\nthree\", "
                + $"style: {{ size: 40, line_height: {lineHeight} }} }}\n");
            return Lit(frame, Everything);
        }

        var single = Block("1");
        var twice = Block("2");

        // Three lines are two line steps tall plus one line: doubling the step adds two more steps of 40 px, half above
        // and half below, so the block stays centred where it was.
        Assert.InRange(twice.Height - single.Height, 78, 82);
        Assert.InRange(twice.MidY - single.MidY, -2, 2);
    }

    [Fact]
    public void Letter_spacing_widens_or_tightens_a_line()
    {
        static int Width(string spacing)
        {
            using var frame = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [1900, 480], text: \"ABCDEFGH\", "
                + $"style: {{ size: 40, letter_spacing: {spacing}, align: right }} }}\n");
            var lit = Lit(frame, Everything);
            Assert.InRange(lit.Right, 1880, 1900);
            return lit.Width;
        }

        var normal = Width("0");

        // Eight letters have seven gaps between them.
        Assert.InRange(Width("10") - normal, 68, 72);
        Assert.InRange(normal - Width("-3"), 19, 23);
    }

    [Fact]
    public void Letter_spacing_counts_when_wrapping()
    {
        using var plain = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [300, 480], text: \"one two three\", style: { size: 40, wrap: true } }\n");
        using var spaced = Render(Landscape + "widgets:\n  - { type: text, at: [0, 0], size: [300, 480], text: \"one two three\", style: { size: 40, letter_spacing: 12, wrap: true } }\n");

        Assert.True(Lit(spaced, Everything).Height > 1.5 * Lit(plain, Everything).Height);
        Assert.True(Lit(spaced, Everything).Right <= 300);
    }

    [Fact]
    public void Lines_that_do_not_fit_the_box_are_left_out_whole()
    {
        // The box holds two lines of 40 px text at 1.2 (48 px apart), not three.
        using var frame = Render(Landscape + "widgets:\n  - { type: text, at: [0, 100], size: [600, 100], text: \"one\\ntwo\\nthree\", style: { size: 40 } }\n");

        var lit = Lit(frame, Everything);

        Assert.True(lit.Top >= 100 && lit.Bottom <= 200, $"Text at {lit} spills out of its box (y 100 to 200)");
        Assert.InRange(lit.Height, 60, 90);
    }

    [Fact]
    public void A_value_whose_text_wraps_wraps_like_text()
    {
        static SKRectI Value(int width)
        {
            using var frame = Render(Landscape + "widgets:\n  - { type: value, at: [0, 0], size: [" + width + ", 480], source: ram.used, "
                + "format: \"{0:0.0} GB of 32 GB\", style: { size: 40, wrap: true } }\n", ("ram.used", 9.25));
            return Lit(frame, Everything);
        }

        var oneLine = Value(1900);
        var wrapped = Value(260);

        Assert.True(wrapped.Height > 1.8 * oneLine.Height, $"Expected two lines: {wrapped} against one line's {oneLine}");
        Assert.True(wrapped.Right <= 260);
    }

    [Fact]
    public void Wrapping_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              box: { size: 30, fill: "#1B2440", radius: 8, padding: 12 }
              wrapping: { extends: box, wrap: true }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: text, at: [20, 20], size: [220, 200], text: "Wraps at the spaces between words", style: wrapping }
              - { type: text, at: [260, 20], size: [220, 200], text: "First line\nSecond line", style: box }
              - type: text
                at: [500, 20]
                size: [220, 200]
                text: |
                  A block scalar
                  keeps its lines
                style: box
              - { type: text, at: [740, 20], size: [300, 200], text: "Left aligned text on several lines", style: { extends: wrapping, align: left } }
              - { type: text, at: [1060, 20], size: [300, 200], text: "Centred text on several lines", style: { extends: wrapping, align: center } }
              - { type: text, at: [1380, 20], size: [300, 200], text: "Right aligned text on several lines", style: { extends: wrapping, align: right } }
              - { type: text, at: [20, 240], size: [220, 110], text: "Only the lines that fit are shown here", style: wrapping }
              - { type: text, at: [260, 240], size: [220, 220], text: "Overlongwordsstay whole", style: wrapping }
              - type: text
                at: [500, 240]
                size: [300, 220]
                text: "Glow and shadow on every line"
                style: { extends: wrapping, size: 40, glow: { color: "#FF5A36", radius: 14 }, shadow: { color: "#000000", x: 3, y: 3, blur: 2 } }
              - type: value
                at: [820, 240]
                size: [300, 220]
                source: gpu.vram_used
                format: "{0:0.0} GB of video memory"
                style: { extends: wrapping, size: 40, align: center }
              - { type: text, at: [1140, 240], size: [220, 70], text: "Without wrap, one line overflows", style: box }
            """, ("gpu.vram_used", 9.2));

        Snapshots.Match(frame, "text-wrapping");
    }

    [Fact]
    public void Spacing_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              box: { size: 36, fill: "#1B2440", radius: 8, padding: 12 }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: text, at: [20, 20], size: [440, 70], text: "Letter spacing 0", style: box }
              - { type: text, at: [20, 100], size: [440, 70], text: "Letter spacing 6", style: { extends: box, letter_spacing: 6 } }
              - { type: text, at: [20, 180], size: [440, 70], text: "Letter spacing -3", style: { extends: box, letter_spacing: -3 } }
              - { type: text, at: [20, 260], size: [440, 70], text: "SPACED RIGHT", style: { extends: box, letter_spacing: 12, align: right } }
              - { type: text, at: [20, 340], size: [440, 70], text: "CENTRED", style: { extends: box, letter_spacing: 12, align: center } }
              - { type: text, at: [480, 20], size: [400, 440], text: "Line height 1\nkeeps lines\nclose together", style: { extends: box, line_height: 1 } }
              - { type: text, at: [900, 20], size: [400, 440], text: "Line height 2\nspreads lines\nfar apart", style: { extends: box, line_height: 2 } }
              - type: text
                at: [1320, 20]
                size: [580, 440]
                text: "Spaced and tall text wraps with both"
                style: { extends: box, wrap: true, size: 48, letter_spacing: 4, line_height: 1.6, align: center, glow: { color: "#36A2FF", radius: 12 } }
            """);

        Snapshots.Match(frame, "text-spacing");
    }

    [Fact]
    public void Spacing_out_of_range_is_reported_with_its_line_and_the_rest_still_checked()
    {
        using var folder = new TempTheme(Landscape + """
            styles:
              wide: { letter_spacing: 120 }
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { line_height: 0.2 } }
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { line_height: 5 } }
            """);

        Assert.Equal(
            [
                "layout.yaml:3: error: letter_spacing must be between -20 and 100",
                "layout.yaml:5: error: line_height must be between 0.5 and 4",
                "layout.yaml:6: error: line_height must be between 0.5 and 4",
            ],
            Lines(Validate(folder.Folder)));
    }
}
