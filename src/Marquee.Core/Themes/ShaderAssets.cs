using SkiaSharp;

namespace Marquee.Core.Themes;

/// Compiles a shader layer's .sksl file and checks its uniforms against the layout's bindings, so mistakes surface
/// once, as warnings when the theme loads, and the render loop never meets them. A layer that can't run is skipped.
static class ShaderAssets
{
    /// Largest .sksl file a theme may use. Themes are shared; a real shader is a few kilobytes.
    public const int MaxShaderBytes = 64 * 1024;

    /// The compiled shader, or null when the layer is skipped: it can't be read or compiled, Marquee can't feed it, or
    /// `probe` says a frame of it would take longer than a frame at `fps` allows on `canvas`.
    public static SKRuntimeEffect? Load(string folder, ShaderLayer layer, List<string> warnings, SKSizeI canvas, int fps, ShaderProbe probe)
    {
        var path = layer.Path;
        if (!ThemeAssets.TryInsideFolder(folder, path, out var full))
            return Skip(warnings, $"Shader '{path}' is not a valid path");
        if (full is null)
            return Skip(warnings, $"Shader '{path}' is outside the theme folder");
        if (!File.Exists(full))
            return Skip(warnings, $"Shader '{path}' was not found");

        // One open, so a file replaced after the Exists check can't throw out of the load.
        string source;
        try
        {
            using var stream = File.OpenRead(full);
            if (stream.Length > MaxShaderBytes)
                return Skip(warnings, $"Shader '{path}' is larger than {MaxShaderBytes / 1024} KB");
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            source = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Skip(warnings, $"Shader '{path}' could not be read");
        }

        var effect = SKRuntimeEffect.CreateShader(source, out var errors);
        if (effect is null)
            return Skip(warnings, $"Shader '{path}' doesn't compile: {CompileErrors(errors)}");

        if (Problem(effect, layer, warnings) is { } problem)
        {
            effect.Dispose();
            return Skip(warnings, $"Shader '{path}' {problem}");
        }

        if (TooSlow(effect, layer, canvas, fps, probe) is { } slow)
        {
            effect.Dispose();
            return Skip(warnings, $"Shader '{path}' {slow}");
        }

        return effect;
    }

    /// Why the layer is too slow for the frame rate, or null. A slow test frame is measured again, and the faster of the
    /// two counts, so a hiccup elsewhere on the PC doesn't cost a theme its background.
    static string? TooSlow(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas, int fps, ShaderProbe probe)
    {
        var budget = TimeSpan.FromSeconds(1.0 / fps);
        var cost = probe(effect, layer, canvas);
        if (cost > budget) cost = Min(cost, probe(effect, layer, canvas));
        if (cost <= budget) return null;

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return $"would take about {cost.TotalMilliseconds.ToString("N0", invariant)} ms to render a frame, " +
            $"over the {budget.TotalMilliseconds.ToString("0", invariant)} ms budget at {fps} fps (lower its scale or the fps, or simplify it)";
    }

    static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    /// Why the layer can't run, or null. Bindings the shader doesn't use, and uniforms nothing binds, are warnings only.
    static string? Problem(SKRuntimeEffect effect, ShaderLayer layer, List<string> warnings)
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
                warnings.Add($"Shader '{layer.Path}' has no uniform '{binding.Name}'; that binding is ignored.");
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
            warnings.Add($"Shader '{layer.Path}' uniform '{name}' isn't bound in layout.yaml, so it stays 0.");

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

    static SKRuntimeEffect? Skip(List<string> warnings, string problem)
    {
        warnings.Add($"{problem}; the layer is skipped.");
        return null;
    }
}
