using System.Globalization;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Text and value widgets: the box, then the text in its style's case, broken into lines that fit inside the padding,
/// each line aligned on its own and the block of lines centred vertically, drawn in TextInk's layers. Dial and donut
/// labels use its font, measures and TextInk too, so a style's text looks the same wherever it is drawn. Characters the
/// font lacks are measured and drawn in a font Windows has for them (FallbackText).
static class TextPainter
{
    /// Skia's slant for a face with no italic of its own: x moves left by a quarter of y, which leans letters right.
    internal const float FauxItalicSkew = -0.25f;

    /// A line ready to draw: its run of text and its baseline origin.
    readonly record struct Line(TextRun Run, float X, float Y);

    public static void Paint(RenderContext ctx, SKRect bounds, StyleSpec style, string text)
    {
        BoxPainter.Paint(ctx, bounds, style);
        if (text.Length == 0) return;

        using var font = Font(ctx, style);
        var culture = ctx.Locale.Culture;
        var lines = Layout(TextCase.Apply(text, style.Case, culture), font, style, bounds, culture);
        try
        {
            using var ink = new TextInk(style, bounds, ctx.Time);
            foreach (var layer in TextInk.Layers)
                foreach (var line in lines)
                    ink.Draw(ctx.Canvas, layer, line.Run, line.X, line.Y);
        }
        finally
        {
            foreach (var line in lines) line.Run.Dispose();
        }
    }

    /// The font text in this style is drawn with, italic when the style says so. The caller disposes it.
    public static SKFont Font(RenderContext ctx, StyleSpec style)
    {
        var weight = style.Weight ?? StyleDefaults.Weight;
        var italic = style.Italic ?? StyleDefaults.Italic;
        var typeface = ctx.Assets.Typeface(style.Font, weight, italic);
        return new SKFont(typeface, style.Size ?? StyleDefaults.Size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.None,
            Embolden = weight >= 600 && typeface.FontWeight < 600,
            SkewX = italic && typeface.FontStyle.Slant == SKFontStyleSlant.Upright ? FauxItalicSkew : 0,
        };
    }

    static List<Line> Layout(string text, SKFont font, StyleSpec style, SKRect bounds, CultureInfo culture)
    {
        var padding = style.Padding ?? StyleDefaults.Padding;
        var content = SKRect.Inflate(bounds, -padding, -padding);
        var spacing = style.LetterSpacing ?? StyleDefaults.LetterSpacing;
        var wrap = style.Wrap ?? StyleDefaults.Wrap;
        var texts = TextLayout.Lines(text, content.Width, wrap, line => Width(font, line, spacing, culture));

        var metrics = font.Metrics;
        var advance = font.Size * (style.LineHeight ?? StyleDefaults.LineHeight);
        var count = TextLayout.Fitting(texts.Count, advance, metrics.Descent - metrics.Ascent, content.Height);

        // One line sits where a single line always has; more lines spread evenly above and below that spot.
        var first = content.MidY - (metrics.Ascent + metrics.Descent) / 2 - (count - 1) * advance / 2;
        var lines = new List<Line>(count);
        for (var i = 0; i < count; i++)
        {
            if (texts[i].Length == 0) continue;
            var run = new TextRun(texts[i], font, spacing, culture);
            var x = (style.Align ?? StyleDefaults.Align) switch
            {
                TextAlign.Center => content.MidX - run.Width / 2,
                TextAlign.Right => content.Right - run.Width,
                _ => content.Left,
            };
            lines.Add(new Line(run, x, first + i * advance));
        }

        return lines;
    }

    /// The line's advance plus the spacing between each pair of characters, not after the last, so spaced text still
    /// lines up with its box's edge. Characters the font lacks are measured in the faces they are drawn in, which Windows
    /// chooses for `culture`, the renderer's.
    public static float Width(SKFont font, string line, float spacing, CultureInfo culture)
    {
        using var fallback = FallbackText.Of(font, line, culture);
        return fallback?.Width(spacing) ?? font.MeasureText(line) + Spread(font, line, spacing);
    }

    /// The box round the line's ink, from its baseline origin, with `spacing` between its characters, as Width measures it.
    public static SKRect Ink(SKFont font, string line, float spacing, CultureInfo culture)
    {
        using var fallback = FallbackText.Of(font, line, culture);
        if (fallback is not null) return fallback.Ink(spacing);

        font.MeasureText(line, out var ink);
        ink.Right += Spread(font, line, spacing);
        return ink;
    }

    /// How much wider letter spacing makes a line: the spacing once between each pair of characters.
    static float Spread(SKFont font, string line, float spacing) =>
        spacing == 0 || line.Length == 0 ? 0 : spacing * Math.Max(0, font.CountGlyphs(line) - 1);
}
