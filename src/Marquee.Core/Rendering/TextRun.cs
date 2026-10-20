using SkiaSharp;

namespace Marquee.Core.Rendering;

/// One run of text in one font: a text widget's line, a dial's or a donut's label, or one span of a line. It knows its
/// width and, when letter spacing is set, holds its glyphs already spread out, so each layer TextInk draws reuses them.
/// The caller disposes it; the font stays the caller's.
sealed class TextRun : IDisposable
{
    readonly float _spacing;
    readonly SKTextBlob? _spaced;

    public TextRun(string text, SKFont font, float spacing)
    {
        Text = text;
        Font = font;
        _spacing = spacing;
        Width = TextPainter.Width(font, text, spacing);
        _spaced = spacing == 0 || text.Length == 0 ? null : Spaced(font, text, spacing);
    }

    public string Text { get; }

    public SKFont Font { get; }

    /// The run's advance, letter spacing between its characters included: where a run after it starts, and how long its
    /// underline is.
    public float Width { get; }

    /// The letters with their baseline origin at (x, y). Without letter spacing the run is drawn as a plain string,
    /// exactly as text always was.
    public void Draw(SKCanvas canvas, float x, float y, SKPaint paint)
    {
        if (_spacing == 0) canvas.DrawText(Text, x, y, Font, paint);
        else if (_spaced is { } blob) canvas.DrawText(blob, x, y, paint);
    }

    public void Dispose() => _spaced?.Dispose();

    /// SKFont has no tracking, so each glyph goes where the font puts it, moved along by the spacing so far.
    static SKTextBlob? Spaced(SKFont font, string text, float spacing)
    {
        var positions = font.GetGlyphPositions(text);
        for (var i = 1; i < positions.Length; i++) positions[i].X += i * spacing;
        return SKTextBlob.CreatePositioned(text, font, positions);
    }
}
