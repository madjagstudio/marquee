using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Marquee.Core.Expressions;
using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using SkiaSharp;
using Xunit.Abstractions;

namespace Marquee.Core.Tests;

/// Characters the style's font has no glyph for, drawn with a font Windows has for them: Yu Gothic or Meiryo, Microsoft
/// YaHei, Malgun Gothic, Segoe UI, Leelawadee UI and Nirmala UI come with every Windows 10 and 11 install. On a machine
/// stripped of them, a test that needs one says so in its output and passes, as the Media Foundation tests do. The two
/// whole-frame snapshots also need the very faces they were approved with, as another version of a font draws other
/// pixels; every other test checks what holds whichever faces Windows offers.
public class FontFallbackTests(ITestOutputHelper output)
{
    const string Landscape = "canvas: { orientation: landscape }\n";

    /// A character no font has: the last code point of the last private use plane.
    const string Nowhere = "\U0010FFFD";

    /// The renderer's culture in these tests (TestClock's), which Windows chooses fallback fonts for.
    static readonly CultureInfo Culture = TestClock.Locale().Culture;

    /// The `head` table, whose fontRevision tells one version of a font from another.
    const uint Head = 'h' << 24 | 'e' << 16 | 'a' << 8 | 'd';

    /// The fallback faces font-fallback-scripts.png was approved with: family, weight and fontRevision, on Windows 11
    /// 10.0.26200 (24H2) in US English.
    static readonly string[] ScriptsFaces =
    [
        "Leelawadee UI 400 6", "Malgun Gothic 400 6.69", "Nirmala UI 400 1.46001", "Segoe UI 400 5.72", "Yu Gothic UI 400 1.95",
        "Yu Gothic UI 700 1.95",
    ];

    /// The fallback faces font-fallback-labels.png was approved with, as ScriptsFaces.
    static readonly string[] LabelsFaces = ["Fake 400 1", "Yu Gothic UI 700 1.95"];

    public static TheoryData<string, string> Scripts => new()
    {
        { "Japanese", "今日の温度はカタカナで" },
        { "Chinese", "中文温度显示" },
        { "Korean", "한국어 온도" },
        { "Arabic", "درجة الحرارة" },
        { "Hebrew", "טמפרטורה של המעבד" },
        { "Thai", "อุณหภูมิ" },
        { "Devanagari", "तापमान" },
    };

    /// Bundled Inter as a style draws it: TextPainter.Font's settings, bold from Inter Bold and italic slanted.
    static SKFont Inter(float size = 48, bool bold = false, bool italic = false) =>
        new(bold ? BundledFonts.Bold : BundledFonts.Regular, size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.None,
            SkewX = italic ? TextPainter.FauxItalicSkew : 0,
        };

    /// Whether Windows has a font for every character of `text` that Inter lacks, asked as the fallback asks for upright
    /// Inter; says which it hasn't when it doesn't.
    bool Installed(string text)
    {
        var inter = BundledFonts.Regular;
        var missing = text.EnumerateRunes()
            .Where(rune => !inter.ContainsGlyph(rune.Value))
            .Where(rune => FontFallback.Match(inter.FamilyName, inter.FontWeight, italic: false, Culture, rune.Value) is null)
            .Select(rune => $"U+{rune.Value:X4}")
            .ToList();
        if (missing.Count == 0) return true;
        output.WriteLine($"Skipped: Windows offers no font for {string.Join(", ", missing)} on this machine.");
        return false;
    }

