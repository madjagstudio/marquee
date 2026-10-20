using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// The video widget, drawn as an image widget is: the box's shadow, its fill, the clip's current frame clipped to the box
/// and its radius, then its stroke. WidgetPainter has already applied its rotation, opacity and blend. A clip that was
/// skipped leaves the box alone.
static class VideoWidgetPainter
{
    static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

    public static void Paint(RenderContext ctx, VideoWidget video, StyleSpec style)
    {
        var bounds = video.Bounds;
        BoxPainter.Shadow(ctx.Canvas, bounds, style);
        BoxPainter.Background(ctx, bounds, style);
        // FrameAt runs once per frame for each player, which is what keeps the frame it returns valid while it is drawn.
        if (ctx.Assets.Video(video) is { } player && player.FrameAt(ctx.Time) is { } frame)
        {
            // The player scaled the frame to the part of the box it covers, in the coordinates the widget is drawn in,
            // so it goes on unscaled unless the widget is rotated.
            BoxPainter.Clip(ctx.Canvas, bounds, style);
            using var paint = new SKPaint { IsAntialias = true };
            ctx.Canvas.DrawImage(frame, player.Destination.Left, player.Destination.Top, Sampling, paint);
            ctx.Canvas.Restore();
        }

        BoxPainter.Outline(ctx, bounds, style);
    }
}
