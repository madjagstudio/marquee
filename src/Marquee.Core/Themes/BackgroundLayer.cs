using Marquee.Core.Expressions;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// One layer of the background, composited bottom-up: a fill, an image, a shader or a video clip.
public abstract record BackgroundLayer(float Opacity, SKBlendMode Blend)
{
    /// Not drawn, and its file not loaded; `marquee validate` still checks it.
    public bool Hidden { get; init; }

    /// For the editor, which won't change a locked layer. The renderer ignores it.
    public bool Locked { get; init; }

    /// What the editor's layers list calls the layer; null when the theme gives no name. The renderer ignores it.
    public string? Name { get; init; }
}

public sealed record FillLayer(PaintSpec Paint, float Opacity, SKBlendMode Blend) : BackgroundLayer(Opacity, Blend);

/// Path is relative to the theme folder.
public sealed record ImageLayer(string Path, ImageFit Fit, float Opacity, SKBlendMode Blend) : BackgroundLayer(Opacity, Blend);

/// A video clip (MP4, M4V, MOV or WMV through Media Foundation; animated GIF or WebP through Skia) that always loops.
/// Its frames are decoded ahead on a background thread, so drawing one never waits on the decoder; the layer draws the
/// frame for the current time, fitted like an image. Path is relative to the theme folder.
public sealed record VideoLayer(string Path, ImageFit Fit, float Opacity, SKBlendMode Blend) : BackgroundLayer(Opacity, Blend);

/// How an image or video frame fills its box. Cover crops to fill, contain letterboxes, stretch distorts, none draws at natural size, centered.
public enum ImageFit { Cover, Contain, Stretch, None }

/// An SkSL shader (a .sksl file in the theme folder) painted over the whole canvas. Marquee sets its `time` (seconds,
/// wrapping daily), `day` (whole days) and `resolution` (pixels) uniforms; Uniforms binds others to expressions, one per
/// component. Scale below 1 renders it at that fraction of the canvas size and stretches it up: shaders run on the CPU,
/// and smooth ones lose nothing.
public sealed record ShaderLayer(string Path, IReadOnlyList<ShaderUniform> Uniforms, float Scale, float Opacity, SKBlendMode Blend)
    : BackgroundLayer(Opacity, Blend);

/// A shader uniform bound to expressions: one for a float, two for a float2, and so on. A missing or stale value is 0.
public sealed record ShaderUniform(string Name, IReadOnlyList<Expression> Values);
