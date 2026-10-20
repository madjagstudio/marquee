using System.Globalization;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// A line whose font lacks some of its characters, in pieces by face (FontFallback), measured and drawn side by side on
/// the font's baseline as one line: letter spacing goes between every character, across the joins too. Glyphs are
/// placed one after another in the order the text is stored, as Skia's simple text drawing always placed them: Arabic
/// and Hebrew come out left to right, in their letters' separate forms, as there is no bidi reordering or shaping (that
/// would take HarfBuzz). The caller disposes it; the line's font stays the caller's.
sealed class FallbackText : IDisposable
{
    /// Some of the line's text and the font it is drawn in: the line's own, or one made for a fallback face. `Color` is
    /// set for a face that draws in colors of its own, such as an emoji font's.
    public readonly record struct Piece(string Text, SKFont Font, bool Color = false);

    readonly SKFont _font;

    FallbackText(SKFont font, List<Piece> pieces)
    {
        _font = font;
        Pieces = pieces;
    }

    public IReadOnlyList<Piece> Pieces { get; }

    /// Whether any piece draws in colors of its own.
    public bool HasColor => Pieces.Any(p => p.Color);

    /// The line in pieces, or null when it needs no fallback and is drawn with its font alone, exactly as text always was.
    /// `culture` is the renderer's, whose regional format Windows chooses fonts for.
    public static FallbackText? Of(SKFont font, string text, CultureInfo culture)
    {
        if (FontFallback.Pieces(font, text, culture) is not { } pieces) return null;

        var made = new List<Piece>(pieces.Count);
        try
        {
            foreach (var piece in pieces)
                made.Add(piece.Face is { } face
                    ? new Piece(text[piece.Start..piece.End], FontFallback.Font(font, face), FontFallback.HasColorGlyphs(face))
                    : new Piece(text[piece.Start..piece.End], font));
            return new FallbackText(font, made);
        }
        catch
        {
            foreach (var piece in made.Where(p => !ReferenceEquals(p.Font, font))) piece.Font.Dispose();
            throw;
        }
    }

    /// The line's advance plus `spacing` between each pair of characters, as TextPainter.Width measures a line in one font.
    public float Width(float spacing) =>
        Pieces.Sum(p => p.Font.MeasureText(p.Text)) + spacing * Math.Max(0, Pieces.Sum(p => p.Font.CountGlyphs(p.Text)) - 1);

    /// The box round the line's ink, from its baseline origin, with `spacing` between its characters, as TextPainter.Ink
    /// measures a line in one font: each piece's ink where the piece starts, and the last piece's spacing at the right.
    public SKRect Ink(float spacing)
    {
        var ink = SKRect.Empty;
        var (pen, glyphs, last) = (0f, 0, 0);
        foreach (var piece in Pieces)
        {
            var advance = piece.Font.MeasureText(piece.Text, out var bounds);
            if (!bounds.IsEmpty)
            {
                bounds.Offset(pen + glyphs * spacing, 0);
                ink = ink.IsEmpty ? bounds : SKRect.Union(ink, bounds);
            }

            last = piece.Font.CountGlyphs(piece.Text);
            pen += advance;
            glyphs += last;
        }

        ink.Right += spacing * Math.Max(0, last - 1);
        return ink;
    }

    /// The line's glyphs, a run for each piece, with the baseline origin at (0, 0). Without letter spacing each run is
    /// placed as Skia places a string, so a piece in the line's own font at its start draws exactly as that text alone.
    /// Without `color`, pieces that draw in colors of their own are left out, their room kept; null when nothing is left.
    public SKTextBlob? Blob(float spacing, bool color = true)
    {
        using var builder = new SKTextBlobBuilder();
        var (pen, index) = (0f, 0);
        foreach (var piece in Pieces)
        {
            var glyphs = piece.Font.GetGlyphs(piece.Text);
            var drawn = color || !piece.Color;
            if (drawn && spacing == 0)
            {
                builder.AddRun(glyphs, piece.Font, new SKPoint(pen, 0));
            }
            else if (drawn)
            {
                // SKFont has no tracking, so each glyph goes where the font puts it, moved along by the spacing so far.
                var positions = piece.Font.GetGlyphPositions(piece.Text, new SKPoint(pen, 0));
                for (var i = 0; i < positions.Length; i++) positions[i].X += (index + i) * spacing;
                builder.AddPositionedRun(glyphs, piece.Font, positions);
            }

            pen += piece.Font.MeasureText(piece.Text);
            index += glyphs.Length;
        }

        return builder.Build();
    }

    public void Dispose()
    {
        foreach (var piece in Pieces.Where(p => !ReferenceEquals(p.Font, _font))) piece.Font.Dispose();
    }
}
