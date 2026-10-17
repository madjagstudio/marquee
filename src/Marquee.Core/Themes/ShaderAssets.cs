using SkiaSharp;

namespace Marquee.Core.Themes;

/// Compiles a shader layer's .sksl file and checks its uniforms against the layout's bindings, so mistakes surface
/// once, as warnings when the theme loads, and the render loop never meets them. A layer that can't run is skipped.
static class ShaderAssets
{
    /// Largest .sksl file a theme may use. Themes are shared; a real shader is a few kilobytes.
    public const int MaxShaderBytes = 64 * 1024;

    public static SKRuntimeEffect? Load(string folder, ShaderLayer layer, List<string> warnings)
    {
        var path = layer.Path;
        if (!ThemeAssets.TryInsideFolder(folder, path, out var full))
            return Skip(warnings, $"Shader '{path}' is not a valid path");
        if (full is null)
            return Skip(warnings, $"Shader '{path}' is outside the theme folder");
        if (!File.Exists(full))
            return Skip(warnings, $"Shader '{path}' was not found");
        if (new FileInfo(full).Length > MaxShaderBytes)
            return Skip(warnings, $"Shader '{path}' is larger than {MaxShaderBytes / 1024} KB");

        string source;
        try
        {
            source = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Skip(warnings, $"Shader '{path}' could not be read");
        }

        var effect = SKRuntimeEffect.CreateShader(source, out var errors);
        if (effect is null)
            return Skip(warnings, $"Shader '{path}' doesn't compile: {errors.Trim().ReplaceLineEndings(" ")}");

        if (Problem(effect, layer, warnings) is { } problem)
        {
            effect.Dispose();
            return Skip(warnings, $"Shader '{path}' {problem}");
        }

        return effect;
    }

    /// Why the layer can't run, or null. Bindings the shader doesn't use, and uniforms nothing binds, are warnings only.
    static string? Problem(SKRuntimeEffect effect, ShaderLayer layer, List<string> warnings)
    {
        if (effect.Children.Count > 0)
            return $"declares child shaders ({string.Join(", ", effect.Children)}), which Marquee doesn't provide";

        using var uniforms = new SKRuntimeEffectUniforms(effect);
        if (!Fits(uniforms, "time", 1)) return "declares 'time' as something other than a float";
        if (!Fits(uniforms, "resolution", 2)) return "declares 'resolution' as something other than a float2";

        foreach (var binding in layer.Uniforms)
        {
            if (!uniforms.Contains(binding.Name))
            {
                warnings.Add($"Shader '{layer.Path}' has no uniform '{binding.Name}'; that binding is ignored.");
                continue;
            }

            if (!Fits(uniforms, binding.Name, binding.Values.Count))
                return $"can't take {binding.Values.Count} value{(binding.Values.Count == 1 ? "" : "s")} for uniform '{binding.Name}'; " +
                    "bind one value per component (3 for a float3)";
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

    static SKRuntimeEffect? Skip(List<string> warnings, string problem)
    {
        warnings.Add($"{problem}; the layer is skipped.");
        return null;
    }
}
