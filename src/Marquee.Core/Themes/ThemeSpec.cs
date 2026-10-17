using Marquee.Core.Devices;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// A parsed layout.yaml: what to draw, before any assets are loaded.
public sealed record ThemeSpec(CanvasSpec Canvas, IReadOnlyList<BackgroundLayer> Background, IReadOnlyList<WidgetSpec> Widgets);

/// The orientation family a theme is designed for. It fits both the plain and -flipped device orientations.
public enum ThemeOrientation { Portrait, Landscape }

public sealed record CanvasSpec(ThemeOrientation Orientation, int Fps)
{
    public const int DefaultFps = 30;
    public const int MaxFps = 60;

    public SKSizeI Size => Orientation == ThemeOrientation.Portrait ? new SKSizeI(480, 1920) : new SKSizeI(1920, 480);

    public bool Fits(Marquee.Core.Devices.Orientation device) => device.IsPortrait() == (Orientation == ThemeOrientation.Portrait);
}
