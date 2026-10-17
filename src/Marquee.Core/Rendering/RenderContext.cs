using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Everything a painter needs for one frame.
sealed class RenderContext(SKCanvas canvas, SensorSnapshot sensors, TimeSpan time, ThemeAssets assets, Dictionary<WidgetSpec, Smoother> smoothers)
{
    public SKCanvas Canvas { get; } = canvas;
    public SensorSnapshot Sensors { get; } = sensors;

    /// Animation time: drives pulse and smoothing. Expressions use Sensors.Now instead.
    public TimeSpan Time { get; } = time;

    public ThemeAssets Assets { get; } = assets;

    /// The number to display for a widget this frame, eased if the widget has `smooth`.
    public double Smoothed(WidgetSpec widget, double target)
    {
        if (widget.SmoothMs <= 0) return target;
        if (!smoothers.TryGetValue(widget, out var smoother))
            smoothers[widget] = smoother = new Smoother();
        return smoother.Next(target, Time, widget.SmoothMs);
    }
}
