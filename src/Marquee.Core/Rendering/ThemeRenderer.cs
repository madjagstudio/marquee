using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Draws frames of one loaded theme at its design size. Keeps per-widget animation state (smoothing) between frames,
/// so use one renderer per theme for its whole life. Not thread-safe: the render loop calls it from its own thread.
public sealed class ThemeRenderer : IDisposable
{
    readonly LoadedTheme _theme;
    readonly SKSurface _surface;
    readonly Dictionary<WidgetSpec, Smoother> _smoothers = new(ReferenceEqualityComparer.Instance);

    public ThemeRenderer(LoadedTheme theme)
    {
        _theme = theme;
        _surface = SKSurface.Create(new SKImageInfo(Size.Width, Size.Height))
            ?? throw new InvalidOperationException("Could not create the render surface.");
    }

    public SKSizeI Size => _theme.Spec.Canvas.Size;

    /// One frame. `time` is animation time (pulse, smoothing); `sensors.Now` is the wall clock seen by expressions.
    public SKImage Render(SensorSnapshot sensors, TimeSpan time)
    {
        var canvas = _surface.Canvas;
        canvas.Clear(SKColors.Black);

        var ctx = new RenderContext(canvas, sensors, time, _theme.Assets, _smoothers);
        BackgroundPainter.Paint(ctx, _theme.Spec.Background, SKRect.Create(Size.Width, Size.Height));
        foreach (var widget in _theme.Spec.Widgets)
            WidgetPainter.Paint(ctx, widget, StyleSpec.Empty);

        canvas.Flush();
        return _surface.Snapshot();
    }

    public void Dispose() => _surface.Dispose();
}
