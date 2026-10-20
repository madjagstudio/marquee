using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// A style's glow or shadow: its color, its blur, and how far it is moved. Every painter takes them from here, so text,
/// shapes, needles and donut slices agree: a blur's sigma is half its radius, a glow sits where its shape does, and a
/// shadow is moved 2 px right and down unless its style says otherwise.
readonly record struct Effect(SKColor Color, float Sigma, float Dx, float Dy)
{
    /// The style's glow this frame, which a pulse can grow and shrink; null when it has none or its radius is 0.
    public static Effect? Glow(StyleSpec style, TimeSpan time) =>
        style.Glow is { Color: { } color } && StyleEvaluator.GlowRadius(style, time) is var radius and > 0
            ? new Effect(color, radius / 2, 0, 0)
            : null;

    /// The style's shadow; null when it has none.
    public static Effect? Shadow(StyleSpec style) =>
        style.Shadow is { Color: { } color } shadow
            ? new Effect(color, (shadow.Blur ?? StyleDefaults.ShadowBlur) / 2, shadow.X ?? StyleDefaults.ShadowX, shadow.Y ?? StyleDefaults.ShadowY)
            : null;

    /// The blur for a paint's mask, or null for a sharp effect. The caller disposes it, after the paint.
    public SKMaskFilter? Blur() => Sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Sigma) : null;

    /// A paint in the effect's color, blurred by `blur`.
    public SKPaint Paint(SKMaskFilter? blur) => new() { IsAntialias = true, Color = Color, MaskFilter = blur };
}
