using System.Globalization;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// A text widget's spans: the box in the widget's style, then each span's text in its own font, case and letter
/// effects, side by side on each line's baseline. The widget's style places the text as it places plain text: its
/// padding, `wrap`, `line_height` and `align`, with the block of lines centred vertically (SpanLayout). Every span draws
/// one TextInk layer before any span draws the next, so a glow never paints over a neighbouring span's letters.
static class SpanPainter
{
    /// One span this frame: its text, filled in, and its style, with the widget's under it and the rules over it.
    public readonly record struct Span(string Text, StyleSpec Style);

    /// A piece of a span ready to draw: its run, the span's ink and its baseline origin.
    readonly record struct Placed(TextRun Run, TextInk Ink, float X, float Y);

    public static void Paint(RenderContext ctx, SKRect bounds, StyleSpec style, IReadOnlyList<Span> spans)
    {
        BoxPainter.Paint(ctx, bounds, style);

        var fonts = new List<SKFont>(spans.Count);
        var inks = new List<TextInk>(spans.Count);
        var placed = new List<Placed>();
        try
        {
            foreach (var span in spans)
            {
                fonts.Add(TextPainter.Font(ctx, span.Style));
                inks.Add(new TextInk(span.Style, bounds, ctx.Time));
            }

            Layout(spans, fonts, inks, style, bounds, ctx.Locale.Culture, placed);
            foreach (var layer in TextInk.Layers)
                foreach (var piece in placed)
                    piece.Ink.Draw(ctx.Canvas, layer, piece.Run, piece.X, piece.Y);
        }
        finally
        {
            foreach (var piece in placed) piece.Run.Dispose();
            foreach (var ink in inks) ink.Dispose();
            foreach (var font in fonts) font.Dispose();
        }
    }

    static void Layout(IReadOnlyList<Span> spans, List<SKFont> fonts, List<TextInk> inks, StyleSpec style, SKRect bounds,
        CultureInfo culture, List<Placed> placed)
    {
        var padding = style.Padding ?? StyleDefaults.Padding;
        var content = SKRect.Inflate(bounds, -padding, -padding);
        var lineHeight = style.LineHeight ?? StyleDefaults.LineHeight;
        float Spacing(int span) => spans[span].Style.LetterSpacing ?? StyleDefaults.LetterSpacing;

        // Each span is cased after the text of the spans before it, so title case doesn't start a word at a join.
        var texts = spans.Select((span, i) => TextCase.Apply(span.Text, span.Style.Case, culture,
            string.Concat(spans.Take(i).Select(s => s.Text)))).ToList();
        var lines = SpanLayout.Lines(texts, content.Width, style.Wrap ?? StyleDefaults.Wrap,
            (span, text) => TextPainter.Width(fonts[span], text, Spacing(span)), Spacing);

        var boxes = lines.Select(line => new SpanLayout.LineBox(
            line.Max(p => fonts[p.Span].Size) * lineHeight,
            line.Min(p => fonts[p.Span].Metrics.Ascent),
            line.Max(p => fonts[p.Span].Metrics.Descent))).ToList();
        var count = SpanLayout.Fitting(boxes, content.Height);
        var baselines = SpanLayout.Baselines(boxes.GetRange(0, count), content.MidY);

        for (var i = 0; i < count; i++)
        {
            // Each run is added as it is made, so it is disposed whatever happens next; it moves into place once the
            // line's width is known. A run's letter spacing also follows its last character when another run comes
            // after it, so spans with no style of their own space their letters exactly as the same text in one does.
            var pieces = lines[i].Where(p => p.Text.Length > 0).ToList();
            var first = placed.Count;
            foreach (var piece in pieces)
                placed.Add(new Placed(new TextRun(piece.Text, fonts[piece.Span], Spacing(piece.Span)), inks[piece.Span], 0, baselines[i]));

            var width = 0f;
            for (var j = 0; j < pieces.Count; j++)
                width += placed[first + j].Run.Width + (j < pieces.Count - 1 ? Spacing(pieces[j].Span) : 0);
            var x = (style.Align ?? StyleDefaults.Align) switch
            {
                TextAlign.Center => content.MidX - width / 2,
                TextAlign.Right => content.Right - width,
                _ => content.Left,
            };
            for (var j = 0; j < pieces.Count; j++)
            {
                placed[first + j] = placed[first + j] with { X = x };
                x += placed[first + j].Run.Width + Spacing(pieces[j].Span);
            }
        }
    }
}
