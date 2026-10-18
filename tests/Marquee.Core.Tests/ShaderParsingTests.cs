using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class ShaderParsingTests
{
    static ShaderLayer Shader(string layer) =>
        Assert.IsType<ShaderLayer>(Layouts.Parse($"canvas: {{ orientation: landscape }}\nbackground:\n{layer}").Background[0]);

    static ThemeException Error(string layer) => Layouts.Error($"canvas: {{ orientation: landscape }}\nbackground:\n{layer}");

    [Fact]
    public void Shader_layer_defaults()
    {
        var layer = Shader("  - shader: shaders/plasma.sksl");

        Assert.Equal("shaders/plasma.sksl", layer.Path);
        Assert.Empty(layer.Uniforms);
        Assert.Equal((1f, 1f, SKBlendMode.SrcOver), (layer.Scale, layer.Opacity, layer.Blend));
    }

    [Fact]
    public void Uniforms_bind_expressions_and_vectors()
    {
        var layer = Shader("""
              - shader: aurora.sksl
                uniforms:
                  intensity: "0.2 + gpu.load / 100"
                  speed: 1.5
                  tint: [0.2, 0.5, "cpu.load / 100"]
                scale: 0.5
                opacity: 0.8
                blend: screen
            """);

        Assert.Equal(["intensity", "speed", "tint"], layer.Uniforms.Select(u => u.Name));
        Assert.Equal([1, 1, 3], layer.Uniforms.Select(u => u.Values.Count));
        var sensors = TestSensors.Of(("gpu.load", 30), ("cpu.load", 50));
        Assert.Equal(0.5, layer.Uniforms[0].Values[0].Evaluate(sensors)!.Value.Number!.Value, 9);
        Assert.Equal(1.5, layer.Uniforms[1].Values[0].Evaluate(sensors)!.Value.Number);
        Assert.Equal(0.5, layer.Uniforms[2].Values[2].Evaluate(sensors)!.Value.Number);
        Assert.Equal((0.5f, 0.8f, SKBlendMode.Screen), (layer.Scale, layer.Opacity, layer.Blend));
    }

    [Theory]
    [InlineData("  - shader: a.sksl\n    uniforms:\n      time: 1", 5, "uniform 'time' is set by Marquee and can't be bound")]
    [InlineData("  - shader: a.sksl\n    uniforms:\n      day: 1", 5, "uniform 'day' is set by Marquee and can't be bound")]
    [InlineData("  - shader: a.sksl\n    uniforms:\n      resolution: [1, 2]", 5, "uniform 'resolution' is set by Marquee and can't be bound")]
    [InlineData("  - shader: a.sksl\n    uniforms:\n      my-glow: 1", 5, "'my-glow' is not a uniform name: use letters, digits and _, starting with a letter or _")]
    [InlineData("  - shader: a.sksl\n    uniforms:\n      glow: \"gpu.load +\"", 5, "uniform 'glow': Expression ended unexpectedly at character 11")]
    [InlineData("  - shader: a.sksl\n    uniforms:\n      glow: []", 5, "uniform 'glow' needs 1 to 16 values")]
    [InlineData("  - shader: a.sksl\n    uniforms:\n      glow: { a: 1 }", 5, "uniform 'glow' must be a single value, not a list or mapping")]
    [InlineData("  - shader: a.sksl\n    uniforms: [1, 2]", 4, "uniforms must be a mapping of uniform names to values, e.g. { intensity: \"gpu.load / 100\" }")]
    [InlineData("  - shader: a.sksl\n    scale: 0.05", 4, "scale must be between 0.1 and 1")]
    [InlineData("  - shader: a.sksl\n    fit: cover", 4, "fit only applies to image layers")]
    [InlineData("  - fill: \"#000\"\n    uniforms: { a: 1 }", 4, "uniforms only applies to shader layers")]
    [InlineData("  - image: a.png\n    scale: 0.5", 4, "scale only applies to shader layers")]
    [InlineData("  - shader: [a.sksl]", 3, "shader must be a single value, not a list or mapping")]
    public void Shader_layer_errors(string layer, int line, string reason)
    {
        var ex = Error(layer);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void At_most_16_values_per_uniform() =>
        Assert.Equal("uniform 'm' needs 1 to 16 values",
            Error($"  - shader: a.sksl\n    uniforms:\n      m: [{string.Join(", ", Enumerable.Repeat("1", 17))}]").Reason);

    [Fact]
    public void At_most_4_shader_layers()
    {
        // Each shader layer is compiled and may keep its own offscreen surface; other layers don't count.
        Assert.Equal(4, Layouts.Parse("canvas: { orientation: landscape }\nbackground:\n" +
            string.Concat(Enumerable.Repeat("  - shader: a.sksl\n  - fill: \"#000\"\n", 4))).Background.Count(l => l is ShaderLayer));

        var ex = Error(string.Concat(Enumerable.Repeat("  - shader: a.sksl\n  - fill: \"#000\"\n", 5)));

        Assert.Equal("a background can have at most 4 shader layers", ex.Reason);
        Assert.Equal(11, ex.Line); // the fifth shader layer
    }

    [Fact]
    public void At_most_32_uniforms() =>
        Assert.Equal("a shader layer can bind at most 32 uniforms",
            Error("  - shader: a.sksl\n    uniforms:\n" + string.Concat(Enumerable.Range(0, 33).Select(i => $"      u{i}: 1\n"))).Reason);
}
