using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Everything a painter needs for one frame.
sealed class RenderContext(SKCanvas canvas, SensorSnapshot sensors, TimeSpan time, ThemeAssets assets,
    Dictionary<WidgetSpec, Smoother> smoothers, Dictionary<ShaderLayer, SKSurface> shaderSurfaces)
{
    public SKCanvas Canvas { get; } = canvas;
    public SensorSnapshot Sensors { get; } = sensors;

    /// Animation time: drives pulse, smoothing and shaders' `time`. Expressions use Sensors.Now instead.
    public TimeSpan Time { get; } = time;

    public ThemeAssets Assets { get; } = assets;

    /// The time zone and culture that `time` placeholders and formats write in.
    public TimeLocale Locale { get; init; } = TimeLocale.Pc;

    /// The number to display for a widget this frame, eased if the widget has `smooth`.
    public double Smoothed(WidgetSpec widget, double target)
    {
        if (widget.SmoothMs <= 0) return target;
        if (!smoothers.TryGetValue(widget, out var smoother))
            smoothers[widget] = smoother = new Smoother();
        return smoother.Next(target, Time, widget.SmoothMs);
    }

    /// A reusable offscreen surface for a scaled shader layer. The renderer owns and disposes these.
    public SKSurface ShaderSurface(ShaderLayer layer, int width, int height)
    {
        if (shaderSurfaces.TryGetValue(layer, out var surface))
        {
            var bounds = surface.Canvas.DeviceClipBounds;
            if (bounds.Width == width && bounds.Height == height) return surface;
            surface.Dispose();
            shaderSurfaces.Remove(layer);
        }
        surface = SKSurface.Create(new SKImageInfo(width, height))
            ?? throw new InvalidOperationException($"Could not create a {width}×{height} surface for shader '{layer.Path}'.");
        shaderSurfaces[layer] = surface;
        return surface;
    }
}
