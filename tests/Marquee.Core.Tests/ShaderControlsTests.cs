using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class ShaderControlsTests
{
    [Theory]
    [InlineData("aurora", "Aurora")]
    [InlineData("waves", "Slow waves")]
    [InlineData("plasma", "Plasma")]
    [InlineData("starfield", "Starfield")]
    [InlineData("drift", "Gradient drift")]
    [InlineData("nebula", "Nebula")]
    public void Shipped_background_has_controls_that_compile_and_draw(string file, string name)
    {
        var folder = Path.Combine(Repository(), "themes", "backgrounds");
        var source = File.ReadAllText(Path.Combine(folder, file + ".sksl"));
        var info = ShaderControls.Read(source);
        Assert.Equal(name, info.Name);
        Assert.Empty(info.Warnings);
        Assert.Contains(info.Controls, c => c.Uniform == "speed" && c.Min == .1 && c.Max == 3 && c.Default == "1");
        Assert.Contains(info.Controls, c => c.Uniform == "strength" && c.Min == 0 && c.Max == 1);
        Assert.InRange(info.Controls.Count(c => c.Kind == ControlKind.Colour), 1, 2);
        Assert.NotNull(info.Reacts);
        using var shader = ShaderUniforms.Compile(Path.Combine(folder, file + ".sksl"));
        Assert.Null(shader.Problem);
        var uniforms = ShaderUniforms.Read(shader.Effect!);
        foreach (var control in info.Controls)
            Assert.Contains(uniforms, u => u.Name == control.Uniform && u.Components == (control.Kind == ControlKind.Colour ? 3 : 1));
        Assert.Contains(uniforms, u => u.Name == info.Reacts.Uniform && u.Components == 1);
        foreach (Match match in Regex.Matches(source, @"\btime\s*\*\s*(\w+(?:\.\d+)?)"))
            Assert.True(match.Groups[1].Value == "speed" || double.TryParse(match.Groups[1].Value, out _));
        var bindings = string.Join("\n", info.Controls.Select(c => $"      {c.Uniform}: " +
            (c.Kind == ControlKind.Colour ? "[0.21, 0.82, 0.86]" : c.Default)));
        using var cache = new ThemeAssetCache();
        using var theme = ThemeLoader.Load(folder, $"canvas: {{ orientation: landscape }}\nbackground:\n  - shader: {file}.sksl\n    scale: 0.5\n    uniforms:\n{bindings}\n      {info.Reacts.Uniform}: 0.5\nwidgets: []\n", cache);
        Assert.Empty(theme.Warnings);
        using var renderer = new ThemeRenderer(theme);
        using var frame = renderer.Render(SensorSnapshot.Of(DateTimeOffset.Now, new Dictionary<string, SensorEntry>()), TimeSpan.FromSeconds(2));
        using var original = SKBitmap.FromImage(frame);
        using var bitmap = original.Resize(new SKImageInfo(192, 48), new SKSamplingOptions(SKFilterMode.Linear));
        Assert.True(bitmap.Pixels.Distinct().Take(2).Count() > 1);
    }

    [Fact]
    public void Malformed_duplicate_and_out_of_range_annotations_are_ignored_and_reported()
    {
        var info = ShaderControls.Read("// @name Test\n// @control speed number 0.1..3 default 1 \"Speed\"\n// @control speed number 0..2 default 1 \"Again\"\n// @control bad number 3..1 default 2 \"Bad\"\n// @control outside number 0..1 default 2 \"Outside\"\n// @control tint colour \"Colour\" default nope\n// @reacts\nuniform float x;\n// @name Not a header");
        Assert.Equal("Test", info.Name);
        Assert.Single(info.Controls);
        Assert.Equal(5, info.Warnings.Count);
        Assert.Null(info.Reacts);
    }

    [Fact]
    public void Example_aurora_is_the_shipped_aurora()
    {
        Assert.Equal(File.ReadAllText(Path.Combine(Repository(), "themes/backgrounds/aurora.sksl")),
            File.ReadAllText(Path.Combine(Repository(), "themes/example-agents/shaders/aurora.sksl")));
    }

    static string Repository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../.."));
}
