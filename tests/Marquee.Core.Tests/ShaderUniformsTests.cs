using System.Runtime.CompilerServices;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// What the theme editor's uniform list reads from a shader: its own uniforms with their sizes, not Marquee's.
public class ShaderUniformsTests
{
    const string Source = """
        uniform float time;
        uniform float2 resolution;
        uniform float heat;
        uniform float3 tint;
        uniform int count;
        half4 main(float2 p) { return half4(heat, tint.x, float(count), 1); }
        """;

    [Fact]
    public void A_shader_lists_its_own_uniforms_with_their_sizes_and_not_marquees()
    {
        using var folder = new TempFolder();
        var path = folder.File("glow.sksl");
        File.WriteAllText(path, Source);

        using var shader = ShaderUniforms.Compile(path);

        Assert.Null(shader.Problem);
        Assert.Equal(
            [new DeclaredUniform("heat", 1), new DeclaredUniform("tint", 3), new DeclaredUniform("count", 0)],
            ShaderUniforms.Read(shader.Effect!));
    }

    [Fact]
    public void A_shader_that_does_not_compile_says_why()
    {
        using var folder = new TempFolder();
        var path = folder.File("broken.sksl");
        File.WriteAllText(path, "half4 main(float2 p) { return nope; }");

        using var shader = ShaderUniforms.Compile(path);

        Assert.Null(shader.Effect);
        Assert.Contains("doesn't compile", shader.Problem);
    }

    [Fact]
    public void The_agents_theme_aurora_declares_heat()
    {
        using var shader = ShaderUniforms.Compile(Path.Combine(Repository(), "themes", "example-agents", "shaders", "aurora.sksl"));
        Assert.Contains(ShaderUniforms.Read(shader.Effect!), u => u.Name == "heat" && u.Components == 1);
    }

    static string Repository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
