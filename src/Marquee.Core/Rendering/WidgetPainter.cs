using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Works out a widget's value, staleness and style for this frame, then hands it to the painter for its type.
static class WidgetPainter
{
    static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

    public static void Paint(RenderContext ctx, WidgetSpec widget, StyleSpec inherited)
    {
        if (widget.Hidden) return;

        var subject = widget.Source?.Evaluate(ctx.Sensors);
        var text = widget is TextWidget t ? t.Text.Render(ctx.Sensors, ctx.Locale) : (TemplateResult?)null;
        var spans = Spans(ctx, widget, subject);
        var stale = (widget.Source is not null && subject is null) || text is { Stale: true } || spans?.Any(span => span.Stale) == true;
        var style = StyleEvaluator.Effective(widget, inherited, subject, stale);

        var opacity = StyleEvaluator.Opacity(style, ctx.Time);
        if (opacity <= 0) return;

        var canvas = ctx.Canvas;
        var saved = canvas.Save();
        if (widget.Rotate != 0)
            canvas.RotateDegrees(widget.Rotate, widget.Bounds.MidX, widget.Bounds.MidY);

        // A partly transparent or blended widget is drawn on a layer of its own, then composited onto what's under it
        // with its opacity and blend mode; any other widget draws straight onto the canvas.
        var blend = style.Blend ?? StyleDefaults.Blend;
        if (opacity < 1 || blend != StyleDefaults.Blend)
        {
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha(Paints.Alpha(opacity)), BlendMode = blend };
            canvas.SaveLayer(layer);
        }

        try
        {
            switch (widget)
            {
                case TextWidget:
                    TextPainter.Paint(ctx, widget.Bounds, style, text!.Value.Text);
                    break;
                case SpansWidget or ValueWidget { Spans: not null }:
                    SpanPainter.Paint(ctx, widget.Bounds, style, spans!.Select((span, i) =>
                        new SpanPainter.Span(span.Text, StyleEvaluator.Effective(widget, inherited, subject, stale, SpansOf(widget)![i].Style))).ToList());
                    break;
                case ValueWidget value:
                    TextPainter.Paint(ctx, widget.Bounds, style, ValueText(ctx, value, subject, stale));
                    break;
                case BarWidget bar:
                    ShapePainters.Bar(ctx, bar, style, Number(ctx, bar, subject, stale));
                    break;
                case GaugeWidget gauge:
                    DialPainter.Paint(ctx, gauge, style, Number(ctx, gauge, subject, stale));
                    break;
                case GraphWidget graph:
                    GraphPainter.Paint(ctx, graph, style);
                    break;
                case DonutWidget donut:
                    DonutPainter.Paint(ctx, donut, style,
                        donut.Slices.Select(s => StyleEvaluator.Effective(widget, inherited, subject, stale, s.Style)).ToList());
                    break;
                case ImageWidget image:
                    Image(ctx, image, style);
                    break;
                case VideoWidget video:
                    VideoWidgetPainter.Paint(ctx, video, style);
                    break;
                case ShapeWidget shape:
                    ShapeWidgetPainter.Paint(ctx, shape, style);
                    break;
                case GroupWidget group:
                    Group(ctx, group, style);
                    break;
            }
        }
        finally
        {
            canvas.RestoreToCount(saved);
        }
    }

    static string ValueText(RenderContext ctx, ValueWidget widget, SensorValue? subject, bool stale)
    {
        if (stale || subject is not { } value) return Formatting.Missing;
        return Formatting.Apply(widget.Format, Smoothed(ctx, widget, value), ctx.Locale);
    }

    /// A text widget's spans, or a value widget's.
    static IReadOnlyList<TextSpan>? SpansOf(WidgetSpec widget) => widget switch
    {
        SpansWidget s => s.Spans,
        ValueWidget v => v.Spans,
        _ => null,
    };

    /// The spans' texts this frame; null for a widget without spans. In a value widget's, `{0}` is its value after
    /// `smooth`. While it has no value, it shows a single dash where its first span goes, not a dash beside a unit.
    static List<TemplateResult>? Spans(RenderContext ctx, WidgetSpec widget, SensorValue? subject)
    {
        switch (widget)
        {
            case SpansWidget s:
                return s.Spans.Select(span => span.Text.Render(ctx.Sensors, ctx.Locale)).ToList();
            case ValueWidget { Spans: { } spans }:
                if (subject is not { } value) return [new TemplateResult(Formatting.Missing, Stale: true)];
                var own = Smoothed(ctx, widget, value);
                return spans.Select(span => span.Text.Render(ctx.Sensors, ctx.Locale, own)).ToList();
            default:
                return null;
        }
    }

    /// A number eased over the widget's `smooth`; any other value as it is.
    static SensorValue Smoothed(RenderContext ctx, WidgetSpec widget, SensorValue value) =>
        value.Number is { } n ? SensorValue.Of(ctx.Smoothed(widget, n)) : value;

    static double? Number(RenderContext ctx, WidgetSpec widget, SensorValue? subject, bool stale) =>
        !stale && subject?.Number is { } n ? ctx.Smoothed(widget, n) : null;

    static void Image(RenderContext ctx, ImageWidget image, StyleSpec style)
    {
        BoxPainter.Shadow(ctx.Canvas, image.Bounds, style);
        BoxPainter.Background(ctx, image.Bounds, style);
        if (ctx.Assets.Image(image.Path) is { } picture)
        {
            var (source, destination) = ImageFitting.Fit(image.Fit, picture.Width, picture.Height, image.Bounds);
            BoxPainter.Clip(ctx.Canvas, image.Bounds, style);
            using var paint = new SKPaint { IsAntialias = true };
            ctx.Canvas.DrawImage(picture, source, destination, Sampling, paint);
            ctx.Canvas.Restore();
        }

        BoxPainter.Outline(ctx, image.Bounds, style);
    }

    /// Children are positioned relative to the group and clipped to it, and inherit its text properties.
    static void Group(RenderContext ctx, GroupWidget group, StyleSpec style)
    {
        var bounds = group.Bounds;
        BoxPainter.Background(ctx, bounds, style);
        BoxPainter.Clip(ctx.Canvas, bounds, style);
        ctx.Canvas.Translate(bounds.Left, bounds.Top);
        var inherited = style.Inherited();
        foreach (var child in group.Children)
            Paint(ctx, child, inherited);
        ctx.Canvas.Restore();
        BoxPainter.Outline(ctx, bounds, style);
    }
}
