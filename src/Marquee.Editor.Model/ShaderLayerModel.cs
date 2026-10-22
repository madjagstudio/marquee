using System.Globalization;
using Marquee.Core.Expressions;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Component text without YAML quoting. An empty Values list means the uniform is unbound and reads as zero.
public sealed record UniformRow(string Name, int Components, IReadOnlyList<string> Values, bool Declared, string? Hint = null);

/// A snapshot of a shader's declarations, bindings and scale. Compiled effects never outlive a read or cost probe.
public sealed class ShaderLayerModel
{
    readonly string? _path;
    readonly float _scale;

    ShaderLayerModel(string? path, float scale, string? problem, IReadOnlyList<UniformRow> rows)
    {
        _path = path;
        _scale = scale;
        Problem = problem;
        Rows = rows;
    }

    public string? Problem { get; }
    public IReadOnlyList<UniformRow> Rows { get; }

    public static ShaderLayerModel Read(EditorSession session, YamlPath layer)
    {
        var document = session.Document;
        var scale = document.TryGetScalar(layer.Child("scale"), out var text) &&
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 1f;
        if (!document.TryGetScalar(layer.Child("shader"), out var relative))
            return new(null, scale, "No shader file is selected.", []);
        if (!ThemeAssets.TryInsideFolder(session.Folder, relative, out var path))
            return new(null, scale, "The shader file is not a valid path.", []);
        if (path is null)
            return new(null, scale, "The shader file is outside the theme folder.", []);

        using var file = ShaderUniforms.Compile(path);
        if (file.Effect is null) return new(path, scale, file.Problem, []);

        string source;
        try { source = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A watcher may replace or remove the file between compilation and reading its hints.
            return new(path, scale, ex.Message, []);
        }
        var declared = ShaderUniforms.Read(file.Effect, source);
        var bindings = layer.Child("uniforms");
        var names = document.GetNode(bindings)?.Keys ?? [];
        var rows = declared.Select(uniform => new UniformRow(uniform.Name, uniform.Components,
            Values(document, bindings.Child(uniform.Name)), Declared: true, uniform.Hint)).ToList();
        foreach (var name in names.Except(declared.Select(uniform => uniform.Name), StringComparer.Ordinal))
        {
            var values = Values(document, bindings.Child(name));
            rows.Add(new UniformRow(name, values.Count, values, Declared: false));
        }
        return new(path, scale, null, rows);
    }

    static IReadOnlyList<string> Values(LayoutDocument document, YamlPath path)
    {
        if (document.TryGetScalar(path, out var scalar)) return [scalar];
        if (document.GetNode(path) is not { Kind: LayoutNodeKind.List } list) return [];
        return Enumerable.Range(0, list.Count)
            .Select(i => document.TryGetScalar(path.Item(i), out var component) ? component : "").ToArray();
    }

    internal static bool IsNumber(string component) =>
        double.TryParse(component, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    public static string? Check(string component)
    {
        if (IsNumber(component)) return null;
        try
        {
            Expression.Parse(component);
            return null;
        }
        catch (ExpressionException ex)
        {
            return ex.Message;
        }
    }

    /// Measures again, so a shader file edited since Read uses its current source. Uncompilable files have no cost.
    public CostHint Cost(SKSizeI canvas, int fps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);
        if (_path is null) throw new InvalidOperationException(Problem);
        using var file = ShaderUniforms.Compile(_path);
        if (file.Effect is null) throw new InvalidOperationException(file.Problem);
        var layer = new ShaderLayer(_path, [], _scale, 1, SKBlendMode.SrcOver);
        return new CostHint(ShaderUniforms.Cost(file.Effect, layer, canvas, fps), TimeSpan.FromSeconds(1.0 / fps));
    }
}

/// The same strict per-layer frame budget the screen uses to skip shaders.
public sealed record CostHint(TimeSpan PerFrame, TimeSpan Budget)
{
    public bool OverBudget => PerFrame > Budget;

    public string Text
    {
        get
        {
            var invariant = CultureInfo.InvariantCulture;
            var cost = PerFrame.TotalMilliseconds.ToString("N0", invariant);
            var budget = Budget.TotalMilliseconds.ToString("0", invariant);
            var fps = (1.0 / Budget.TotalSeconds).ToString("0", invariant);
            return OverBudget
                ? $"would take about {cost} ms a frame, over the {budget} ms budget at {fps} fps: the screen skips it. Lower its scale."
                : $"About {cost} ms a frame at this scale, within the {budget} ms budget at {fps} fps";
        }
    }
}
