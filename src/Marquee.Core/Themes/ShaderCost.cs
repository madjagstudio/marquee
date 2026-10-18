using System.Diagnostics;
using Marquee.Core.Rendering;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// Measures what a compiled shader layer costs to render on `canvas`, as a full frame's time.
delegate TimeSpan ShaderProbe(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas);

/// Estimates a shader layer's cost per frame from one low-resolution test frame, when the theme loads. SkSL runs on the
/// CPU, and an accepted shader can take seconds a frame: the panel would freeze on its last frame, and Ctrl+C and
/// closing the preview would wait for the frame. A layer over the frame budget is skipped instead (see ShaderAssets).
static class ShaderCost
{
    /// The test frame is 1/8 of the rendered size each way, so it costs about 1/64 of a frame.
    const int ProbeDivisor = 8;

    /// The test frame's size for a shader that renders `width` × `height` pixels.
    public static (int Width, int Height) ProbeSize(int width, int height) =>
        (Math.Max(1, width / ProbeDivisor), Math.Max(1, height / ProbeDivisor));

    /// Renders the test frame and scales its time up to the layer's full pixel count (the canvas times its scale).
    /// The test frame samples the whole area, so a shader that is costly in one part is caught. `time` and `day` are 0
    /// and bound uniforms are 0, as a stale value is: no sensors are read while a theme loads.
    public static TimeSpan Estimate(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas)
    {
        var (width, height) = ShaderPainter.RenderSize(layer, canvas);
        var (probeWidth, probeHeight) = ProbeSize(width, height);

        using var surface = SKSurface.Create(new SKImageInfo(probeWidth, probeHeight))
            ?? throw new InvalidOperationException($"Could not create a {probeWidth}×{probeHeight} surface to measure shader '{layer.Path}'.");
        using var shader = ShaderPainter.Shader(effect, layer, width, height, TimeSpan.Zero, sensors: null);
        using var paint = new SKPaint { Shader = shader };
        var target = surface.Canvas;
        target.Scale(probeWidth / (float)width, probeHeight / (float)height);

        // One pixel first, so the time to build the shader's pipeline isn't counted as per-pixel cost.
        target.Save();
        target.ClipRect(new SKRect(0, 0, width / (float)probeWidth, height / (float)probeHeight));
        target.DrawRect(0, 0, width, height, paint);
        target.Restore();

        var clock = Stopwatch.StartNew();
        target.DrawRect(0, 0, width, height, paint);
        clock.Stop();

        return clock.Elapsed * ((double)width * height / ((double)probeWidth * probeHeight));
    }
}
