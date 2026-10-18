using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Paints a shader layer: sets `time` (animation seconds), `day` (whole days), `resolution` (the pixels it renders) and
/// the bound uniforms, then fills the area, at a reduced size first when the layer has a scale below 1.
static class ShaderPainter
{
    static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

    /// Seconds after which shader `time` wraps to 0, and `day` counts up by one. A float keeps sub-10 ms precision
    /// within a day, but after days of uptime consecutive frames would share a value. A shader that must not jump at
    /// the wrap can animate with `day` too, e.g. a phase of fract(day * k + time / period).
    const double ShaderTimePeriod = 86400;

    public static void Paint(RenderContext ctx, ShaderLayer layer, SKRuntimeEffect effect, SKRect area)
    {
        var (width, height) = RenderSize(layer, area.Size);

        using var shader = Shader(effect, layer, width, height, ctx.Time, ctx.Sensors);
        using var paint = new SKPaint { BlendMode = layer.Blend, Color = SKColors.White.WithAlpha(Paints.Alpha(layer.Opacity)) };

        if (width == (int)area.Width && height == (int)area.Height)
        {
            paint.Shader = shader;
            ctx.Canvas.Save();
            ctx.Canvas.Translate(area.Left, area.Top); // shader coordinates start at the area's corner
            ctx.Canvas.DrawRect(0, 0, area.Width, area.Height, paint);
            ctx.Canvas.Restore();
            return;
        }

        var surface = ctx.ShaderSurface(layer, width, height);
        surface.Canvas.Clear(SKColors.Transparent);
        using (var fill = new SKPaint { Shader = shader })
            surface.Canvas.DrawRect(0, 0, width, height, fill);
        using var image = surface.Snapshot();
        ctx.Canvas.DrawImage(image, area, Sampling, paint);
    }

    /// The pixels the shader renders for an area: the area's size times the layer's scale.
    public static (int Width, int Height) RenderSize(ShaderLayer layer, SKSize area) =>
        (Math.Max(1, (int)MathF.Round(area.Width * layer.Scale)), Math.Max(1, (int)MathF.Round(area.Height * layer.Scale)));

    /// The shader with Marquee's uniforms and the layer's bindings set for one frame. Null sensors bind every value to
    /// 0, as a stale one is.
    public static SKShader Shader(SKRuntimeEffect effect, ShaderLayer layer, int width, int height, TimeSpan time, SensorSnapshot? sensors)
    {
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        if (uniforms.Contains("time")) uniforms["time"] = (float)(time.TotalSeconds % ShaderTimePeriod);
        if (uniforms.Contains("day")) uniforms["day"] = (float)Math.Floor(time.TotalSeconds / ShaderTimePeriod);
        if (uniforms.Contains("resolution")) uniforms["resolution"] = new float[] { width, height };
        foreach (var binding in layer.Uniforms)
        {
            if (!uniforms.Contains(binding.Name)) continue; // warned about at load
            var values = binding.Values.Select(v => (float)(sensors is null ? 0 : v.Evaluate(sensors)?.Number ?? 0)).ToArray();
            uniforms[binding.Name] = values.Length == 1 ? values[0] : values;
        }

        return effect.ToShader(uniforms);
    }
}
