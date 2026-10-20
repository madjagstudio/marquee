using Marquee.Core.Expressions;
using Marquee.Core.Rendering;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Letter effects drawn: an outline round the letters, a colour or gradient for the letters alone, italic, underline,
/// strikethrough and letter case, on text and value widgets and on a dial's and a donut's labels.
public class LetterEffectsRenderingTests
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    static SKImage Render(string layout, params (string, object)[] values) => Frames.Render(layout, TestSensors.Of(values));

    /// With the renderer's zone and culture pinned, so `case` follows that culture's rules and not the machine's.
    static SKImage Render(string layout, TimeLocale locale, params (string, object)[] values)
    {
        using var folder = new TempTheme(layout);
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme) { Locale = locale };
        using var frame = renderer.Render(TestSensors.Of(values), TimeSpan.Zero);
        return Frames.ThroughSink(frame);
    }

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

    static bool Bluish(SKColor c) => c.Blue > 150 && c.Red < 100 && c.Green < 100;

    static bool Greenish(SKColor c) => c.Green > 150 && c.Red < 100 && c.Blue < 100;

    [Fact]
    public void The_defaults_written_out_draw_exactly_as_leaving_them_out()
    {
        const string Widget = "  - { type: text, at: [40, 40], size: [600, 100], text: \"Cpu 64°C\", style: { size: 48, color: \"#36D1DC\"";

        using var plain = Render(Landscape + "widgets:\n" + Widget + " } }\n");
        using var written = Render(Landscape + "widgets:\n" + Widget
            + ", italic: false, underline: false, strike: false, case: none, text_stroke: { width: 3 }, text_fill: \"#36D1DC\" } }\n");

        AssertSamePixels(plain, written);
    }

    [Fact]
    public void Text_fill_colours_the_letters_instead_of_color()
    {
        using var frame = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 120], text: "HELLO", style: { size: 96, weight: bold, color: "#FF0000", text_fill: "#00FF00" } }
            """);

        var region = new SKRectI(40, 40, 640, 160);
        Assert.Equal(0, Pixels.Count(frame, region, Reddish));
        Assert.True(Pixels.Count(frame, region, Greenish) > 1000);
    }

    [Fact]
    public void A_text_fill_gradient_spans_the_widgets_box()
    {
        // Red at the box's left edge to blue at its right: the first letter is reddish and the last bluish.
        using var frame = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [800, 120], text: "IIIIIIIIIIIIII", style: { size: 96, weight: bold, align: center, text_fill: { linear: ["#FF0000", "#0000FF"] } } }
            """);

        var lit = Lit(frame, new SKRectI(40, 40, 840, 160), c => c.Red > 60 || c.Blue > 60);
        var first = new SKRectI(lit.Left, lit.Top, lit.Left + 20, lit.Bottom);
        var last = new SKRectI(lit.Right - 20, lit.Top, lit.Right, lit.Bottom);
        Assert.True(Pixels.Count(frame, first, c => c.Red > c.Blue + 40) > 50, "The first letter isn't reddish");
        Assert.True(Pixels.Count(frame, last, c => c.Blue > c.Red + 40) > 50, "The last letter isn't bluish");
        Assert.True(lit.Left > 200, "The text should be centred, well inside the gradient's box");
    }

    [Fact]
    public void An_outline_shows_its_width_outside_the_letters()
    {
        const string Text = "  - { type: text, at: [40, 40], size: [600, 160], text: \"HOT\", style: { size: 96, weight: bold, color: \"#FF0000\"";
        var region = new SKRectI(0, 0, 700, 240);

        using var plain = Render(Landscape + "widgets:\n" + Text + " } }\n");
        using var outlined = Render(Landscape + "widgets:\n" + Text + ", text_stroke: { width: 6, color: \"#0000FF\" } } }\n");

        var letters = Lit(plain, region, Reddish);
        var outline = Lit(outlined, region, Bluish);
        Assert.InRange(letters.Left - outline.Left, 5, 7);
        Assert.InRange(outline.Right - letters.Right, 5, 7);
        Assert.InRange(letters.Top - outline.Top, 5, 7);
        Assert.InRange(outline.Bottom - letters.Bottom, 5, 7);

        // The fill is drawn over the outline, so the letters keep their shape and colour: only their antialiased edges,
        // which now blend with blue rather than black, can read differently.
        var kept = Lit(outlined, region, Reddish);
        Assert.InRange(kept.Left - letters.Left, 0, 1);
        Assert.InRange(letters.Right - kept.Right, 0, 1);
        Assert.InRange(kept.Top - letters.Top, 0, 1);
        Assert.InRange(letters.Bottom - kept.Bottom, 0, 1);
    }

    [Fact]
    public void An_outline_needs_a_colour_and_a_width()
    {
        const string Text = "  - { type: text, at: [40, 40], size: [600, 160], text: \"HOT\", style: { size: 96, weight: bold";

        using var plain = Render(Landscape + "widgets:\n" + Text + " } }\n");
        using var noColour = Render(Landscape + "widgets:\n" + Text + ", text_stroke: { width: 6 } } }\n");
        using var noWidth = Render(Landscape + "widgets:\n" + Text + ", text_stroke: { width: 0, color: \"#0000FF\" } } }\n");

        AssertSamePixels(plain, noColour);
        AssertSamePixels(plain, noWidth);
    }

    [Fact]
    public void Underline_runs_the_width_of_the_text_letter_spacing_included()
    {
        var (line, baseline, width) = Added("underline", "Wide", spacing: 30);

        Assert.InRange(line.Left, 39, 41);
        Assert.InRange(line.Right, 40 + width - 1, 40 + width + 1);
        Assert.True(line.Top > baseline, $"The underline {line} should be below the baseline at {baseline}");
        Assert.True(line.Height < 8, $"Expected a thin line, got {line}");
    }

    [Fact]
    public void Strike_crosses_the_letters_above_the_baseline()
    {
        var (line, baseline, width) = Added("strike", "Strike");
        using var font = new SKFont(BundledFonts.Regular, 60);

        Assert.InRange(line.Left, 39, 41);
        Assert.InRange(line.Right, 40 + width - 1, 40 + width + 1);
        // Through the middle of the lower-case letters: above the baseline, below the top of an x.
        Assert.True(line.Bottom < baseline, $"The strike {line} should be above the baseline at {baseline}");
        Assert.True(line.Top > baseline - font.Metrics.XHeight, $"The strike {line} should be below the x-height");
        Assert.True(line.Height < 8, $"Expected a thin line, got {line}");
    }

    /// The pixels a line adds to one line of Inter, 60 px, at the left of a box at (40, 40): what changes when `key` is
    /// turned on, the baseline, and the text's width.
    static (SKRectI Line, float Baseline, float Width) Added(string key, string text, float spacing = 0)
    {
        var widget = $"  - {{ type: text, at: [40, 40], size: [1000, 120], text: \"{text}\", style: {{ size: 60, letter_spacing: {spacing}";
        using var plain = Render(Landscape + "widgets:\n" + widget + " } }\n");
        using var lined = Render(Landscape + "widgets:\n" + widget + $", {key}: true }} }}\n");

        using var a = SKBitmap.FromImage(plain);
        using var b = SKBitmap.FromImage(lined);
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
                if (!Pixels.Near(a.GetPixel(x, y), b.GetPixel(x, y), 60))
                    (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x + 1), Math.Max(bottom, y + 1));

        using var font = new SKFont(BundledFonts.Regular, 60);
        var baseline = 100 - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
        return (new SKRectI(left, top, right, bottom), baseline, TextPainter.Width(font, text, spacing, TestClock.Locale().Culture));
    }

    [Fact]
    public void Case_applies_after_the_template_is_filled_in()
    {
        using var upper = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 100], text: "{gpu.name}", style: { size: 48, case: upper } }
            """, ("gpu.name", "rtx 4090"));
        using var written = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 100], text: "RTX 4090", style: { size: 48 } }
            """);

        AssertSamePixels(written, upper);
    }

    [Fact]
    public void Case_applies_to_a_value_widgets_text()
    {
        using var lower = Render(Landscape + """
            widgets:
              - { type: value, at: [40, 40], size: [600, 100], source: cpu.temp, format: "{0:0} DEGREES", style: { size: 48, case: lower } }
            """, ("cpu.temp", 64));
        using var written = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 100], text: "64 degrees", style: { size: 48 } }
            """);

        AssertSamePixels(written, lower);
    }

    [Fact]
    public void Case_follows_the_renderers_culture()
    {
        // The machine's own culture is left as it is; only the renderer's is Turkish, whose capital i is İ.
        using var upper = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 100], text: "istanbul", style: { size: 48, case: upper } }
            """, TestClock.Locale("tr-TR"));
        using var written = Render(Landscape + """
            widgets:
              - { type: text, at: [40, 40], size: [600, 100], text: "İSTANBUL", style: { size: 48 } }
            """);

        AssertSamePixels(written, upper);
    }

    [Fact]
    public void Italic_slants_inter_which_has_no_italic_face()
    {
        // The bundled Inter is two upright faces, Regular and Bold, so italic Inter is slanted by the renderer.
        Assert.Equal(SKFontStyleSlant.Upright, BundledFonts.Regular.FontStyle.Slant);
        Assert.Equal(SKFontStyleSlant.Upright, BundledFonts.Bold.FontStyle.Slant);

        using var theme = new TempTheme(Landscape + "widgets: []\n");
        using var loaded = theme.Load();
        using var regular = Font(loaded, new StyleSpec { Italic = true });
        using var bold = Font(loaded, new StyleSpec { Italic = true, Weight = 700 });
        using var upright = Font(loaded, new StyleSpec { Italic = false });

        Assert.Equal(BundledFonts.Regular.Handle, regular.Typeface.Handle);
        Assert.Equal(-0.25f, regular.SkewX);
        Assert.Equal(BundledFonts.Bold.Handle, bold.Typeface.Handle);
        Assert.Equal(-0.25f, bold.SkewX);
        Assert.Equal(0f, upright.SkewX);
    }

    [Fact]
    public void Italic_uses_an_installed_familys_italic_face_without_slanting_it()
    {
        // Arial comes with Windows, with an italic face (ariali.ttf) of its own.
        using var theme = new TempTheme(Landscape + "widgets: []\n");
        using var loaded = theme.Load();
        using var italic = Font(loaded, new StyleSpec { Font = "Arial", Italic = true });
        using var upright = Font(loaded, new StyleSpec { Font = "Arial" });

        Assert.Equal("Arial", italic.Typeface.FamilyName);
        Assert.Equal(SKFontStyleSlant.Italic, italic.Typeface.FontStyle.Slant);
        Assert.Equal(0f, italic.SkewX);
        Assert.Equal(SKFontStyleSlant.Upright, upright.Typeface.FontStyle.Slant);
        Assert.Equal(0f, upright.SkewX);
    }

    [Fact]
    public void Italic_slants_a_font_file_that_is_upright()
    {
        using var theme = new TempTheme(Landscape + """
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: fonts/Inter.ttf, italic: true } }
            """);
        theme.AddFile("fonts/Inter.ttf", BundledFonts.ReadBytes("Inter-Regular.ttf"));
        using var loaded = theme.Load();
        using var italic = Font(loaded, new StyleSpec { Font = "fonts/Inter.ttf", Italic = true });

        Assert.Equal(SKFontStyleSlant.Upright, italic.Typeface.FontStyle.Slant);
        Assert.Equal(-0.25f, italic.SkewX);
    }

    static SKFont Font(LoadedTheme theme, StyleSpec style)
    {
        using var surface = SKSurface.Create(new SKImageInfo(10, 10));
        var ctx = new RenderContext(surface.Canvas, TestSensors.Of(), TimeSpan.Zero, theme.Assets, [], []);
        return TextPainter.Font(ctx, style);
    }

    [Fact]
    public void Outline_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              title: { size: 72, weight: bold, color: "#F4F6FA" }
            background:
              - fill: "#30384A"
            widgets:
              - { type: text, at: [20, 20], size: [440, 100], text: "Outline 1", style: { extends: title, text_stroke: { color: "#000000" } } }
              - { type: text, at: [20, 130], size: [440, 100], text: "Outline 4", style: { extends: title, text_stroke: { width: 4, color: "#000000" } } }
              - { type: text, at: [20, 240], size: [440, 100], text: "Outline 8", style: { extends: title, text_stroke: { width: 8, color: "#FF5A36" } } }
              - type: text
                at: [480, 20]
                size: [700, 160]
                text: "Gradient"
                style: { extends: title, size: 120, text_stroke: { width: 6, color: { linear: ["#36D1DC", "#A770EF"] } } }
              - type: text
                at: [480, 200]
                size: [700, 160]
                text: "With a glow"
                style: { extends: title, size: 96, text_stroke: { width: 3, color: "#000000" }, glow: { color: "#36A2FF", radius: 16 } }
              - type: text
                at: [1200, 20]
                size: [700, 160]
                text: "And a shadow"
                style: { extends: title, size: 96, text_stroke: { width: 3, color: "#FFFFFF" }, color: "#1B2440", shadow: { color: "#000000", x: 6, y: 6, blur: 2 } }
              - type: text
                at: [1200, 200]
                size: [700, 260]
                text: "Wrapped text keeps its outline"
                style: { extends: title, size: 64, wrap: true, align: center, text_stroke: { width: 2, color: "#FF5A36" } }
            """);

        Snapshots.Match(frame, "letter-outline");
    }

    [Fact]
    public void Fill_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              title: { size: 96, weight: bold, color: "#FF0000" }
            background:
              - fill: "#0B0F1A"
            widgets:
              # A solid text_fill wins over color.
              - { type: text, at: [20, 20], size: [600, 120], text: "Solid fill", style: { extends: title, text_fill: "#36D1DC" } }
              # A gradient across the box, top to bottom, with a box of its own showing where it runs.
              - { type: text, at: [20, 160], size: [600, 140], text: "Top down", style: { extends: title, fill: "#1B2440", text_fill: { linear: ["#FFE259", "#FF5A36"], angle: 90 } } }
              # Two texts side by side with boxes that line up share one gradient.
              - { type: text, at: [20, 320], size: [300, 140], text: "Shared", style: { extends: title, size: 64, text_fill: { linear: ["#FFE259", "#5B86E5"], angle: 90 } } }
              - { type: text, at: [320, 320], size: [300, 140], text: "Ramp", style: { extends: title, size: 64, text_fill: { linear: ["#FFE259", "#5B86E5"], angle: 90 } } }
              # Left to right over a long line, and a radial gradient.
              - { type: text, at: [660, 20], size: [1240, 160], text: "Left to right across the box", style: { extends: title, align: center, text_fill: { linear: ["#36D1DC", "#A770EF", "#FF5A36"] } } }
              - { type: text, at: [660, 200], size: [600, 260], text: "Radial", style: { extends: title, size: 140, align: center, text_fill: { radial: ["#FFFFFF", "#5B86E5"] } } }
              # A gradient fill with an outline and a glow.
              - { type: text, at: [1280, 200], size: [620, 260], text: "Both", style: { extends: title, size: 140, align: center, text_fill: { linear: ["#FFE259", "#FF5A36"], angle: 90 }, text_stroke: { width: 4, color: "#000000" }, glow: { color: "#FF5A36", radius: 20 } } }
            """);

        Snapshots.Match(frame, "letter-fill");
    }

    [Fact]
    public void Italic_slanted_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              line: { size: 64, color: "#F4F6FA", fill: "#1B2440", padding: 12 }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: text, at: [20, 20], size: [900, 100], text: "Upright Inter", style: line }
              - { type: text, at: [20, 130], size: [900, 100], text: "Slanted Inter", style: { extends: line, italic: true } }
              - { type: text, at: [20, 240], size: [900, 100], text: "Slanted bold Inter", style: { extends: line, italic: true, weight: bold } }
              - { type: text, at: [20, 350], size: [900, 100], text: "Right aligned", style: { extends: line, italic: true, align: right } }
              - { type: value, at: [960, 20], size: [900, 100], source: cpu.temp, format: "{0:0}°C slanted", style: { extends: line, italic: true, letter_spacing: 4 } }
              - { type: text, at: [960, 130], size: [900, 320], text: "Slanted lines wrap and keep their slant", style: { extends: line, italic: true, wrap: true, align: center, underline: true } }
            """, ("cpu.temp", 64));

        Snapshots.Match(frame, "letter-italic-slanted");
    }

    [Fact]
    public void Italic_face_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              line: { font: Arial, size: 72, color: "#F4F6FA", fill: "#1B2440", padding: 12 }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: text, at: [20, 20], size: [900, 120], text: "Arial upright", style: line }
              - { type: text, at: [20, 160], size: [900, 120], text: "Arial Italic face", style: { extends: line, italic: true } }
              - { type: text, at: [20, 300], size: [900, 120], text: "Arial Bold Italic", style: { extends: line, italic: true, weight: bold } }
              - { type: text, at: [960, 160], size: [900, 120], text: "Inter slanted", style: { extends: line, font: Inter, italic: true } }
            """);

        Snapshots.Match(frame, "letter-italic-face");
    }

    [Fact]
    public void Lines_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              line: { size: 56, color: "#F4F6FA", fill: "#1B2440", padding: 12 }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: text, at: [20, 20], size: [600, 100], text: "Underlined", style: { extends: line, underline: true } }
              - { type: text, at: [20, 130], size: [600, 100], text: "Struck through", style: { extends: line, strike: true } }
              - { type: text, at: [20, 240], size: [600, 100], text: "Both lines", style: { extends: line, underline: true, strike: true } }
              - { type: text, at: [20, 350], size: [600, 100], text: "SPACED", style: { extends: line, underline: true, letter_spacing: 16, align: center } }
              - { type: text, at: [640, 20], size: [600, 100], text: "Gradient lines", style: { extends: line, underline: true, strike: true, text_fill: { linear: ["#36D1DC", "#FF5A36"] } } }
              - { type: text, at: [640, 130], size: [600, 100], text: "Outlined lines", style: { extends: line, underline: true, strike: true, text_stroke: { width: 3, color: "#FF5A36" } } }
              - { type: text, at: [640, 240], size: [600, 210], text: "Each wrapped line is underlined", style: { extends: line, underline: true, wrap: true, align: right } }
              - { type: text, at: [1260, 20], size: [640, 100], text: "Glowing line", style: { extends: line, underline: true, glow: { color: "#36A2FF", radius: 12 } } }
              - { type: text, at: [1260, 130], size: [640, 100], text: "Shadowed line", style: { extends: line, strike: true, shadow: { color: "#000000", x: 4, y: 4 } } }
              - { type: text, at: [1260, 240], size: [640, 100], text: "Bold and underlined", style: { extends: line, weight: bold, underline: true } }
              - { type: text, at: [1260, 350], size: [640, 100], text: "Large text", style: { extends: line, size: 72, underline: true, strike: true } }
            """);

        Snapshots.Match(frame, "letter-lines");
    }

    [Fact]
    public void Case_snapshot()
    {
        using var frame = Render(Landscape + """
            styles:
              line: { size: 56, color: "#F4F6FA", fill: "#1B2440", padding: 12 }
            background:
              - fill: "#0B0F1A"
            widgets:
              - { type: text, at: [20, 20], size: [900, 100], text: "{gpu.name} is at {gpu.temp:0}°c", style: { extends: line, case: none } }
              - { type: text, at: [20, 130], size: [900, 100], text: "{gpu.name} is at {gpu.temp:0}°c", style: { extends: line, case: upper } }
              - { type: text, at: [20, 240], size: [900, 100], text: "{gpu.name} is at {gpu.temp:0}°c", style: { extends: line, case: lower } }
              - { type: text, at: [20, 350], size: [900, 100], text: "{gpu.name} is at {gpu.temp:0}°c", style: { extends: line, case: title } }
              - { type: value, at: [960, 20], size: [900, 100], source: gpu.name, style: { extends: line, case: upper, letter_spacing: 8 } }
              - { type: text, at: [960, 130], size: [900, 210], text: "title case keeps GHz and (brackets) as written", style: { extends: line, case: title, wrap: true } }
            """, TestClock.Locale(), ("gpu.name", "GeForce rtx 4090"), ("gpu.temp", 64));

        Snapshots.Match(frame, "letter-case");
    }

    // An outline that can't be seen, so a frame shows only the red letters, while the labels still take its room.
    const string HiddenOutline = ", text_stroke: { width: 14, color: \"#00000000\" }";

    // A dial's two labels, either side of 12 o'clock and a little apart.
    static string TwoTickLabels(string stroke) => Landscape + $$"""
        widgets:
          - type: gauge
            at: [0, 0]
            size: [400, 400]
            source: cpu.load
            min: 10
            max: 20
            start: -95
            sweep: 10
            thickness: 0
            ticks: { major: 1, length: 4, color: "#00000000", labels: "{0:0}" }
            style: { size: 24, color: "#FF0000"{{stroke}} }
        """;

    [Fact]
    public void A_dial_leaves_out_a_label_whose_outline_would_touch_the_last_one()
    {
        using var plain = Render(TwoTickLabels(""));
        using var outlined = Render(TwoTickLabels(HiddenOutline));
        var (left, right) = (new SKRectI(0, 0, 200, 200), new SKRectI(200, 0, 400, 200));

        Assert.False(Lit(plain, left, Reddish).IsEmpty);
        Assert.False(Lit(plain, right, Reddish).IsEmpty);
        Assert.False(Lit(outlined, left, Reddish).IsEmpty);
        Assert.True(Lit(outlined, right, Reddish).IsEmpty);
    }

    // A donut's label inside a ring that has room for its letters but not for an outline round them as well.
    static string RingLabel(string stroke) => Landscape + $$"""
        widgets:
          - type: donut
            at: [0, 0]
            size: [400, 400]
            hole: 0.72
            labels: inside
            style: { size: 24, color: "#FF0000"{{stroke}} }
            slices:
              - { source: cpu.power, label: CPU, style: { fill: "#2A8C94" } }
              - { source: gpu.power }
        """;

    [Fact]
    public void A_donut_leaves_out_a_label_whose_outline_would_leave_the_ring()
    {
        using var plain = Render(RingLabel(""), ("cpu.power", 50), ("gpu.power", 0));
        using var outlined = Render(RingLabel(HiddenOutline), ("cpu.power", 50), ("gpu.power", 0));
        var whole = new SKRectI(0, 0, 400, 400);

        Assert.False(Lit(plain, whole, Reddish).IsEmpty);
        Assert.True(Lit(outlined, whole, Reddish).IsEmpty);
    }

    [Fact]
    public void Labels_snapshot()
    {
        using var frame = Render(Landscape + """
            background:
              - fill: { linear: ["#5B86E5", "#36D1DC"] }
            widgets:
              # Outlined, gradient-filled tick labels; the ticks keep their own colour.
              - type: gauge
                at: [40, 40]
                size: [400, 400]
                source: cpu.load
                thickness: 16
                needle: { width: 10, color: "#FFFFFF", cap: 14 }
                ticks: { major: 5, minor: 1, length: 14, width: 3, color: "#FFFFFF", labels: "{0:0}%" }
                style: { size: 30, weight: bold, fill: "#FF5A36", track: "#00000040", text_fill: { linear: ["#FFE259", "#FF5A36"], angle: 90 }, text_stroke: { width: 3, color: "#000000" } }
              # Italic, underlined labels in upper case.
              - type: gauge
                at: [480, 40]
                size: [400, 400]
                source: gpu.temp
                max: 100
                thickness: 0
                needle: { width: 8, color: "#000000", cap: 10 }
                ticks: { major: 4, length: 12, color: "#000000", labels: "{0:0}c" }
                style: { size: 28, color: "#000000", italic: true, underline: true, case: upper }
              # Outlined inside labels on a donut, in title case.
              - type: donut
                at: [920, 40]
                size: [400, 400]
                hole: 0.45
                labels: inside
                format: "{0:0}%"
                style: { size: 24, weight: bold, color: "#FFFFFF", text_stroke: { width: 3, color: "#000000" }, case: title }
                slices:
                  - { source: cpu.power, label: cpu, style: { fill: "#2A8C94" } }
                  - { source: gpu.power, label: gpu, style: { fill: "#B33A1E" } }
                  - { source: ram.power, label: ram, style: { fill: "#6B3FA0" } }
              # Outside labels with a gradient fill, struck through.
              - type: donut
                at: [1360, 40]
                size: [540, 400]
                labels: outside
                style: { size: 28, weight: bold, text_fill: { linear: ["#FFFFFF", "#FF5A36"] }, text_stroke: { width: 2, color: "#000000" }, strike: true }
                slices:
                  - { source: cpu.power, label: CPU, style: { fill: "#36D1DC" } }
                  - { source: gpu.power, label: GPU, style: { fill: "#FF5A36" } }
                  - { source: ram.power, label: RAM, style: { fill: "#A770EF" } }
            """, ("cpu.load", 62), ("gpu.temp", 71), ("cpu.power", 120), ("gpu.power", 280), ("ram.power", 40));

        Snapshots.Match(frame, "letter-labels");
    }
}
