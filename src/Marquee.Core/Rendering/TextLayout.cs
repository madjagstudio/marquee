namespace Marquee.Core.Rendering;

/// Where a text or value widget's text breaks into lines, and how many of them its box shows. Kept apart from drawing
/// so the rules can be tested with any measure.
static class TextLayout
{
    /// The lines of `text`, breaking at `\n` and, with `wrap`, at spaces where a line is wider than `width`. Without
    /// `wrap` a line too wide overflows its box, as text always has. A line that fits is kept exactly as written, spaces
    /// and all, so text that fits on one line draws as it always has. A word wider than the line gets a line of its own
    /// and overflows it rather than being broken: a value such as 100°C split across two lines would misread.
    public static List<string> Lines(string text, float width, bool wrap, Func<string, float> measure)
    {
        // Most text is one line, which needs no splitting.
        if (!text.Contains('\n') && (!wrap || measure(text) <= width)) return [text];

        // A `|` block scalar ends with a line break that `|-` doesn't; neither should add a blank line.
        var paragraphs = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (!wrap || measure(paragraph) <= width) lines.Add(paragraph);
            else Wrap(paragraph, width, measure, lines);
        }

        return lines;
    }

    static void Wrap(string paragraph, float width, Func<string, float> measure, List<string> lines)
    {
        foreach (var (start, end) in Breaks(paragraph, width, (start, end) => measure(paragraph[start..end])))
            lines.Add(paragraph[start..end]);
    }

    /// Where a paragraph too wide for `width` breaks: each line as the start and end of the part of the paragraph it
    /// holds, with `measure` giving the width of any part. Spans wrap with it too, measuring a part across their fonts.
    /// Greedy: each word goes on the current line if the line, with the spaces before the word, still fits. The spaces
    /// at a break are dropped, so they don't push a centred or right-aligned line off its mark.
    public static List<(int Start, int End)> Breaks(string paragraph, float width, Func<int, int, float> measure)
    {
        var lines = new List<(int Start, int End)>();
        int? lineStart = null;
        var lineEnd = 0;
        var start = 0;
        while (start < paragraph.Length)
        {
            var wordStart = start;
            while (wordStart < paragraph.Length && paragraph[wordStart] == ' ') wordStart++;
            if (wordStart == paragraph.Length) break;
            var wordEnd = paragraph.IndexOf(' ', wordStart);
            if (wordEnd < 0) wordEnd = paragraph.Length;

            // The word goes on with the spaces before it. The first line keeps the spaces it starts with, as indentation.
            if (lineStart is null) lineStart = start;
            else if (measure(lineStart.Value, wordEnd) > width)
            {
                lines.Add((lineStart.Value, lineEnd));
                lineStart = wordStart;
            }

            lineEnd = wordEnd;
            start = wordEnd;
        }

        lines.Add((lineStart ?? 0, lineEnd));
        return lines;
    }

    /// How many of `count` lines fit in `height`, where lines are `advance` apart and each is `extent` tall (ascent
    /// plus descent). The first line always shows, as a single line always has, even in a box too short for it; the
    /// lines after the last that fits whole are left out rather than cut through.
    public static int Fitting(int count, float advance, float extent, float height)
    {
        if (count <= 1 || (count - 1) * advance + extent <= height) return count;
        var fit = (int)Math.Floor((height - extent) / advance) + 1;
        return Math.Clamp(fit, 1, count);
    }
}
