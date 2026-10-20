using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// The layers text is drawn in, back to front. Text with several runs draws one layer for all of them before the next
/// layer, so a glow never paints over the line above it and an outline never over a neighbouring letter's fill.
enum TextLayer { Glow, Shadow, Outline, Letters }

/// How one style inks letters this frame: the glow and shadow behind them, the outline round them (`text_stroke`) and
/// their fill (`text_fill`, else `color`), with gradients spread across `area`, the widget's box; and the lines under
/// and through them (`underline`, `strike`), which every layer draws as part of the letters. Text and value widgets and
/// a dial's and a donut's labels all draw with it, so a style's letters look the same wherever they are. Made once per
/// style each frame, and disposed after.
sealed class TextInk : IDisposable
{
    /// Every layer, back to front.
    public static readonly IReadOnlyList<TextLayer> Layers = [TextLayer.Glow, TextLayer.Shadow, TextLayer.Outline, TextLayer.Letters];

    // Skia's own measures for a font whose metrics give no underline or strikeout, as fractions of its size.
    const float FallbackThickness = 1f / 18;
    const float FallbackUnderline = 1f / 9;
    const float FallbackStrike = -6f / 21;

    readonly SKMaskFilter? _glowBlur, _shadowBlur;
    readonly SKPaint? _glow, _shadow, _outline;
    readonly SKPaint _letters;
    readonly float _shadowDx, _shadowDy;
    readonly bool _underline, _strike;

    /// `effects` is false for text printed flat on a face, as a dial's and a donut's labels are, which takes no glow or
    /// shadow.
    public TextInk(StyleSpec style, SKRect area, TimeSpan time, bool effects = true)
    {
        var outline = OutlineWidth(style);

        if (effects && Effect.Glow(style, time) is { } glow)
        {
            _glowBlur = glow.Blur();
            _glow = Thickened(glow.Paint(_glowBlur), outline);
        }

        if (effects && Effect.Shadow(style) is { } shadow)
        {
            _shadowBlur = shadow.Blur();
            _shadow = Thickened(shadow.Paint(_shadowBlur), outline);
            (_shadowDx, _shadowDy) = (shadow.Dx, shadow.Dy);
        }

        if (outline > 0)
        {
            _outline = Paints.Create(style.TextStroke!.Paint!, area, style: SKPaintStyle.Stroke);
            // Half the outline's stroke falls inside the letters, under their fill, so it is twice the width that shows.
            _outline.StrokeWidth = outline * 2;
            _outline.StrokeJoin = SKStrokeJoin.Round;
        }

        _letters = Paints.Create(style.TextFill ?? new SolidPaint(style.Color ?? StyleDefaults.Color), area);
        _underline = style.Underline ?? StyleDefaults.Underline;
        _strike = style.Strike ?? StyleDefaults.Strike;
    }

    /// How far the style's outline (`text_stroke`) reaches beyond its letters: its width, or 0 when it has none. A dial's
    /// and a donut's labels make room for it.
    public static float OutlineWidth(StyleSpec style) =>
        style.TextStroke is { Paint: not null } stroke ? stroke.Width ?? StyleDefaults.TextStrokeWidth : 0;

    /// Every layer of one run, back to front, with its baseline origin at (x, y): a label, or a run alone.
    public void Draw(SKCanvas canvas, TextRun run, float x, float y)
    {
        foreach (var layer in Layers) Draw(canvas, layer, run, x, y);
    }

    /// One layer of one run, with its baseline origin at (x, y). A layer the style doesn't have draws nothing.
    public void Draw(SKCanvas canvas, TextLayer layer, TextRun run, float x, float y)
    {
        var paint = layer switch
        {
            TextLayer.Glow => _glow,
            TextLayer.Shadow => _shadow,
            TextLayer.Outline => _outline,
            _ => _letters,
        };
        if (paint is null || run.Text.Length == 0) return;
        if (layer == TextLayer.Shadow) (x, y) = (x + _shadowDx, y + _shadowDy);

        run.Draw(canvas, x, y, paint, effect: layer != TextLayer.Letters);
        if (_underline) canvas.DrawRect(Underline(run, x, y), paint);
        if (_strike) canvas.DrawRect(Strike(run, x, y), paint);
    }

    public void Dispose()
    {
        _glow?.Dispose();
        _shadow?.Dispose();
        _outline?.Dispose();
        _letters.Dispose();
        _glowBlur?.Dispose();
        _shadowBlur?.Dispose();
    }

    /// An outlined run's glow and shadow take the outline's shape, not just the letters inside it.
    static SKPaint Thickened(SKPaint paint, float outline)
    {
        if (outline <= 0) return paint;
        paint.Style = SKPaintStyle.StrokeAndFill;
        paint.StrokeWidth = outline * 2;
        paint.StrokeJoin = SKStrokeJoin.Round;
        return paint;
    }

    /// The underline across the run's whole width, letter spacing included, where and as thick as its font says: Skia
    /// gives the top of the line, below the baseline.
    static SKRect Underline(TextRun run, float x, float y)
    {
        var (size, metrics) = (run.Font.Size, run.Font.Metrics);
        var thickness = metrics.UnderlineThickness is { } t and > 0 ? t : size * FallbackThickness;
        var top = y + (metrics.UnderlinePosition ?? size * FallbackUnderline);
        return new SKRect(x, top, x + run.Width, top + thickness);
    }

    /// The strikethrough, as the underline is: Skia gives the bottom of the line, above the baseline.
    static SKRect Strike(TextRun run, float x, float y)
    {
        var (size, metrics) = (run.Font.Size, run.Font.Metrics);
        var thickness = metrics.StrikeoutThickness is { } t and > 0 ? t : size * FallbackThickness;
        var bottom = y + (metrics.StrikeoutPosition ?? size * FallbackStrike);
        return new SKRect(x, bottom - thickness, x + run.Width, bottom);
    }
}
