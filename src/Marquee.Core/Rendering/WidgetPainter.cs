using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Works out a widget's value, staleness and style for this frame, then hands it to the painter for its type.
static class WidgetPainter
{
    public static void Paint(RenderContext ctx, WidgetSpec widget, StyleSpec inherited)
    {
        var subject = widget.Source?.Evaluate(ctx.Sensors);
        var text = widget is TextWidget t ? t.Text.Render(ctx.Sensors) : (TemplateResult?)null;
        var stale = (widget.Source is not null && subject is null) || text is { Stale: true };
        var style = StyleEvaluator.Effective(widget, inherited, subject, stale);

        var opacity = StyleEvaluator.Opacity(style, ctx.Time);
        if (opacity <= 0) return;

        var canvas = ctx.Canvas;
        int saved;
        if (opacity < 1)
        {
            using var layer = new SKPaint { Color = SKColors.White.WithAlpha(Paints.Alpha(opacity)) };
            saved = canvas.SaveLayer(layer);
        }
        else
        {
            saved = canvas.Save();
        }

        try
        {
            switch (widget)
            {
                case TextWidget:
                    TextPainter.Paint(ctx, widget.Bounds, style, text!.Value.Text);
                    break;
                case ValueWidget value:
                    TextPainter.Paint(ctx, widget.Bounds, style, ValueText(ctx, value, subject, stale));
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
        if (value.Number is { } n) value = SensorValue.Of(ctx.Smoothed(widget, n));
        return Formatting.Apply(widget.Format, value);
    }
}
