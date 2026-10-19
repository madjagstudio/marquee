using System.Text;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A shader layer whose test frame says it can't keep up with the theme's frame rate is skipped when the theme loads.
public class ShaderCostTests
{
    const string Cheap = "half4 main(float2 p) { return half4(0, 0, 1, 1); }";

    /// 160 sin() calls a pixel: about 2 s a full frame on the CPU, far over any frame budget.
    const string Heavy = """
        uniform float time;
        half4 main(float2 p) {
            float v = 0.0;
            for (int i = 0; i < 16; i++) {
                for (int j = 0; j < 10; j++) { v += sin(p.x * 0.001 + float(i) * 0.37 + float(j) + time); }
            }
            return half4(half(fract(v)), 0, 0, 1);
        }
        """;

    static TempTheme Theme(string background, int fps = 30, params (string Path, string Source)[] shaders)
    {
        var theme = new TempTheme($"canvas: {{ orientation: landscape, fps: {fps} }}\nbackground:\n{background}");
        foreach (var (path, source) in shaders) theme.AddFile(path, Encoding.UTF8.GetBytes(source));
        return theme;
    }

    /// A probe that reports each cost in turn (the last one from then on) and records what it was asked to measure.
    sealed class FakeProbe(params double[] milliseconds)
    {
        public List<(ShaderLayer Layer, SKSizeI Canvas)> Calls { get; } = [];

        public TimeSpan Measure(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas)
        {
            Calls.Add((layer, canvas));
            return TimeSpan.FromMilliseconds(milliseconds[Math.Min(Calls.Count, milliseconds.Length) - 1]);
        }
    }

    [Fact]
    public void A_shader_over_the_frame_budget_is_skipped_with_its_estimated_cost()
    {
        using var folder = Theme("  - fill: \"#203040\"\n  - shader: shaders/slow.sksl", 30, ("shaders/slow.sksl", Cheap));
        var probe = new FakeProbe(1250);
        using var theme = ThemeLoader.Load(folder.Folder, null, probe.Measure, TestProbes.Free);
        using var frame = Frames.Render(theme, TestSensors.Of());

        Assert.Equal(["Shader 'shaders/slow.sksl' would take about 1,250 ms to render a frame, over the 33 ms budget at 30 fps " +
            "(lower its scale or the fps, or simplify it); the layer is skipped."], theme.Warnings);
        Assert.Null(theme.Assets.Shader((ShaderLayer)theme.Spec.Background[1]));
        Pixels.AssertNear(new SKColor(0x20, 0x30, 0x40), Images.PixelAt(frame, 960, 240), 0);
    }

    [Fact]
    public void A_shader_within_the_budget_is_kept()
    {
        using var folder = Theme("  - shader: s.sksl", 30, ("s.sksl", Cheap));
        var probe = new FakeProbe(30);
        using var theme = ThemeLoader.Load(folder.Folder, null, probe.Measure, TestProbes.Free);

        Assert.Empty(theme.Warnings);
        Assert.NotNull(theme.Assets.Shader((ShaderLayer)theme.Spec.Background[0]));
    }

    [Fact]
    public void The_budget_follows_the_fps_override()
    {
        using var folder = Theme("  - shader: s.sksl", 30, ("s.sksl", Cheap));

        using (var at60 = ThemeLoader.Load(folder.Folder, 60, new FakeProbe(20).Measure, TestProbes.Free))
            Assert.Contains("over the 17 ms budget at 60 fps", Assert.Single(at60.Warnings));
        using (var at10 = ThemeLoader.Load(folder.Folder, 10, new FakeProbe(90).Measure, TestProbes.Free))
            Assert.Empty(at10.Warnings);
    }

    [Fact]
    public void A_slow_test_frame_is_measured_again_before_the_layer_is_skipped()
    {
        // One test frame can be slow for reasons that have nothing to do with the shader (another process, a GC).
        using var folder = Theme("  - shader: s.sksl", 30, ("s.sksl", Cheap));
        var probe = new FakeProbe(500, 5);
        using var theme = ThemeLoader.Load(folder.Folder, null, probe.Measure, TestProbes.Free);

        Assert.Empty(theme.Warnings);
        Assert.Equal(2, probe.Calls.Count);
    }

    [Fact]
    public void Each_shader_layer_is_measured_on_the_themes_canvas()
    {
        using var folder = Theme("  - shader: a.sksl\n    scale: 0.5\n  - shader: b.sksl", 30, ("a.sksl", Cheap), ("b.sksl", Cheap));
        var probe = new FakeProbe(1);
        using var theme = ThemeLoader.Load(folder.Folder, null, probe.Measure, TestProbes.Free);

        Assert.Equal(["a.sksl", "b.sksl"], probe.Calls.Select(c => c.Layer.Path));
        Assert.All(probe.Calls, c => Assert.Equal(new SKSizeI(1920, 480), c.Canvas));
    }

    [Fact]
    public void A_shader_that_is_skipped_anyway_is_not_measured()
    {
        using var folder = Theme("  - shader: broken.sksl", 30, ("broken.sksl", "half4 main(float2 p) { return glow; }"));
        var probe = new FakeProbe(1);
        using var theme = ThemeLoader.Load(folder.Folder, null, probe.Measure, TestProbes.Free);

        Assert.Empty(probe.Calls);
    }

    [Fact]
    public void The_test_frame_is_a_fraction_of_the_rendered_size()
    {
        Assert.Equal((240, 60), ShaderCost.ProbeSize(1920, 480));
        Assert.Equal((120, 30), ShaderCost.ProbeSize(960, 240));
        Assert.Equal((1, 1), ShaderCost.ProbeSize(5, 3));
    }

    [Fact]
    public void A_really_heavy_shader_is_skipped_and_a_cheap_one_kept()
    {
        // The real test frame. Heavy is hundreds of times over a 60 fps budget, Cheap a small fraction of it, so the
        // outcome doesn't depend on how fast or busy this machine is.
        using var folder = Theme("  - shader: heavy.sksl\n  - shader: cheap.sksl", 60, ("heavy.sksl", Heavy), ("cheap.sksl", Cheap));
        using var theme = ThemeLoader.Load(folder.Folder);

        var warning = Assert.Single(theme.Warnings);
        Assert.StartsWith("Shader 'heavy.sksl' would take about ", warning);
        Assert.Null(theme.Assets.Shader((ShaderLayer)theme.Spec.Background[0]));
        Assert.NotNull(theme.Assets.Shader((ShaderLayer)theme.Spec.Background[1]));
    }

    [Fact]
    public void The_watcher_loads_with_the_fps_override()
    {
        using var folder = Theme("  - shader: s.sksl", 1, ("s.sksl", Heavy));
        var warnings = new List<string>();
        using var watcher = new ThemeWatcher(folder.Folder, t => { warnings.AddRange(t.Warnings); t.Dispose(); }, _ => { }, fpsOverride: 60);

        Assert.True(watcher.Reload(retryIfLocked: false));
        var warning = Assert.Single(warnings);
        Assert.Contains("budget at 60 fps", warning);
    }
}
