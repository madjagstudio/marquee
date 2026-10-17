using System.Text;
using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class ShaderRenderingTests
{
    /// Red follows x across the canvas, green is time / 10 s, blue is the bound `level`.
    const string Probe = """
        uniform float time;
        uniform float2 resolution;
        uniform float level;
        half4 main(float2 p) {
            return half4(half(p.x / resolution.x), half(time / 10.0), half(level), 1.0);
        }
        """;

    static TempTheme Theme(string background, params (string Path, string Source)[] shaders)
    {
        var theme = new TempTheme($"canvas: {{ orientation: landscape }}\nbackground:\n{background}");
        foreach (var (path, source) in shaders) theme.AddFile(path, Encoding.UTF8.GetBytes(source));
        return theme;
    }

    static SKImage Render(TempTheme folder, SensorSnapshot sensors, double seconds)
    {
        using var theme = folder.Load();
        return Frames.Render(theme, sensors, TimeSpan.FromSeconds(seconds));
    }

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    [Fact]
    public void Shader_gets_time_resolution_and_bound_uniforms()
    {
        using var folder = Theme("  - shader: probe.sksl\n    uniforms: { level: \"cpu.load / 100\" }", ("probe.sksl", Probe));
        using var frame = Render(folder, TestSensors.Of(("cpu.load", 25)), seconds: 5);

        Pixels.AssertNear(new SKColor(0, 128, 64), At(frame, 0, 240), 2);
        Pixels.AssertNear(new SKColor(128, 128, 64), At(frame, 960, 240), 2);
        Pixels.AssertNear(new SKColor(255, 128, 64), At(frame, 1919, 240), 2);
    }

    [Fact]
    public void Shader_time_wraps_daily_so_a_float_stays_precise()
    {
        using var folder = Theme("  - shader: probe.sksl\n    uniforms: { level: 0 }", ("probe.sksl", Probe));
        using var frame = Render(folder, TestSensors.Of(), seconds: 86_400 + 5);

        Pixels.AssertNear(new SKColor(128, 128, 0), At(frame, 960, 240), 2);
    }

    [Fact]
    public void A_stale_binding_is_zero()
    {
        using var folder = Theme("  - shader: probe.sksl\n    uniforms: { level: \"cpu.load / 100\" }", ("probe.sksl", Probe));
        using var frame = Render(folder, TestSensors.Of(), seconds: 0);

        Pixels.AssertNear(new SKColor(128, 0, 0), At(frame, 960, 240), 2);
    }

    [Fact]
    public void Scale_renders_smaller_and_stretches_it_to_the_canvas()
    {
        using var folder = Theme("  - shader: probe.sksl\n    uniforms: { level: 1 }\n    scale: 0.25", ("probe.sksl", Probe));
        using var frame = Render(folder, TestSensors.Of(), seconds: 2.5);

        Pixels.AssertNear(new SKColor(128, 64, 255), At(frame, 960, 240), 3);
        Pixels.AssertNear(new SKColor(255, 64, 255), At(frame, 1919, 479), 3);
    }

    [Fact]
    public void Opacity_and_blend_apply_over_lower_layers()
    {
        using var folder = Theme("""
              - fill: "#FF0000"
              - shader: blue.sksl
                opacity: 0.5
            """, ("blue.sksl", "half4 main(float2 p) { return half4(0, 0, 1, 1); }"));
        using var frame = Render(folder, TestSensors.Of(), 0);

        Pixels.AssertNear(new SKColor(128, 0, 127), At(frame, 960, 240), 2);
    }

    [Fact]
    public void A_shader_that_does_not_compile_is_skipped_with_the_error()
    {
        using var folder = Theme("  - fill: \"#203040\"\n  - shader: broken.sksl", ("broken.sksl", "half4 main(float2 p) { return glow; }"));
        using var theme = folder.Load();
        using var frame = Frames.Render(theme, TestSensors.Of());

        var warning = Assert.Single(theme.Warnings);
        Assert.StartsWith("Shader 'broken.sksl' doesn't compile: error: 1: unknown identifier 'glow'", warning);
        Assert.EndsWith("; the layer is skipped.", warning);
        Pixels.AssertNear(new SKColor(0x20, 0x30, 0x40), At(frame, 960, 240), 0);
    }

    [Fact]
    public void Compile_errors_keep_the_messages_and_drop_the_echoed_source()
    {
        using var folder = Theme("  - shader: two.sksl", ("two.sksl", "half4 main(float2 p) { return glow + dim; }"));
        using var theme = folder.Load();

        var warning = Assert.Single(theme.Warnings);
        Assert.Contains("error: 1: unknown identifier 'glow'", warning);
        Assert.Contains("error: 1: unknown identifier 'dim'", warning);
        Assert.DoesNotContain('^', warning);
        Assert.DoesNotContain("half4 main", warning);
    }

    [Fact]
    public void An_int_uniform_gets_a_warning_that_says_so()
    {
        using var folder = Theme("  - shader: s.sksl\n    uniforms: { mode: 1 }",
            ("s.sksl", "uniform int mode; half4 main(float2 p) { return half4(half(mode), 0, 0, 1); }"));
        using var theme = folder.Load();

        Assert.Equal(["Shader 's.sksl' declares uniform 'mode' as an int; Marquee binds floats, so declare it as float (or float2…); the layer is skipped."],
            theme.Warnings);
    }

    [Theory]
    [InlineData("missing.sksl", "Shader 'missing.sksl' was not found; the layer is skipped.")]
    [InlineData("../outside.sksl", "Shader '../outside.sksl' is outside the theme folder; the layer is skipped.")]
    public void Missing_or_outside_shaders_are_skipped(string path, string warning)
    {
        using var folder = Theme($"  - shader: {path}");
        using var theme = folder.Load();

        Assert.Equal([warning], theme.Warnings);
    }

    [Fact]
    public void Large_shader_files_are_skipped()
    {
        using var folder = Theme("  - shader: big.sksl", ("big.sksl", "// " + new string('x', 70_000)));
        using var theme = folder.Load();

        Assert.Equal(["Shader 'big.sksl' is larger than 64 KB; the layer is skipped."], theme.Warnings);
    }

    [Fact]
    public void A_binding_of_the_wrong_size_skips_the_layer()
    {
        using var folder = Theme("  - shader: probe.sksl\n    uniforms: { level: [1, 2, 3] }", ("probe.sksl", Probe));
        using var theme = folder.Load();

        Assert.Equal(["Shader 'probe.sksl' can't take 3 values for uniform 'level'; bind one value per component (3 for a float3); the layer is skipped."],
            theme.Warnings);
        Assert.Null(theme.Assets.Shader((Marquee.Core.Themes.ShaderLayer)theme.Spec.Background[0]));
    }

    [Fact]
    public void Unknown_bindings_and_unbound_uniforms_are_warnings_only()
    {
        using var folder = Theme("  - shader: probe.sksl\n    uniforms: { glow: 1 }", ("probe.sksl", Probe));
        using var theme = folder.Load();

        Assert.Equal([
            "Shader 'probe.sksl' has no uniform 'glow'; that binding is ignored.",
            "Shader 'probe.sksl' uniform 'level' isn't bound in layout.yaml, so it stays 0.",
        ], theme.Warnings);
        Assert.NotNull(theme.Assets.Shader((Marquee.Core.Themes.ShaderLayer)theme.Spec.Background[0]));
    }

    [Theory]
    [InlineData("uniform float2 time; half4 main(float2 p) { return half4(time.x); }", "declares 'time' as something other than a float")]
    [InlineData("uniform float resolution; half4 main(float2 p) { return half4(resolution); }", "declares 'resolution' as something other than a float2")]
    [InlineData("uniform shader image; half4 main(float2 p) { return image.eval(p); }", "declares child shaders (image), which Marquee doesn't provide")]
    public void Shaders_marquee_cannot_feed_are_skipped(string source, string problem)
    {
        using var folder = Theme("  - shader: s.sksl", ("s.sksl", source));
        using var theme = folder.Load();

        Assert.Equal([$"Shader 's.sksl' {problem}; the layer is skipped."], theme.Warnings);
    }

    [Fact]
    public void Editing_the_shader_file_triggers_hot_reload()
    {
        // ThemeWatcher reloads on any file in the theme folder; this pins that .sksl edits count.
        using var folder = Theme("  - shader: probe.sksl\n    uniforms: { level: 1 }", ("probe.sksl", Probe));
        var reloads = 0;
        using var watcher = new Marquee.Core.Themes.ThemeWatcher(folder.Folder, t => { Interlocked.Increment(ref reloads); t.Dispose(); }, _ => { },
            TimeSpan.FromMilliseconds(50));
        watcher.Start();

        File.WriteAllText(Path.Combine(folder.Folder, "probe.sksl"), Probe.Replace("half(level)", "half(1.0 - level)"));

        Assert.True(Wait.Until(() => Volatile.Read(ref reloads) > 0));
    }
}
