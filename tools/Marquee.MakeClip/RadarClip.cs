using SkiaSharp;

namespace Marquee.MakeClip;

/// example-showcase's clip: a radar scope in the theme's colors, its beam sweeping once round while a few blips light up
/// as it passes and fade behind it. Everything that moves is a function of the beam's angle, which turns exactly once
/// over the clip, so the last frame leads straight back into the first and the loop has no seam.
static class RadarClip
{
    /// 16:10, the shape of the showcase's video box, so cover crops almost nothing; the box is smaller, so the player
    /// only ever scales the frames down.
    public const int Width = 320;
    public const int Height = 200;

    /// One slow turn of 6 seconds at the showcase's 30 fps.
    public const int Fps = 30;
    public const int Frames = 6 * Fps;

    /// At most about 375 KB for the 6 seconds, and ample for flat colors and soft edges at this size. The encoder spends
    /// less on frames this plain: the clip comes out at about 190 KB.
    public const int Bitrate = 500_000;

    static readonly SKColor Night = SKColor.Parse("#070A12");
    static readonly SKColor Navy = SKColor.Parse("#141B2E");
    static readonly SKColor Cyan = SKColor.Parse("#36D1DC");
    static readonly SKColor Blue = SKColor.Parse("#5B86E5");
    static readonly SKColor Hot = SKColor.Parse("#FF5A36");
    static readonly SKColor White = SKColor.Parse("#F4F6FA");

    static readonly SKPoint Center = new(Width / 2f, Height / 2f);
    const float Radius = 88;

    /// Each blip's bearing (degrees clockwise from 3 o'clock, as Skia measures), distance from the centre (a fraction
    /// of the scope's radius) and color.
    static readonly (float Bearing, float Distance, SKColor Color)[] Blips =
    [
        (-58, 0.62f, Cyan), (17, 0.38f, Cyan), (74, 0.81f, Blue), (128, 0.55f, Cyan), (203, 0.72f, Hot), (262, 0.27f, Blue),
    ];

    /// Paints frame `index` of the clip.
    public static void Draw(SKCanvas canvas, int index)
    {
        // The beam starts at 12 o'clock and turns clockwise.
        var beam = -90f + 360f * index / Frames;
        canvas.Clear(Night);
        using var paint = new SKPaint { IsAntialias = true };

        paint.Shader = SKShader.CreateRadialGradient(Center, Width * 0.6f, [Navy, Night], SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, Width, Height, paint);
        paint.Shader = null;

        // A faint grid across the whole frame, like a scope's graticule.
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = Cyan.WithAlpha(0x12);
        for (var x = Center.X % 20; x < Width; x += 20) canvas.DrawLine(x, 0, x, Height, paint);
        for (var y = Center.Y % 20; y < Height; y += 20) canvas.DrawLine(0, y, Width, y, paint);

        // The scope: rings and a crosshair.
        paint.StrokeWidth = 1.5f;
        paint.Color = Cyan.WithAlpha(0x40);
        for (var ring = 1; ring <= 4; ring++) canvas.DrawCircle(Center, Radius * ring / 4, paint);
        paint.Color = Cyan.WithAlpha(0x28);
        canvas.DrawLine(Center.X - Radius, Center.Y, Center.X + Radius, Center.Y, paint);
        canvas.DrawLine(Center.X, Center.Y - Radius, Center.X, Center.Y + Radius, paint);

        // The sweep: a wedge that fades out over the 144° behind the beam. A sweep gradient runs clockwise from 3 o'clock,
        // so turning it by the beam's angle puts its bright end on the beam.
        paint.Style = SKPaintStyle.Fill;
        // A shader's colors are multiplied by the paint's alpha, so the paint goes back to opaque first.
        paint.Color = SKColors.White;
        paint.Shader = SKShader.CreateSweepGradient(Center,
            [Cyan.WithAlpha(0), Cyan.WithAlpha(0), Blue.WithAlpha(0x48), Cyan.WithAlpha(0xA8)], [0f, 0.6f, 0.88f, 1f]);
        canvas.Save();
        canvas.RotateDegrees(beam, Center.X, Center.Y);
        canvas.DrawCircle(Center, Radius, paint);
        canvas.Restore();
        paint.Shader = null;

        // Blips flare as the beam crosses them and fade over the next half turn, never quite out.
        using var glow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
        foreach (var (bearing, distance, color) in Blips)
        {
            var since = ((beam - bearing) % 360 + 360) % 360;
            var strength = 0.15f + 0.85f * MathF.Exp(-since / 70);
            var at = Point(bearing, Radius * distance);
            paint.MaskFilter = glow;
            paint.Color = color.WithAlpha((byte)(200 * strength));
            canvas.DrawCircle(at, 7, paint);
            paint.MaskFilter = null;
            paint.Color = White.WithAlpha((byte)(255 * strength));
            canvas.DrawCircle(at, 2.5f, paint);
        }

        // The beam itself, over everything.
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2;
        paint.StrokeCap = SKStrokeCap.Round;
        paint.Color = White.WithAlpha(0xE0);
        canvas.DrawLine(Center, Point(beam, Radius), paint);
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawCircle(Center, 3, paint);
    }

    static SKPoint Point(float bearing, float distance)
    {
        var radians = bearing * MathF.PI / 180;
        return new SKPoint(Center.X + distance * MathF.Cos(radians), Center.Y + distance * MathF.Sin(radians));
    }
}
