using SkiaSharp;

namespace Marquee.Core.Themes;

/// One layer of the background, composited bottom-up. Shader and video layers arrive in M3 and M4.
public abstract record BackgroundLayer(float Opacity, SKBlendMode Blend);

public sealed record FillLayer(PaintSpec Paint, float Opacity, SKBlendMode Blend) : BackgroundLayer(Opacity, Blend);

/// Path is relative to the theme folder.
public sealed record ImageLayer(string Path, ImageFit Fit, float Opacity, SKBlendMode Blend) : BackgroundLayer(Opacity, Blend);

/// How an image fills its box. Cover crops to fill, contain letterboxes, stretch distorts, none draws at natural size, centered.
public enum ImageFit { Cover, Contain, Stretch, None }
