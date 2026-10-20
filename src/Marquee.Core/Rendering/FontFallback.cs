using System.Collections.Concurrent;
using System.Globalization;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Which face draws each character of a line: the style's font wherever it has the glyph, and otherwise a font Windows
/// offers for that character (Skia's character matcher, which asks DirectWrite's font fallback), so a Japanese weekday
/// or a Korean label shows its letters rather than empty boxes. The fallback keeps the weight and slant the style's own
/// letters show. Windows chooses among fonts for the renderer's regional format, the PC's unless a test pins another,
/// as the time helper's names follow it: Chinese characters take Chinese or Japanese forms as the PC is set up.
static class FontFallback
{
    /// The characters from `Start` to `End` of a line, drawn in `Face`: null for the line's own font.
    public readonly record struct Piece(int Start, int End, SKTypeface? Face);

    /// The face found for each character, by the family, weight, slant and culture asked for, so the matcher runs once
    /// for each character rather than every frame; null when Windows has no font with it. Shared by every theme and
    /// renderer, as the faces belong to the font manager, as installed fonts' do in ThemeAssets. It holds no more than the
    /// distinct characters themes have drawn. It is kept for as long as Marquee runs, so a font installed meanwhile is used
    /// after a restart.
    static readonly ConcurrentDictionary<(string Family, int Weight, bool Italic, string Culture, int Codepoint), SKTypeface?> Matches =
        new();

    /// Whether each fallback face found draws in colors of its own, as HasColorGlyphs says.
    static readonly ConcurrentDictionary<SKTypeface, bool> ColorFaces = new();

    /// The tables a face keeps color glyphs in: layered vector glyphs (COLR), and bitmaps from Google (CBDT) and Apple
    /// (sbix).
    static readonly uint[] ColorTables = [Tag("COLR"), Tag("CBDT"), Tag("sbix")];

    /// The line in pieces by face, or null when the font has every character of it or Windows has none it lacks, so the
    /// line is drawn exactly as it always was: with the font alone, and an empty box for a character no font has. A
    /// letter with its combining marks, or a character written as a surrogate pair, stays in one piece, and a word in
    /// one face where one has all its letters, rather than changing typeface partway through. `culture` is the
    /// renderer's, whose regional format Windows chooses fonts for.
    public static List<Piece>? Pieces(SKFont font, string text, CultureInfo culture)
    {
        if (text.Length == 0 || font.ContainsGlyphs(text) || font.Typeface is not { } own) return null;

        var (weight, italic) = Asked(font, own);
        var pieces = new List<Piece>();
        for (var i = 0; i < text.Length;)
        {
            var length = StringInfo.GetNextTextElementLength(text, i);
            var face = Face(own, weight, italic, culture, text.AsSpan(i, length), pieces.Count > 0 ? pieces[^1].Face : null);
            var start = i;
            // The letters before this one in another fallback face move to this one's when it has them all too.
            if (face is not null && pieces.Count > 0 && pieces[^1].Face is { } before && !ReferenceEquals(before, face)
                && face.ContainsGlyphs(text.AsSpan(pieces[^1].Start, pieces[^1].End - pieces[^1].Start)))
            {
                start = pieces[^1].Start;
                pieces.RemoveAt(pieces.Count - 1);
            }

            if (pieces.Count > 0 && ReferenceEquals(pieces[^1].Face, face))
                pieces[^1] = pieces[^1] with { End = i + length };
            else
                pieces.Add(new Piece(start, i + length, face));
            i += length;
        }

        return pieces.All(p => p.Face is null) ? null : pieces;
    }

    /// A font like `font` in `face`: its size, width and edging, made bold when the font's letters are and the face
    /// isn't, and slanted when they are italic and the face is upright, as TextPainter.Font does for a style's own face.
    /// The caller disposes it.
    public static SKFont Font(SKFont font, SKTypeface face)
    {
        var (weight, italic) = Asked(font, font.Typeface ?? face);
        return new SKFont(face, font.Size, font.ScaleX)
        {
            Edging = font.Edging,
            Subpixel = font.Subpixel,
            Hinting = font.Hinting,
            Embolden = weight >= 600 && face.FontWeight < 600,
            SkewX = italic && face.FontStyle.Slant == SKFontStyleSlant.Upright ? TextPainter.FauxItalicSkew : 0,
        };
    }

    /// Whether a face draws its glyphs in colors of its own, as Segoe UI Emoji does: a style's `color` and `text_fill`
    /// don't change them, and a glow, shadow or outline would be a colored copy, so those layers leave them out.
    public static bool HasColorGlyphs(SKTypeface face) =>
        ColorFaces.GetOrAdd(face, static face =>
        {
            try
            {
                return face.GetTableTags().Any(ColorTables.Contains);
            }
            catch (Exception)
            {
                // Called while drawing, as Match is: a face whose tables can't be read draws as letters do.
                return false;
            }
        });

    static uint Tag(string name) => (uint)(name[0] << 24 | name[1] << 16 | name[2] << 8 | name[3]);

    /// The weight and slant of the font's letters as drawn: its face's, made bold (700) when it is emboldened, and italic
    /// when its face is or it is slanted. A style's weight between two faces' gets the face it is drawn in, so the
    /// fallback matches the letters beside it.
    static (int Weight, bool Italic) Asked(SKFont font, SKTypeface own) =>
        (font.Embolden ? Math.Max(own.FontWeight, (int)SKFontStyleWeight.Bold) : own.FontWeight,
            font.SkewX != 0 || own.FontStyle.Slant != SKFontStyleSlant.Upright);

    /// The face for one letter and its marks: null when the font has them all; else `previous`, the face of the letters
    /// before it, when it has the whole letter, so a word keeps to one face; else the first face found for a character
    /// it lacks that has the whole letter, or failing that the first face found for any of them, so the base letter
    /// shows even if a mark can't; null when none is found.
    static SKTypeface? Face(SKTypeface own, int weight, bool italic, CultureInfo culture, ReadOnlySpan<char> letter,
        SKTypeface? previous)
    {
        if (own.ContainsGlyphs(letter)) return null;
        if (previous is not null && previous.ContainsGlyphs(letter)) return previous;

        SKTypeface? first = null;
        foreach (var rune in letter.EnumerateRunes())
        {
            if (own.ContainsGlyph(rune.Value)) continue;
            if (Match(own.FamilyName, weight, italic, culture, rune.Value) is not { } face) continue;
            if (face.ContainsGlyphs(letter)) return face;
            first ??= face;
        }

        return first;
    }

    /// The face Windows offers for one character in a font like `family` at `weight`, upright or italic, or null when it
    /// has none. The culture goes to Windows by its name (a BCP 47 tag, such as ja-JP). The invariant culture has none, so
    /// Windows takes the PC's own, as it does for an app that names none.
    public static SKTypeface? Match(string family, int weight, bool italic, CultureInfo culture, int codepoint) =>
        Matches.GetOrAdd((family, weight, italic, culture.Name, codepoint), static key =>
        {
            try
            {
                var slant = key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                string[]? bcp47 = key.Culture.Length > 0 ? [key.Culture] : null;
                var face = SKFontManager.Default.MatchCharacter(key.Family, key.Weight, (int)SKFontStyleWidth.Normal, slant, bcp47,
                    key.Codepoint);
                return face is not null && face.ContainsGlyph(key.Codepoint) ? face : null;
            }
            catch (Exception)
            {
                // Called while drawing: a font manager failure leaves the character as it was, not the frame failed.
                return null;
            }
        });
}
