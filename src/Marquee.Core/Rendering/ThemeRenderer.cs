using Marquee.Core.Expressions;
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
    readonly SKRect _viewBox;
    readonly Dictionary<WidgetSpec, Smoother> _smoothers = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<ShaderLayer, SKSurface> _shaderSurfaces = new(ReferenceEqualityComparer.Instance);

    public ThemeRenderer(LoadedTheme theme, SKSizeI? renderSize = null, SKRect? viewBox = null)
    {
        _theme = theme;
        Size = renderSize ?? theme.Spec.Canvas.Size;
        _viewBox = viewBox ?? SKRect.Create(theme.Spec.Canvas.Size.Width, theme.Spec.Canvas.Size.Height);
        _surface = SKSurface.Create(new SKImageInfo(Size.Width, Size.Height))
            ?? throw new InvalidOperationException("Could not create the render surface.");
    }

    public SKSizeI Size { get; }

    /// The time zone and culture that `time` placeholders and formats write in. Null, the default, is the PC's own,
    /// read each frame; tests set a fixed one so clocks draw the same on every machine.
    public TimeLocale? Locale { get; set; }

    /// One frame. `time` is animation time (pulse, smoothing); `sensors.Now` is the wall clock seen by expressions.
    public SKImage Render(SensorSnapshot sensors, TimeSpan time) =>
        Render(sensors, time, _theme.Spec.Widgets, RenderContext.NoStandIns);

    /// A frame with `widgets` drawn in place of the theme's own: the theme editor's preview of a drag, which moves,
    /// resizes or turns widgets in memory (copies made with `with`) and draws them without loading the theme again.
    /// `standIns` maps each copy, by reference, to the theme's widget it stands in for, so it keeps that widget's clip,
    /// drawn in its new box, and its smoothing; a group holding one is a copy too. Widgets not in it draw as they are.
    public SKImage Render(SensorSnapshot sensors, TimeSpan time, IReadOnlyList<WidgetSpec> widgets,
        IReadOnlyDictionary<WidgetSpec, WidgetSpec> standIns)
    {
        var canvas = _surface.Canvas;
        canvas.Clear(SKColors.Black);

        var ctx = new RenderContext(canvas, sensors, time, _theme.Assets, _smoothers, _shaderSurfaces)
        {
            Locale = Locale ?? TimeLocale.Pc,
            StandIns = standIns,
        };
        BackgroundPainter.Paint(ctx, _theme.Spec.Background, SKRect.Create(Size.Width, Size.Height));
        canvas.Save();
        canvas.Scale(Size.Width / _viewBox.Width, Size.Height / _viewBox.Height);
        canvas.Translate(-_viewBox.Left, -_viewBox.Top);
        foreach (var widget in widgets)
            WidgetPainter.Paint(ctx, widget, StyleSpec.Empty);
        canvas.Restore();

        canvas.Flush();
        return _surface.Snapshot();
    }

    public void Dispose()
    {
        _surface.Dispose();
        foreach (var surface in _shaderSurfaces.Values) surface.Dispose();
    }
}
