using SkiaSharp;

namespace Marquee.Core.Devices;

/// How a panel is mounted, which also decides the canvas that layouts are designed at.
/// Portrait shows the panel's native buffer upright; Landscape turns it 90° clockwise.
/// The Flipped variants are the same mounting turned 180°, for when the picture is upside down.
public enum Orientation { Portrait, PortraitFlipped, Landscape, LandscapeFlipped }

public static class OrientationExtensions
{
    public static bool IsPortrait(this Orientation orientation) =>
        orientation is Orientation.Portrait or Orientation.PortraitFlipped;

    public static SKSizeI CanvasSize(this Orientation orientation) =>
        orientation.IsPortrait() ? new SKSizeI(480, 1920) : new SKSizeI(1920, 480);
}
