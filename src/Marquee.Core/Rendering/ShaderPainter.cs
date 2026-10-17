using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Paints a shader layer: sets `time` (animation seconds), `resolution` (the pixels it renders) and the bound uniforms,
/// then fills the area, at a reduced size first when the layer has a scale below 1.
static class ShaderPainter
{
    static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

    /// Seconds after which shader `time` wraps to 0. A float keeps sub-10 ms precision within a day, but after days of
    /// uptime consecutive frames would share a value; wrapping daily means one visible jump a day instead.
    const double ShaderTimePeriod = 86400;

    public static void Paint(RenderContext ctx, ShaderLayer layer, SKRuntimeEffect effect, SKRect area)
    {
        var width = Math.Max(1, (int)MathF.Round(area.Width * layer.Scale));
        var height = Math.Max(1, (int)MathF.Round(area.Height * layer.Scale));

        using var uniforms = new SKRuntimeEffectUniforms(effect);
        if (uniforms.Contains("time")) uniforms["time"] = (float)(ctx.Time.TotalSeconds % ShaderTimePeriod);
        if (uniforms.Contains("resolution")) uniforms["resolution"] = new float[] { width, height };
        foreach (var binding in layer.Uniforms)
        {
            if (!uniforms.Contains(binding.Name)) continue; // warned about at load
            var values = binding.Values.Select(v => (float)(v.Evaluate(ctx.Sensors)?.Number ?? 0)).ToArray();
            uniforms[binding.Name] = values.Length == 1 ? values[0] : values;
        }

        using var shader = effect.ToShader(uniforms);
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
}
