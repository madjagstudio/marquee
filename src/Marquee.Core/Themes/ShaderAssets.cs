using Marquee.Core.Rendering;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// Compiles a shader layer's .sksl file and checks its uniforms against the layout's bindings, so mistakes surface
/// once, as warnings when the theme loads, and the render loop never meets them. A layer that can't run is skipped.
static class ShaderAssets
{
    /// Largest .sksl file a theme may use. Themes are shared; a real shader is a few kilobytes.
    public const int MaxShaderBytes = 64 * 1024;

    /// The compiled shader, or null when the layer is skipped: it can't be read or compiled, Marquee can't feed it, or
    /// `probe` says a frame of it would take longer than a frame at `fps` allows on `canvas`. With `assets` loading through
    /// a cache, the compiled shader and its cost come from there, and the cache owns the effect.
    public static SKRuntimeEffect? Load(string folder, ShaderLayer layer, List<ThemeWarning> warnings, SKSizeI canvas, int fps, ShaderProbe probe,
        ThemeAssets? assets = null)
    {
        var path = layer.Path;
        if (!ThemeAssets.TryInsideFolder(folder, path, out var full))
            return Skip(warnings, path, $"Shader '{path}' is not a valid path");
        if (full is null)
            return Skip(warnings, path, $"Shader '{path}' is outside the theme folder");
        if (!File.Exists(full))
            return Skip(warnings, path, $"Shader '{path}' was not found");

        var cache = assets?.Cache;
        var compiled = cache is null ? Compile(full) : cache.Shader(assets!, full, () => Compile(full));
        if (compiled is null)
            return Skip(warnings, path, $"Shader '{path}' could not be read");
        if (compiled.Effect is not { } effect)
            return Skip(warnings, path, $"Shader '{path}' {compiled.Problem}");

        // A cached effect may be drawn by other loads, and the cache disposes it; one of this load's own is disposed here.
        if (Problem(effect, layer, warnings) is { } problem)
        {
            if (cache is null) effect.Dispose();
            return Skip(warnings, path, $"Shader '{path}' {problem}");
        }

        var cost = cache is null
            ? Cost(effect, layer, canvas, fps, probe)
            : compiled.Cost(ShaderPainter.RenderSize(layer, canvas), fps, () => Cost(effect, layer, canvas, fps, probe));
        if (TooSlow(cost, fps) is { } slow)
        {
            if (cache is null) effect.Dispose();
            return Skip(warnings, path, $"Shader '{path}' {slow}");
        }

        return effect;
    }

    /// The file's shader compiled, or why it can't be; null when it can't be read. One open, so a file replaced after
    /// the Exists check can't throw out of the load.
    static CompiledShader? Compile(string full)
    {
        string source;
        try
        {
            using var stream = File.OpenRead(full);
            if (stream.Length > MaxShaderBytes)
                return new CompiledShader(null, $"is larger than {MaxShaderBytes / 1024} KB");
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            source = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var effect = SKRuntimeEffect.CreateShader(source, out var errors);
        return effect is null ? new CompiledShader(null, $"doesn't compile: {CompileErrors(errors)}") : new CompiledShader(effect, null);
    }

    /// What a frame of the layer costs. A slow test frame is measured again, and the faster of the two counts, so a
    /// hiccup elsewhere on the PC doesn't cost a theme its background.
    static TimeSpan Cost(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas, int fps, ShaderProbe probe)
    {
        var budget = TimeSpan.FromSeconds(1.0 / fps);
        var cost = probe(effect, layer, canvas);
        return cost > budget ? Min(cost, probe(effect, layer, canvas)) : cost;
    }

    /// Why a layer that costs `cost` a frame is too slow for the frame rate, or null.
    static string? TooSlow(TimeSpan cost, int fps)
    {
        var budget = TimeSpan.FromSeconds(1.0 / fps);
        if (cost <= budget) return null;

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return $"would take about {cost.TotalMilliseconds.ToString("N0", invariant)} ms to render a frame, " +
            $"over the {budget.TotalMilliseconds.ToString("0", invariant)} ms budget at {fps} fps (lower its scale or the fps, or simplify it)";
    }

    static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// Why the layer can't run, or null. Bindings the shader doesn't use, and uniforms nothing binds, are warnings only.
    static string? Problem(SKRuntimeEffect effect, ShaderLayer layer, List<ThemeWarning> warnings)
    {
        if (effect.Children.Count > 0)
            return $"declares child shaders ({string.Join(", ", effect.Children)}), which Marquee doesn't provide";

        using var uniforms = new SKRuntimeEffectUniforms(effect);
        if (!Fits(uniforms, "time", 1)) return "declares 'time' as something other than a float";
        if (!Fits(uniforms, "day", 1)) return "declares 'day' as something other than a float";
        if (!Fits(uniforms, "resolution", 2)) return "declares 'resolution' as something other than a float2";

        foreach (var binding in layer.Uniforms)
        {
            if (!uniforms.Contains(binding.Name))
            {
                warnings.Add(new($"Shader '{layer.Path}' has no uniform '{binding.Name}'; that binding is ignored.", layer.Path, Skipped: false));
                continue;
            }

            if (!Fits(uniforms, binding.Name, binding.Values.Count))
            {
                if (FitsInts(uniforms, binding.Name, binding.Values.Count))
                    return $"declares uniform '{binding.Name}' as an int; Marquee binds floats, so declare it as float (or float2…)";
                return $"can't take {binding.Values.Count} value{(binding.Values.Count == 1 ? "" : "s")} for uniform '{binding.Name}'; " +
                    "bind one value per component (3 for a float3)";
            }
        }

        foreach (var name in effect.Uniforms.Except(Parsing.BackgroundParser.BuiltInUniforms).Except(layer.Uniforms.Select(u => u.Name)))
            warnings.Add(new($"Shader '{layer.Path}' uniform '{name}' isn't bound in layout.yaml, so it stays 0.", layer.Path, Skipped: false));

        return null;
    }

    /// Whether the uniform (if the shader declares it) takes `count` floats.
    static bool Fits(SKRuntimeEffectUniforms uniforms, string name, int count)
    {
        if (!uniforms.Contains(name)) return true;
        try
        {
            uniforms[name] = count == 1 ? 0f : new float[count];
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// Whether the uniform takes `count` ints, which tells an int declaration from a float of the wrong size.
    static bool FitsInts(SKRuntimeEffectUniforms uniforms, string name, int count)
    {
        try
        {
            uniforms[name] = count == 1 ? 0 : new int[count];
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    const int MaxErrorChars = 300;

    /// Skia's compile output repeats the source line and a caret line after every error. Keeps the "error:" lines only,
    /// on one line, so the warning stays readable in the log and the preview banner.
    static string CompileErrors(string errors)
    {
        var lines = errors.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("error:", StringComparison.Ordinal));
        var text = string.Join("; ", lines);
        if (text.Length == 0) text = errors.Trim().ReplaceLineEndings(" ");
        return text.Length > MaxErrorChars ? text[..MaxErrorChars] + "…" : text;
    }

    static SKRuntimeEffect? Skip(List<ThemeWarning> warnings, string path, string problem)
    {
        warnings.Add(new($"{problem}; the layer is skipped.", path, Skipped: true));
        return null;
    }
}
