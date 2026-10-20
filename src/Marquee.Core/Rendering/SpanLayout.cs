namespace Marquee.Core.Rendering;

/// Where a text widget's spans break into lines, how many lines its box shows and where their baselines go: the rules
/// TextLayout has for plain text, with a line made of pieces of several spans in several sizes. Kept apart from drawing
/// so the rules can be tested with any measure.
static class SpanLayout
{
    /// The part of one span (by its index) that is on one line.
    public readonly record struct Piece(int Span, string Text);

    /// A line's box: as tall as its largest span's size times the line height, and the tallest ascent (negative, as
    /// Skia gives it) and deepest descent of its spans' fonts.
    public readonly record struct LineBox(float Height, float Ascent, float Descent);

    /// The lines of the spans' texts. A `\n` in any span starts a new line, which the rest of the span and the spans after
    /// it continue. With `wrap`, a line wider than `width` breaks at spaces as plain text does, wherever the spans meet:
    /// a number and its unit with no space between them are one word, which stays whole. `measure` gives the width of
    /// some of a span's text, with letter spacing between its characters, and `spacing` a span's letter spacing, which
    /// also follows its last character when more text comes after it on the line. A blank line keeps the (empty) pieces
    /// of the spans it is in, so it still has their height; any other line holds only pieces with text.
    public static List<List<Piece>> Lines(IReadOnlyList<string> texts, float width, bool wrap, Func<int, string, float> measure,
        Func<int, float>? spacing = null)
    {
        float Measure(IEnumerable<Piece> pieces) => Width(pieces, measure, spacing ?? (_ => 0));

        var lines = new List<List<Piece>>();
        foreach (var paragraph in Paragraphs(texts))
        {
            var text = string.Concat(paragraph.Select(p => p.Text));
            if (text.Length == 0)
            {
                lines.Add(paragraph);
                continue;
            }

            if (!wrap || Measure(paragraph) <= width)
            {
                lines.Add(paragraph.Where(p => p.Text.Length > 0).ToList());
                continue;
            }

            foreach (var (start, end) in TextLayout.Breaks(text, width, (start, end) => Measure(Slice(paragraph, start, end))))
            {
                // Only a paragraph of nothing but spaces wraps to an empty line, which is blank as plain text's is.
                var line = Slice(paragraph, start, end);
                lines.Add(line.Count > 0 ? line : paragraph.Select(p => p with { Text = "" }).ToList());
            }
        }

        return lines;
    }

    /// The spans' texts split at their line breaks, each line a piece of every span on it, empty ones included. A `|`
    /// block scalar ends with a line break that `|-` doesn't; neither adds a blank line at the end, as for plain text.
    static List<List<Piece>> Paragraphs(IReadOnlyList<string> texts)
    {
        var paragraphs = new List<List<Piece>> { new() };
        for (var span = 0; span < texts.Count; span++)
        {
            var parts = texts[span].Replace("\r\n", "\n").Split('\n');
            paragraphs[^1].Add(new Piece(span, parts[0]));
            foreach (var part in parts.Skip(1))
                paragraphs.Add([new Piece(span, part)]);
        }

        while (paragraphs.Count > 1 && paragraphs[^1].All(p => p.Text.Length == 0))
            paragraphs.RemoveAt(paragraphs.Count - 1);
        return paragraphs;
    }

    /// The pieces holding characters `start` to `end` of the paragraph's text, leaving out the spans with none there.
    static List<Piece> Slice(List<Piece> paragraph, int start, int end)
    {
        var slice = new List<Piece>();
        var offset = 0;
        foreach (var piece in paragraph)
        {
            var (from, to) = (Math.Max(start, offset), Math.Min(end, offset + piece.Text.Length));
            if (from < to) slice.Add(piece with { Text = piece.Text[(from - offset)..(to - offset)] });
            offset += piece.Text.Length;
        }

        return slice;
    }

    /// A line's width: its pieces' widths, and each piece's letter spacing after it but the last's, as the spacing follows
    /// every character of one span's text but the last.
    public static float Width(IEnumerable<Piece> pieces, Func<int, string, float> measure, Func<int, float> spacing)
    {
        var shown = pieces.Where(p => p.Text.Length > 0).ToList();
        return shown.Sum(p => measure(p.Span, p.Text)) + shown.SkipLast(1).Sum(p => spacing(p.Span));
    }

    /// How many of the lines fit in `height`: as many as have their letters whole inside it, from the top of the first
    /// line's to the bottom of the last's. The first line always shows, as a single line always has, even in a box too
    /// short for it. With lines of one size this is TextLayout.Fitting.
    public static int Fitting(IReadOnlyList<LineBox> lines, float height)
    {
        var count = 1;
        var boxes = lines[0].Height;
        for (var i = 1; i < lines.Count; i++)
        {
            boxes += lines[i].Height;
            if (boxes - Room(lines[0]) - Room(lines[i]) > height) break;
            count = i + 1;
        }

        return count;
    }

    /// The baseline of each line, with the lines centred on `middle` top to bottom. The line boxes stack with no gaps,
    /// and each line's letters are centred in its box, as a single line's are in a widget's. With lines of one size this
    /// puts them exactly where plain text's go.
    public static float[] Baselines(IReadOnlyList<LineBox> lines, float middle)
    {
        var total = lines.Sum(l => l.Height);
        var baselines = new float[lines.Count];
        var above = 0f;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            baselines[i] = middle - (line.Ascent + line.Descent) / 2 - (total - line.Height) / 2 + above;
            above += line.Height;
        }

        return baselines;
    }

    /// The room in a line's box above its letters, and the same again below them.
    static float Room(LineBox line) => (line.Height - (line.Descent - line.Ascent)) / 2;
}
