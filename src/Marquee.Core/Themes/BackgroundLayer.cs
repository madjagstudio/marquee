using Marquee.Core.Expressions;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// One layer of the background, composited bottom-up. Video layers arrive in M4.
public abstract record BackgroundLayer(float Opacity, SKBlendMode Blend);

public sealed record FillLayer(PaintSpec Paint, float Opacity, SKBlendMode Blend) : BackgroundLayer(Opacity, Blend);

/// Path is relative to the theme folder.
public sealed record ImageLayer(string Path, ImageFit Fit, float Opacity, SKBlendMode Blend) : BackgroundLayer(Opacity, Blend);

/// How an image fills its box. Cover crops to fill, contain letterboxes, stretch distorts, none draws at natural size, centered.
public enum ImageFit { Cover, Contain, Stretch, None }

/// An SkSL shader (a .sksl file in the theme folder) painted over the whole canvas. Marquee sets its `time` (seconds)
/// and `resolution` (pixels) uniforms; Uniforms binds others to expressions, one per component. Scale below 1 renders
/// it at that fraction of the canvas size and stretches it up: shaders run on the CPU, and smooth ones lose nothing.
public sealed record ShaderLayer(string Path, IReadOnlyList<ShaderUniform> Uniforms, float Scale, float Opacity, SKBlendMode Blend)
    : BackgroundLayer(Opacity, Blend);

/// A shader uniform bound to expressions: one for a float, two for a float2, and so on. A missing or stale value is 0.
public sealed record ShaderUniform(string Name, IReadOnlyList<Expression> Values);
