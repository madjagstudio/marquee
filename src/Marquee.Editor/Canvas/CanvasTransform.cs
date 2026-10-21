using System.Windows;
using SkiaSharp;

namespace Marquee.Editor.Canvas;

/// The canvas's zoom: fit to the window, 100% or 200%.
public enum ZoomMode { Fit, Actual, Double }

/// How the canvas view maps theme pixels to its own coordinates and back: one scale for zoom and DPI together, so a
/// position is never rounded on the way, at any zoom on any screen.
/// - Zoom is screen pixels per theme pixel: at 1 (100%) each theme pixel is one pixel of the screen.
/// - DpiScale is screen pixels per WPF unit: 1.5 on a screen set to 150%.
/// - The view's coordinates are WPF units from the canvas's top-left corner: theme pixels times Scale.
public readonly record struct CanvasTransform(double Zoom, double DpiScale)
{
    /// The smallest zoom Fit goes to, for a window made tiny.
    public const double MinZoom = 0.05;

    public static CanvasTransform Identity { get; } = new(1, 1);

    /// WPF units per theme pixel.
    public double Scale => Zoom / DpiScale;

    /// A point in the view as theme pixels.
    public SKPoint ToTheme(Point view) => new((float)(view.X / Scale), (float)(view.Y / Scale));

    /// A point in theme pixels in the view.
    public Point ToView(SKPoint theme) => new(theme.X * Scale, theme.Y * Scale);

    /// The canvas's size in the view.
    public Size ViewSize(SKSizeI canvas) => new(canvas.Width * Scale, canvas.Height * Scale);

    /// The zoom `mode` means for a canvas of `canvas` theme pixels in a viewport of `viewport` WPF units on a screen
    /// of `dpiScale`: Fit makes the whole canvas fit, as large as it can.
    public static double ZoomFor(ZoomMode mode, Size viewport, SKSizeI canvas, double dpiScale) => mode switch
    {
        ZoomMode.Actual => 1,
        ZoomMode.Double => 2,
        _ when canvas.Width <= 0 || canvas.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0 => 1,
        _ => Math.Max(MinZoom, Math.Min(viewport.Width * dpiScale / canvas.Width, viewport.Height * dpiScale / canvas.Height)),
    };
}
