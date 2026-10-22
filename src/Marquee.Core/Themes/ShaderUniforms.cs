using Marquee.Core.Themes.Parsing;
using SkiaSharp;
using System.Text.RegularExpressions;

namespace Marquee.Core.Themes;

/// A uniform a shader declares that a theme can bind: its name and how many floats it takes (1 to 4, 9 or 16); 0 for
/// one Marquee can't bind, such as an int.
public sealed record DeclaredUniform(string Name, int Components, string? Hint = null);

/// A compiled .sksl file for the theme editor: the effect, or why there is none. The caller disposes it.
public sealed class ShaderFile(SKRuntimeEffect? effect, string? problem) : IDisposable
{
    public SKRuntimeEffect? Effect { get; } = effect;
    public string? Problem { get; } = problem;
    public void Dispose() => Effect?.Dispose();
}

/// What the theme editor reads from a shader layer's file, compiled and checked as ShaderAssets loads it.
public static class ShaderUniforms
{
    static readonly int[] FloatSizes = [1, 2, 3, 4, 9, 16];

    public static ShaderFile Compile(string path)
    {
        var compiled = ShaderAssets.Compile(path);
        if (compiled is null) return new ShaderFile(null, "could not be read");
        return new ShaderFile(compiled.Effect, compiled.Effect is null ? compiled.Problem : null);
    }

    /// The uniforms the shader declares in its own order, without the ones Marquee sets (time, day, resolution).
    public static IReadOnlyList<DeclaredUniform> Read(SKRuntimeEffect effect, string? source = null)
    {
        var hints = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is not null)
        {
            // Ignore declarations inside block comments, preserving line boundaries for trailing hints.
            var declarations = Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*", match =>
                match.Value.StartsWith("//", StringComparison.Ordinal) ? match.Value :
                    Regex.Replace(match.Value, @"[^\r\n]", " "));
            foreach (Match match in Regex.Matches(declarations,
                @"(?m)^[ \t]*(?:layout\([^\r\n)]*\)[ \t]*)?uniform\s+\w+\s+(?<name>\w+)(?:[ \t]*\[[^\r\n\]]*\])?[ \t]*;[ \t]*//(?<hint>[^\r\n]*)"))
                hints[match.Groups["name"].Value] = match.Groups["hint"].Value.Trim();
        }
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        return effect.Uniforms
            .Where(name => !BackgroundParser.BuiltInUniforms.Contains(name))
            .Select(name => new DeclaredUniform(name, FloatSizes.FirstOrDefault(n => ShaderAssets.Fits(uniforms, name, n)),
                hints.GetValueOrDefault(name)))
            .ToList();
    }

    /// A frame's cost at the layer's scale on `canvas`, as the screen measures it when the theme loads.
    public static TimeSpan Cost(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas, int fps) =>
        ShaderCost.Measure(fps, () => ShaderCost.Estimate(effect, layer, canvas));
}
