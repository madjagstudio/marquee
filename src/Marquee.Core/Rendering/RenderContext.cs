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

    /// Null draws shader layers on the CPU.
    public ShaderGpu? Gpu { get; init; }

    /// For the theme editor's drag preview: each widget drawn in place of one of the theme's, mapped (by reference) to
    /// the widget it stands in for. Empty for any other frame.
    public IReadOnlyDictionary<WidgetSpec, WidgetSpec> StandIns { get; init; } = NoStandIns;

    public static IReadOnlyDictionary<WidgetSpec, WidgetSpec> NoStandIns { get; } =
        new Dictionary<WidgetSpec, WidgetSpec>(ReferenceEqualityComparer.Instance);

    /// The theme's widget that `widget` stands in for, or `widget` itself: what its clip and smoothing are found by.
    public WidgetSpec Original(WidgetSpec widget) => StandIns.GetValueOrDefault(widget, widget);

    /// The number to display for a widget this frame, eased if the widget has `smooth`.
    public double Smoothed(WidgetSpec widget, double target)
    {
        if (widget.SmoothMs <= 0) return target;
        var original = Original(widget);
        if (!smoothers.TryGetValue(original, out var smoother))
            smoothers[original] = smoother = new Smoother();
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
