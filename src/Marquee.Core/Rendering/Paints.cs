using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// PaintSpec → SKPaint. Gradients are laid across the area being painted.
static class Paints
{
    public static SKPaint Create(PaintSpec spec, SKRect area, float opacity = 1, SKPaintStyle style = SKPaintStyle.Fill)
    {
        var paint = new SKPaint { IsAntialias = true, Style = style };
        Apply(paint, spec, area, opacity);
        return paint;
    }

    public static void Apply(SKPaint paint, PaintSpec spec, SKRect area, float opacity = 1)
    {
        switch (spec)
        {
            case SolidPaint solid:
                paint.Color = solid.Color.WithAlpha((byte)Math.Round(solid.Color.Alpha * Math.Clamp(opacity, 0, 1)));
                break;
            case LinearGradientPaint linear:
                paint.Color = SKColors.White.WithAlpha(Alpha(opacity));
                paint.Shader = Linear(linear, area);
                break;
            case RadialGradientPaint radial:
                paint.Color = SKColors.White.WithAlpha(Alpha(opacity));
                paint.Shader = SKShader.CreateRadialGradient(
                    new SKPoint(area.MidX, area.MidY), Math.Max(area.Width, area.Height) / 2, radial.Colors, null, SKShaderTileMode.Clamp);
                break;
        }
    }

    public static byte Alpha(float opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);

    static SKShader Linear(LinearGradientPaint linear, SKRect area)
    {
        var radians = linear.Angle * MathF.PI / 180;
        var (dx, dy) = (MathF.Cos(radians), MathF.Sin(radians));
        // Half the length of the area's projection onto the gradient direction, so the colors span corner to corner.
        var half = MathF.Abs(area.Width / 2 * dx) + MathF.Abs(area.Height / 2 * dy);
        var start = new SKPoint(area.MidX - dx * half, area.MidY - dy * half);
        var end = new SKPoint(area.MidX + dx * half, area.MidY + dy * half);
        return SKShader.CreateLinearGradient(start, end, linear.Colors, null, SKShaderTileMode.Clamp);
    }
}
