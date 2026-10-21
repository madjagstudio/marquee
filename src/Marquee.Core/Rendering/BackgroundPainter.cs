using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

static class BackgroundPainter
{
    static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

    public static void Paint(RenderContext ctx, IReadOnlyList<BackgroundLayer> layers, SKRect area)
    {
        foreach (var layer in layers)
        {
            if (layer.Hidden) continue;

            using var paint = new SKPaint { IsAntialias = true, BlendMode = layer.Blend };
            switch (layer)
            {
                case FillLayer fill:
                    Paints.Apply(paint, fill.Paint, area, layer.Opacity);
                    ctx.Canvas.DrawRect(area, paint);
                    break;
                case ImageLayer image when ctx.Assets.Image(image.Path) is { } picture:
                    paint.Color = SKColors.White.WithAlpha(Paints.Alpha(layer.Opacity));
                    var (source, destination) = ImageFitting.Fit(image.Fit, picture.Width, picture.Height, area);
                    ctx.Canvas.DrawImage(picture, source, destination, Sampling, paint);
                    break;
                case ShaderLayer shader when ctx.Assets.Shader(shader) is { } effect:
                    ShaderPainter.Paint(ctx, shader, effect, area);
                    break;
                case VideoLayer video when ctx.Assets.Video(video) is { } player && player.FrameAt(ctx.Time) is { } frame:
                    // The player scaled the frame for the canvas, which is the area a background is painted on, so it
                    // goes on pixel for pixel. The frame stays valid until the next FrameAt, on the next render.
                    paint.Color = SKColors.White.WithAlpha(Paints.Alpha(layer.Opacity));
                    player.Draw(ctx.Canvas, frame, Sampling, paint);
                    break;
            }
        }
    }
}
