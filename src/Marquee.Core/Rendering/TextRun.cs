using System.Globalization;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// One run of text in one font: a text widget's line, a dial's or a donut's label, or one span of a line. It knows its
/// width and, when letter spacing is set or the font lacks some of its characters, holds its glyphs already placed, so
/// each layer TextInk draws reuses them. `culture` is the renderer's, whose regional format Windows chooses fonts for. The
/// caller disposes it; the font stays the caller's.
sealed class TextRun : IDisposable
{
    readonly float _spacing;
    readonly SKTextBlob? _placed;

    // Set when a face draws some of the run in colors of its own; the effect layers draw _uncolored, the run without
    // them, which is null when nothing else is left.
    readonly bool _hasColor;
    readonly SKTextBlob? _uncolored;

    public TextRun(string text, SKFont font, float spacing, CultureInfo culture)
    {
        Text = text;
        Font = font;
        _spacing = spacing;
        // A blob keeps its own copy of each piece's font, so the pieces can go once it is built.
        using var fallback = FallbackText.Of(font, text, culture);
        if (fallback is not null)
        {
            Width = fallback.Width(spacing);
            _placed = fallback.Blob(spacing);
            if (fallback.HasColor)
            {
                _hasColor = true;
                _uncolored = fallback.Blob(spacing, color: false);
            }
        }
        else
        {
            Width = TextPainter.Width(font, text, spacing, culture);
            _placed = spacing == 0 || text.Length == 0 ? null : Spaced(font, text, spacing);
        }
    }

    public string Text { get; }

    /// The run's font: its line height, baseline, underline and strikethrough are this font's, whatever faces draw
    /// characters it lacks.
    public SKFont Font { get; }

    /// The run's advance, letter spacing between its characters included: where a run after it starts, and how long its
    /// underline is.
    public float Width { get; }

    /// The letters with their baseline origin at (x, y). Text in the font alone without letter spacing is drawn as a
    /// plain string, exactly as text always was. An `effect` layer (a glow, shadow or outline) leaves out characters
    /// drawn in colors of their own, such as emoji, which would show as a colored copy rather than the effect.
    public void Draw(SKCanvas canvas, float x, float y, SKPaint paint, bool effect)
    {
        if (effect && _hasColor)
        {
            if (_uncolored is { } uncolored) canvas.DrawText(uncolored, x, y, paint);
            return;
        }

        if (_placed is { } blob) canvas.DrawText(blob, x, y, paint);
        else if (_spacing == 0) canvas.DrawText(Text, x, y, Font, paint);
    }

    public void Dispose()
    {
        _placed?.Dispose();
        _uncolored?.Dispose();
    }

    /// SKFont has no tracking, so each glyph goes where the font puts it, moved along by the spacing so far.
    static SKTextBlob? Spaced(SKFont font, string text, float spacing)
    {
        var positions = font.GetGlyphPositions(text);
        for (var i = 1; i < positions.Length; i++) positions[i].X += i * spacing;
        return SKTextBlob.CreatePositioned(text, font, positions);
    }
}
