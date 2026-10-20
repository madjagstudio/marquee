using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Text and value widgets: the box, then the text broken into lines that fit inside the padding, each line aligned on
/// its own and the block of lines centred vertically, drawn glow first, then shadow, then the text itself. Dial and
/// donut labels use its font and letter spacing too, so a style's text looks the same wherever it is drawn.
static class TextPainter
{
    /// A line ready to draw: its baseline origin and, when letter spacing is set, its glyphs already spread out.
    readonly record struct Line(string Text, float X, float Y, SKTextBlob? Spaced);

    public static void Paint(RenderContext ctx, SKRect bounds, StyleSpec style, string text)
    {
        BoxPainter.Paint(ctx, bounds, style);
        if (text.Length == 0) return;

        using var font = Font(ctx, style);
        var lines = Layout(text, font, style, bounds);
        try
        {
            Draw(ctx, style, font, lines);
        }
        finally
        {
            foreach (var line in lines) line.Spaced?.Dispose();
        }
    }

    /// The font text in this style is drawn with. The caller disposes it.
    public static SKFont Font(RenderContext ctx, StyleSpec style)
    {
        var weight = style.Weight ?? StyleDefaults.Weight;
        var typeface = ctx.Assets.Typeface(style.Font, weight);
        return new SKFont(typeface, style.Size ?? StyleDefaults.Size)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
            Hinting = SKFontHinting.None,
            Embolden = weight >= 600 && typeface.FontWeight < 600,
        };
    }

    static List<Line> Layout(string text, SKFont font, StyleSpec style, SKRect bounds)
    {
        var padding = style.Padding ?? StyleDefaults.Padding;
        var content = SKRect.Inflate(bounds, -padding, -padding);
        var spacing = style.LetterSpacing ?? StyleDefaults.LetterSpacing;
        var wrap = style.Wrap ?? StyleDefaults.Wrap;
        var texts = TextLayout.Lines(text, content.Width, wrap, line => Width(font, line, spacing));

        var metrics = font.Metrics;
        var advance = font.Size * (style.LineHeight ?? StyleDefaults.LineHeight);
        var count = TextLayout.Fitting(texts.Count, advance, metrics.Descent - metrics.Ascent, content.Height);

        // One line sits where a single line always has; more lines spread evenly above and below that spot.
        var first = content.MidY - (metrics.Ascent + metrics.Descent) / 2 - (count - 1) * advance / 2;
        var lines = new List<Line>(count);
        for (var i = 0; i < count; i++)
        {
            if (texts[i].Length == 0) continue;
            var width = Width(font, texts[i], spacing);
            var x = (style.Align ?? StyleDefaults.Align) switch
            {
                TextAlign.Center => content.MidX - width / 2,
                TextAlign.Right => content.Right - width,
                _ => content.Left,
            };
            lines.Add(new Line(texts[i], x, first + i * advance, spacing == 0 ? null : Spaced(font, texts[i], spacing)));
        }

        return lines;
    }

    /// Every line's glow before any line's text, so a glow never paints over the line above it; shadows likewise.
    static void Draw(RenderContext ctx, StyleSpec style, SKFont font, List<Line> lines)
    {
        if (Effect.Glow(style, ctx.Time) is { } glow)
        {
            using var blur = glow.Blur();
            using var paint = glow.Paint(blur);
            foreach (var line in lines) DrawLine(ctx.Canvas, line, 0, 0, font, paint);
        }

        if (Effect.Shadow(style) is { } shadow)
        {
            using var blur = shadow.Blur();
            using var paint = shadow.Paint(blur);
            foreach (var line in lines) DrawLine(ctx.Canvas, line, shadow.Dx, shadow.Dy, font, paint);
        }

        using var textPaint = new SKPaint { IsAntialias = true, Color = style.Color ?? StyleDefaults.Color };
        foreach (var line in lines) DrawLine(ctx.Canvas, line, 0, 0, font, textPaint);
    }

    /// Without letter spacing the line is drawn as a plain string, exactly as text always was.
    static void DrawLine(SKCanvas canvas, Line line, float dx, float dy, SKFont font, SKPaint paint)
    {
        if (line.Spaced is { } blob) canvas.DrawText(blob, line.X + dx, line.Y + dy, paint);
        else canvas.DrawText(line.Text, line.X + dx, line.Y + dy, font, paint);
    }

    /// One line of text with its baseline origin at (x, y), spread by `spacing` as a text widget's lines are.
    public static void DrawLine(SKCanvas canvas, string text, float x, float y, SKFont font, float spacing, SKPaint paint)
    {
        if (spacing == 0)
        {
            canvas.DrawText(text, x, y, font, paint);
            return;
        }

        using var blob = Spaced(font, text, spacing);
        if (blob is not null) canvas.DrawText(blob, x, y, paint);
    }

    /// The line's advance plus the spacing between each pair of characters, not after the last, so spaced text still
    /// lines up with its box's edge.
    public static float Width(SKFont font, string line, float spacing) => font.MeasureText(line) + Spread(font, line, spacing);

    /// The box round the line's ink, from its baseline origin, with `spacing` between its characters.
    public static SKRect Ink(SKFont font, string line, float spacing)
    {
        font.MeasureText(line, out var ink);
        ink.Right += Spread(font, line, spacing);
        return ink;
    }

    /// How much wider letter spacing makes a line: the spacing once between each pair of characters.
    static float Spread(SKFont font, string line, float spacing) =>
        spacing == 0 || line.Length == 0 ? 0 : spacing * Math.Max(0, font.CountGlyphs(line) - 1);

    /// SKFont has no tracking, so each glyph goes where the font puts it, moved along by the spacing so far.
    static SKTextBlob? Spaced(SKFont font, string line, float spacing)
    {
        var positions = font.GetGlyphPositions(line);
        for (var i = 1; i < positions.Length; i++) positions[i].X += i * spacing;
        return SKTextBlob.CreatePositioned(line, font, positions);
    }
}
