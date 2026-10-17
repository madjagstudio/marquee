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
            }
        }
    }
}
