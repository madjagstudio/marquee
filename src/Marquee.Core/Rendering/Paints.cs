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
                paint.Shader = Radial(radial, area);
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
        return SKShader.CreateLinearGradient(start, end, linear.Colors, linear.Stops, SKShaderTileMode.Clamp);
    }

    static SKShader Radial(RadialGradientPaint radial, SKRect area)
    {
        // With the default centre and radius these are exactly the area's middle and half its longer side, as before
        // they could move: halving and multiplying by 0.5 round the same way.
        var center = new SKPoint(area.Left + area.Width * radial.Center.X, area.Top + area.Height * radial.Center.Y);
        var radius = Math.Max(area.Width, area.Height) / 2 * radial.Radius;
        return SKShader.CreateRadialGradient(center, radius, radial.Colors, radial.Stops, SKShaderTileMode.Clamp);
    }
}