    /// Whether the fallback faces a scene's lines are drawn in, each line as (text, bold, italic) in Inter, are the
    /// `approved` ones its snapshot was approved with; says which they are when they aren't.
    bool SameFaces(string snapshot, string[] approved, params (string Text, bool Bold, bool Italic)[] lines)
    {
        var faces = lines.SelectMany(line =>
            {
                using var font = Inter(bold: line.Bold, italic: line.Italic);
                using var fallback = FallbackText.Of(font, line.Text, Culture);
                return fallback?.Pieces.Where(p => p.Font != font).Select(p => Fingerprint(p.Font.Typeface!)).ToList() ?? [];
            })
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        if (faces.SequenceEqual(approved)) return true;
        // A machine whose faces differ may skip, but only once the snapshot exists: a missing one is never skipped,
        // so a first run elsewhere can't finish green without anything approved.
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(ThisFile())!, "Snapshots", $"{snapshot}.png")),
            $"No approved snapshot '{snapshot}', and this machine's fallback faces aren't the recorded ones. Put these in the"
            + $" test's face list, run it again and approve the image: {string.Join(", ", faces.Select(face => $"\"{face}\""))}");
        output.WriteLine($"Skipped: the fallback faces differ from those {snapshot}.png was approved with. On this machine: "
            + string.Join(", ", faces.Select(face => $"\"{face}\"")));
        return false;
    }

    static string ThisFile([CallerFilePath] string file = "") => file;

    /// A face's family, weight and version: its `head` table's fontRevision, a 16.16 fixed-point number.
    static string Fingerprint(SKTypeface face)
    {
        var head = face.GetTableData(Head);
        var revision = head.Length >= 8 ? BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(4)) / 65536.0 : 0;
        return string.Create(CultureInfo.InvariantCulture, $"{face.FamilyName} {face.FontWeight} {revision:0.#####}");
    }

    static SKImage Render(string layout, TimeLocale? locale = null, params (string, object)[] values)
    {
        using var folder = new TempTheme(layout);
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme) { Locale = locale ?? TestClock.Locale() };
        using var frame = renderer.Render(TestSensors.Of(values), TimeSpan.Zero);
        return Frames.ThroughSink(frame);
    }

    static void AssertSamePixels(SKImage expected, SKImage actual, SKRectI? region = null)
    {
        using var a = SKBitmap.FromImage(expected);
        using var b = SKBitmap.FromImage(actual);
        var area = region ?? new SKRectI(0, 0, a.Width, a.Height);
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
                Assert.True(a.GetPixel(x, y) == b.GetPixel(x, y), $"The frames differ at ({x}, {y})");
    }

    static bool SamePixels(SKImage a, SKImage b)
    {
        using var x = SKBitmap.FromImage(a);
        using var y = SKBitmap.FromImage(b);
        return x.Pixels.SequenceEqual(y.Pixels);
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Every_character_gets_a_glyph(string script, string text)
    {
        if (!Installed(text)) return;
        using var font = Inter();

        using var fallback = FallbackText.Of(font, text, Culture);

        Assert.NotNull(fallback);
        Assert.Equal(text, string.Concat(fallback.Pieces.Select(p => p.Text)));
        foreach (var piece in fallback.Pieces)
            Assert.True(!piece.Font.GetGlyphs(piece.Text).Contains((ushort)0),
                $"{script}: '{piece.Text}' in {piece.Font.Typeface?.FamilyName} has a character drawn as an empty box");
        Assert.Contains(fallback.Pieces, p => p.Font != font);
    }

    [Theory]
    [InlineData("CPU 52°C")]
    [InlineData("Ünïcödé Ελληνικά Кириллица")]
    [InlineData("")]
    public void Text_the_font_covers_needs_no_fallback(string text)
    {
        using var font = Inter();

        Assert.Null(FallbackText.Of(font, text, Culture));
    }

    [Fact]
    public void A_character_no_font_has_draws_as_it_always_has()
    {
        using var font = Inter();

        // No pieces: the line takes the old path, which draws the font's empty box.
        Assert.Null(FallbackText.Of(font, "A" + Nowhere, Culture));
        using var frame = Render(Landscape + $"widgets:\n  - {{ type: text, at: [40, 40], size: [400, 100], text: \"A{Nowhere}\" }}\n");
    }

    [Fact]
    public void The_covered_part_stays_on_the_font_and_the_rest_moves_in_whole_characters()
    {
        const string Text = "CPU 52°C 温度";
        if (!Installed(Text)) return;
        using var font = Inter();

        using var fallback = FallbackText.Of(font, Text, Culture)!;

        Assert.Equal(["CPU 52°C ", "温度"], fallback.Pieces.Select(p => p.Text));
        Assert.Same(font, fallback.Pieces[0].Font);
        Assert.NotSame(font, fallback.Pieces[1].Font);
    }

    /// A letter with its marks, or a character written as a surrogate pair, is drawn in one face: a Thai consonant and
    /// its vowel above, a Devanagari consonant and its vowel sign, a CJK character beyond the Basic Multilingual Plane
    /// between Latin letters, and a digit Inter has with a keycap mark it hasn't, which go to a face that has both.
    [Theory]
    [InlineData("กี่")]
    [InlineData("कि")]
    [InlineData("A𠮷B")]
    [InlineData("A1⃣")]
    public void A_letter_and_its_marks_keep_to_one_face(string text)
    {
        if (!Installed(text)) return;
        using var font = Inter();

        using var fallback = FallbackText.Of(font, text, Culture);

        Assert.NotNull(fallback);
        var start = 0;
        var boundaries = new HashSet<int>(StringInfo.ParseCombiningCharacters(text));
        foreach (var piece in fallback.Pieces)
        {
            Assert.Contains(start, boundaries);
            Assert.DoesNotContain((ushort)0, piece.Font.GetGlyphs(piece.Text));
            start += piece.Text.Length;
        }

        Assert.Equal(text.Length, start);
    }

    /// A word stays in one fallback face rather than changing typeface partway through. In US English, for one, Windows
    /// offers Yu Gothic UI for every character of this Chinese word but 显, which it has no glyph for, and Microsoft
    /// JhengHei UI for 显, which has them all.
    [Fact]
    public void A_word_keeps_to_one_fallback_face()
    {
        const string Text = "中文温度显示";
        if (!Installed(Text)) return;
        using var font = Inter();

        using var fallback = FallbackText.Of(font, Text, Culture)!;

        var piece = Assert.Single(fallback.Pieces);
        Assert.Equal(Text, piece.Text);
        Assert.DoesNotContain((ushort)0, piece.Font.GetGlyphs(Text));
    }

    /// A color emoji (Segoe UI Emoji's) keeps its own colors, so the glow, shadow and outline leave it out rather than
    /// draw a colored copy of it, and still take the letters beside it.
    [Theory]
    [InlineData("glow: { color: \"#36D1DC\", radius: 12 }")]
    [InlineData("shadow: { color: \"#00FF00\", x: 30, y: 30 }")]
    [InlineData("text_stroke: { width: 4, color: \"#FF0000\" }")]
    public void A_color_emoji_takes_no_glow_shadow_or_outline(string effect)
    {
        const string Emoji = "\U0001F600";
        if (!Installed(Emoji)) return;
        const string Widget = "  - { type: text, at: [40, 40], size: [600, 240], style: { size: 96, align: left";

        using var emoji = Render(Landscape + "widgets:\n" + Widget + $" }}, text: \"{Emoji}\" }}\n");
        using var emojiWithEffect = Render(Landscape + "widgets:\n" + Widget + $", {effect} }}, text: \"{Emoji}\" }}\n");
        using var letters = Render(Landscape + "widgets:\n" + Widget + $" }}, text: \"A{Emoji}\" }}\n");
        using var lettersWithEffect = Render(Landscape + "widgets:\n" + Widget + $", {effect} }}, text: \"A{Emoji}\" }}\n");
        using var empty = Render(Landscape + "widgets:\n" + Widget + " }, text: \" \" }\n");

        Assert.False(SamePixels(empty, emoji), "The emoji isn't drawn");
        AssertSamePixels(emoji, emojiWithEffect);
        Assert.False(SamePixels(letters, lettersWithEffect), "The letter beside the emoji has no effect");
    }

    [Fact]
    public void Bold_and_italic_carry_over_to_the_fallback_face()
    {
        if (!Installed("温")) return;
        using var plain = Inter();
        using var boldItalic = Inter(bold: true, italic: true);

        using var upright = FallbackText.Of(plain, "温", Culture)!;
        using var slanted = FallbackText.Of(boldItalic, "温", Culture)!;

        var regular = upright.Pieces.Single().Font;
        Assert.True(regular.Typeface!.FontWeight < 600 && !regular.Embolden, "The fallback is bold");
        Assert.Equal(0, regular.SkewX);
        Assert.Equal(SKFontStyleSlant.Upright, regular.Typeface.FontStyle.Slant);
        Assert.Equal(48, regular.Size);

        var bold = slanted.Pieces.Single().Font;
        Assert.True(bold.Typeface!.FontWeight >= 600 || bold.Embolden, "The fallback isn't bold");
        // Slanted once: by its own italic or oblique face, or else by Marquee.
        Assert.True(bold.Typeface.FontStyle.Slant != SKFontStyleSlant.Upright ^ bold.SkewX == TextPainter.FauxItalicSkew,
            $"The fallback is slanted wrongly: {bold.Typeface.FontStyle.Slant}, skew {bold.SkewX}");
    }

    [Fact]
    public void A_line_measures_as_its_pieces_with_letter_spacing_between_every_character()
    {
        const string Text = "CPU 温度";
        if (!Installed(Text)) return;
        using var font = Inter();
        using var fallback = FallbackText.Of(font, Text, Culture)!;
        var advance = fallback.Pieces.Sum(p => p.Font.MeasureText(p.Text));

        Assert.Equal(advance, TextPainter.Width(font, Text, 0, Culture), 3);
        // Six characters, so five gaps.
        Assert.Equal(advance + 5 * 10, TextPainter.Width(font, Text, 10, Culture), 3);
        using var run = new TextRun(Text, font, 10, Culture);
        Assert.Equal(advance + 5 * 10, run.Width, 3);
    }

    [Theory]
    [InlineData("size: 64, align: left")]
    [InlineData("size: 64, align: left, letter_spacing: 6, text_stroke: { width: 2, color: \"#FF0000\" }")]
    [InlineData("size: 64, align: left, italic: true, weight: bold")]
    public void Latin_beside_a_fallback_draws_exactly_as_alone(string style)
    {
        const string Latin = "CPU 52°C ";
        if (!Installed("温度")) return;
        const string Widget = "  - { type: text, at: [40, 40], size: [1200, 160], text: \"";

        using var alone = Render(Landscape + "widgets:\n" + Widget + Latin + "\", style: { " + style + " } }\n");
        using var mixed = Render(Landscape + "widgets:\n" + Widget + Latin + "温度\", style: { " + style + " } }\n");

        // Everything up to where the Japanese starts: the widget's padding, then the Latin's advance.
        using var font = Inter(64);
        var end = (int)(40 + StyleDefaults.Padding + TextPainter.Width(font, Latin.TrimEnd(), 0, Culture));
        AssertSamePixels(alone, mixed, new SKRectI(0, 0, end, 480));
        Assert.False(SamePixels(alone, mixed));
    }

    [Fact]
    public void A_japanese_weekday_from_the_time_helper_draws_its_letter()
    {
        if (!Installed("金")) return;
        var japanese = TestClock.Locale("ja-JP");
        const string Widget = "  - { type: text, at: [40, 40], size: [400, 160], style: { size: 96 }, text: ";

        // Friday 16 October 2026 is 金 (gold) in Japanese.
        using var weekday = Render(Landscape + "widgets:\n" + Widget + "\"{now|time:ddd}\" }\n", japanese);
        using var written = Render(Landscape + "widgets:\n" + Widget + "\"金\" }\n", japanese);
        using var box = Render(Landscape + "widgets:\n" + Widget + $"\"{Nowhere}\" }}\n", japanese);

        AssertSamePixels(written, weekday);
        Assert.False(SamePixels(box, weekday), "The weekday is drawn as an empty box");
    }

    [Theory]
    [InlineData("underline")]
    [InlineData("strike")]
    public void Lines_run_unbroken_across_a_fallback(string line)
    {
        const string Text = "温度 Temp 温度";
        if (!Installed(Text)) return;

        using var frame = Render(Landscape + "widgets:\n"
            + $"  - {{ type: text, at: [40, 40], size: [1200, 160], text: \"{Text}\", style: {{ size: 64, align: left, color: \"#FF0000\", {line}: true }} }}\n");

        using var font = Inter(64);
        var left = (int)(40 + StyleDefaults.Padding);
        var width = (int)TextPainter.Width(font, Text, 0, Culture);
        // The row the line is drawn on is the longest red run in the box; it reaches from the first letter to the last.
        using var bitmap = SKBitmap.FromImage(frame);
        var longest = Enumerable.Range(40, 160).Max(y => Enumerable.Range(left + 1, width - 2).Count(x => bitmap.GetPixel(x, y).Red > 200));
        Assert.Equal(width - 2, longest);
    }

    [Fact]
    public void Spans_with_a_fallback_draw_exactly_as_the_same_text()
    {
        const string Text = "温度 52°C";
        if (!Installed(Text)) return;
        const string Widget = "  - { type: text, at: [40, 40], size: [800, 200], ";

        using var plain = Render(Landscape + "widgets:\n" + Widget + $"text: \"{Text}\", style: {{ size: 80, letter_spacing: 8 }} }}\n");
        using var split = Render(Landscape + "widgets:\n" + Widget
            + "spans: [{ text: \"温度 5\" }, { text: \"2°C\" }], style: { size: 80, letter_spacing: 8 } }\n");

        AssertSamePixels(plain, split);
    }

    /// Windows picks the fallback for the renderer's regional format, not the machine's, as the time helper's names
    /// follow it: the same Chinese characters take Japanese forms in Japanese and Chinese ones in Chinese.
    [Fact]
    public void Chinese_characters_take_the_forms_of_the_renderers_regional_format()
    {
        const string Text = "温度";
        if (!Installed(Text)) return;
        var families = new[] { "ja-JP", "zh-CN" }.Select(culture => SKFontManager.Default.MatchCharacter(
            BundledFonts.Regular.FamilyName, BundledFonts.Regular.FontWeight, (int)SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright, [culture], Text[0])?.FamilyName).ToList();
        if (families[0] == families[1])
        {
            output.WriteLine($"Skipped: Windows offers {families[0]} for Chinese characters in Japanese and in Chinese on this machine.");
            return;
        }

        const string Widget = "  - { type: text, at: [40, 40], size: [400, 160], style: { size: 96 }, text: \"" + Text + "\" }\n";
        using var japanese = Render(Landscape + "widgets:\n" + Widget, TestClock.Locale("ja-JP"));
        using var chinese = Render(Landscape + "widgets:\n" + Widget, TestClock.Locale("zh-CN"));

        Assert.False(SamePixels(japanese, chinese), $"Both draw in one face, though Windows offers {families[0]} and {families[1]}");
    }

    [Fact]
    public void Scripts_snapshot()
    {
        if (!Installed(string.Concat(Scripts.Select(row => (string)row[1])))) return;
        if (!SameFaces("font-fallback-scripts", ScriptsFaces, ("CPU 52°C 今日の温度", false, false), ("中文温度 GPU 71°C", true, false),
                ("한국어 온도 · Korean", false, true), ("Arabic درجة الحرارة", false, false), ("Hebrew טמפרטורה", false, false),
                ("Thai อุณหภูมิ", false, false), ("Hindi तापमान", false, true), ("WRAPPED 温度 AND 中文 ACROSS LINES IN A BOX", false, false)))
            return;

        using var frame = Render(Landscape + """
            background:
              - fill: { linear: ["#0B0F1A", "#1B2440"], angle: 90 }
            styles:
              line: { size: 44, color: "#F4F6FA", align: left }
            widgets:
              - { type: text, at: [40, 20], size: [900, 80], text: "CPU 52°C 今日の温度", style: { extends: line, underline: true } }
              - { type: text, at: [40, 110], size: [900, 80], text: "中文温度 GPU 71°C", style: { extends: line, weight: bold, glow: { color: "#36D1DC", radius: 10 } } }
              - { type: text, at: [40, 200], size: [900, 80], text: "한국어 온도 · Korean", style: { extends: line, italic: true, strike: true } }
              - { type: text, at: [40, 290], size: [900, 80], text: "Arabic درجة الحرارة", style: { extends: line, letter_spacing: 4, text_stroke: { width: 2, color: "#FF5A36" } } }
              - { type: text, at: [40, 380], size: [900, 80], text: "Hebrew טמפרטורה", style: { extends: line, shadow: { color: "#000000", x: 4, y: 4, blur: 2 } } }
              - { type: text, at: [980, 20], size: [900, 80], text: "Thai อุณหภูมิ", style: { extends: line, text_fill: { linear: ["#FFE259", "#FF5A36"] } } }
              - { type: text, at: [980, 110], size: [900, 80], text: "Hindi तापमान", style: { extends: line, align: right, italic: true } }
              - type: text
                at: [980, 200]
                size: [900, 260]
                text: "wrapped 温度 and 中文 across lines in a box"
                style: { size: 40, color: "#FFB547", align: center, wrap: true, case: upper }
            """);

        Snapshots.Match(frame, "font-fallback-scripts");
    }

    [Fact]
    public void Labels_and_spans_snapshot()
    {
        if (!Installed("温度度℃。高中低")) return;
        if (!SameFaces("font-fallback-labels", LabelsFaces, ("0度", true, false), ("高", false, false), ("中", false, false), ("低", false, false),
                ("高", false, true), ("中", false, true), ("低", false, true), ("温度 ", false, false), ("℃", false, false), ("度。", false, true)))
            return;

        using var frame = Render(Landscape + """
            background:
              - fill: "#101521"
            widgets:
              # A dial's tick labels with a Japanese unit.
              - type: gauge
                at: [40, 40]
                size: [400, 400]
                source: cpu.load
                thickness: 16
                needle: { width: 10, color: "#FFFFFF", cap: 14 }
                ticks: { major: 4, length: 12, color: "#FFFFFF", labels: "{0:0}度" }
                style: { size: 28, weight: bold, fill: "#36D1DC", track: "#FFFFFF20", text_stroke: { width: 2, color: "#000000" } }
              # A donut with Chinese labels, inside and out.
              - type: donut
                at: [480, 40]
                size: [400, 400]
                hole: 0.45
                labels: inside
                style: { size: 30, color: "#FFFFFF", underline: true }
                slices:
                  - { source: cpu.power, label: 高, style: { fill: "#2A8C94" } }
                  - { source: gpu.power, label: 中, style: { fill: "#B33A1E" } }
                  - { source: ram.power, label: 低, style: { fill: "#6B3FA0" } }
              - type: donut
                at: [920, 40]
                size: [460, 400]
                labels: outside
                format: "{0:0}%"
                style: { size: 26, color: "#FFFFFF", italic: true }
                slices:
                  - { source: cpu.power, label: 高, style: { fill: "#36D1DC" } }
                  - { source: gpu.power, label: 中, style: { fill: "#FF5A36" } }
                  - { source: ram.power, label: 低, style: { fill: "#A770EF" } }
              # Spans: a Japanese caption, a big number and a small unit, on one baseline.
              - type: text
                at: [1420, 40]
                size: [480, 200]
                style: { align: center }
                spans:
                  - { text: "温度 ", style: { size: 40, color: "#FFB547" } }
                  - { text: "52", style: { size: 120, weight: bold, color: "#F4F6FA" } }
                  - { text: "℃", style: { size: 48, color: "#36D1DC", underline: true } }
              # A value widget's spans with a fallback around its own value.
              - type: value
                at: [1420, 260]
                size: [480, 180]
                source: gpu.temp
                style: { align: center }
                spans:
                  - { text: "{0:0}", style: { size: 96, weight: bold, color: "#F4F6FA" } }
                  - { text: "度。", style: { size: 48, color: "#FF5A36", italic: true } }
            """, null, ("cpu.load", 62), ("gpu.temp", 71), ("cpu.power", 120), ("gpu.power", 280), ("ram.power", 40));

        Snapshots.Match(frame, "font-fallback-labels");
    }

    /// What a frame costs with a line that needs a fallback and with one that doesn't, written to the test's output. Once
    /// the faces are found, a mixed line pays only for splitting itself and building its pieces: a few microseconds, far
    /// below the millisecond this allows for a slow or busy machine, and the 33 ms a frame has at 30 fps.
    [Fact]
    public void A_fallback_costs_a_frame_under_a_millisecond()
    {
        const string Widget = "  - { type: text, at: [40, 40], size: [1840, 160], style: { size: 72 }, text: ";
        var latin = Measure(Landscape + "widgets:\n" + Widget + "\"CPU 52°C GPU 71°C RAM 43% FAN 1200 rpm\" }\n");
        var mixed = Measure(Landscape + "widgets:\n" + Widget + "\"CPU 52°C 温度 GPU 71°C 한국어 RAM 43%\" }\n");

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Per frame: pure Latin {latin.TotalMilliseconds:0.000} ms, mixed {mixed.TotalMilliseconds:0.000} ms"));
        Assert.True(mixed - latin < TimeSpan.FromMilliseconds(1), "The fallback costs a millisecond or more a frame");
    }

    static TimeSpan Measure(string layout)
    {
        const int Frames = 200;
        using var folder = new TempTheme(layout);
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme) { Locale = TestClock.Locale() };
        var sensors = TestSensors.Of();
        for (var i = 0; i < 20; i++) renderer.Render(sensors, TimeSpan.Zero).Dispose();

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < Frames; i++) renderer.Render(sensors, TimeSpan.Zero).Dispose();
        return clock.Elapsed / Frames;
    }
}
