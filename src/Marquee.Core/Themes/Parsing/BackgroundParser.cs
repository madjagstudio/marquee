using System.Text.RegularExpressions;
using Marquee.Core.Expressions;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

static partial class BackgroundParser
{
    /// Uniforms Marquee sets on every shader; a theme can't bind them.
    public static readonly IReadOnlyList<string> BuiltInUniforms = ["time", "day", "resolution"];

    /// A theme is untrusted: every layer is drawn each frame, and each shader layer is compiled at load and may keep
    /// its own offscreen surface (up to ~3.6 MB), so neither can be stacked without limit.
    public const int MaxLayers = 16;
    public const int MaxShaderLayers = 4;

    /// Each video layer decodes on its own background thread and holds several decoded frames in memory, so two clips
    /// are the most a theme may play at once.
    public const int MaxVideoLayers = 2;

    /// Media Foundation decodes the first four; Skia decodes animated GIF and WebP.
    public static readonly IReadOnlyList<string> VideoExtensions = [".mp4", ".m4v", ".mov", ".wmv", ".gif", ".webp"];

    internal const int MaxUniforms = 32;

    /// A float4x4 has 16 components, the most any SkSL uniform type has.
    internal const int MaxComponents = 16;

    internal const string UniformNamePattern = "^[A-Za-z_][A-Za-z0-9_]*$";

    // The keys a layer accepts: one of Kinds, which says what the layer is, and the others, some of which only apply to
    // some kinds. ThemeSchema describes each, and its drift test reads these lists.
    internal static readonly string[] Kinds = ["fill", "image", "shader", "video"];
    internal static readonly string[] LayerKeys = [.. Kinds, "fit", "uniforms", "scale", "opacity", "blend", "hidden", "locked"];

    internal static readonly (string Key, string[] Kinds)[] KindOnly =
        [("fit", ["image", "video"]), ("uniforms", ["shader"]), ("scale", ["shader"])];

    /// A bad layer is reported and left out, and the next one is still checked.
    public static IReadOnlyList<BackgroundLayer> Parse(YamlNode node, ParseErrors errors)
    {
        var nodes = Yaml.List(node, "background");
        if (nodes.Count > MaxLayers)
            throw Yaml.Error(nodes[MaxLayers], $"a background can have at most {MaxLayers} layers");

        var layers = new List<BackgroundLayer>();
        var shaders = 0;
        var videos = 0;
        foreach (var layerNode in nodes)
        {
            if (errors.Recover(() => Layer(layerNode)) is not { } layer) continue;
            var (count, max, kind) = layer switch
            {
                ShaderLayer => (++shaders, MaxShaderLayers, "shader"),
                VideoLayer => (++videos, MaxVideoLayers, "video"),
                _ => (0, int.MaxValue, ""),
            };
            if (count > max)
            {
                // Only the first layer over a limit is reported: the ones after it are over for the same reason.
                if (count == max + 1)
                    errors.Report(Yaml.Error(layerNode, $"a background can have at most {max} {kind} layers"));
                continue;
            }

            layers.Add(layer);
        }

        return layers;
    }

    public static ImageFit Fit(YamlNode node, string what) => Yaml.Choice(node, what,
        ("cover", ImageFit.Cover), ("contain", ImageFit.Contain), ("stretch", ImageFit.Stretch), ("none", ImageFit.None));

    static BackgroundLayer Layer(YamlNode node)
    {
        var fields = Yaml.Map(node, "background layer", LayerKeys);
        return Content(fields) with
        {
            Hidden = fields.Optional("hidden") is { } hidden && Yaml.Bool(hidden, "hidden"),
            Locked = fields.Optional("locked") is { } locked && Yaml.Bool(locked, "locked"),
        };
    }

    /// The layer's kind and how it draws: every key but hidden and locked.
    static BackgroundLayer Content(Fields fields)
    {
        var what = fields.What;
        var kind = fields.OneOf(Kinds);

        foreach (var (key, kinds) in KindOnly)
            if (!kinds.Contains(kind) && fields.Optional(key) is { } misplaced)
                throw Yaml.Error(misplaced, $"{key} only applies to {string.Join(" and ", kinds)} layers");

        var opacity = fields.Optional("opacity") is { } o ? Yaml.Float(o, $"{what} opacity", 0, 1) : 1f;
        var blend = fields.Optional("blend") is { } b ? Blend(b) : SKBlendMode.SrcOver;
        var fit = fields.Optional("fit") is { } f ? Fit(f, "fit") : ImageFit.Cover;

        switch (kind)
        {
            case "fill":
                return new FillLayer(PaintParser.Paint(fields.Required("fill"), "fill"), opacity, blend);
            case "image":
                return new ImageLayer(Yaml.Text(fields.Required("image"), "image"), fit, opacity, blend);
            case "video":
                return new VideoLayer(VideoPath(fields.Required("video")), fit, opacity, blend);
            default:
                var uniforms = fields.Optional("uniforms") is { } u ? Uniforms(u) : [];
                var scale = fields.Optional("scale") is { } s ? Yaml.Float(s, "scale", 0.1, 1) : 1f;
                return new ShaderLayer(Yaml.Text(fields.Required("shader"), "shader"), uniforms, scale, opacity, blend);
        }
    }

    /// The extension picks the decoder, so a clip no decoder reads is caught here, with its line, rather than at load.
    static string VideoPath(YamlNode node)
    {
        var path = Yaml.Text(node, "video");
        if (!VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw Yaml.Error(node, $"video must be a {string.Join(", ", VideoExtensions.SkipLast(1))} or {VideoExtensions[^1]} file, not '{path}'");
        return path;
    }

    static IReadOnlyList<ShaderUniform> Uniforms(YamlNode node)
    {
        if (node is not YamlMappingNode map)
            throw Yaml.Error(node, "uniforms must be a mapping of uniform names to values, e.g. { intensity: \"gpu.load / 100\" }");
        if (map.Children.Count > MaxUniforms)
            throw Yaml.Error(node, $"a shader layer can bind at most {MaxUniforms} uniforms");

        var uniforms = new List<ShaderUniform>();
        foreach (var (keyNode, value) in map.Children)
        {
            var name = keyNode is YamlScalarNode { Value: { } k } ? k : throw Yaml.Error(keyNode, "Uniform names must be plain names");
            if (!Identifier().IsMatch(name))
                throw Yaml.Error(keyNode, $"'{name}' is not a uniform name: use letters, digits and _, starting with a letter or _");
            if (BuiltInUniforms.Contains(name))
                throw Yaml.Error(keyNode, $"uniform '{name}' is set by Marquee and can't be bound");

            var components = value is YamlSequenceNode list ? list.Children.ToList() : [value];
            if (components.Count is 0 or > MaxComponents)
                throw Yaml.Error(value, $"uniform '{name}' needs 1 to {MaxComponents} values");
            uniforms.Add(new ShaderUniform(name, components.Select(c => Component(c, name)).ToList()));
        }

        return uniforms;
    }

    static Expression Component(YamlNode node, string uniform)
    {
        try
        {
            return Expression.Parse(Yaml.Text(node, $"uniform '{uniform}'"));
        }
        catch (ExpressionException ex)
        {
            throw Yaml.Error(node, $"uniform '{uniform}': {ex.Message}");
        }
    }

    /// A layer's blend mode, which a style's `blend` shares.
    internal static SKBlendMode Blend(YamlNode node) => Yaml.Choice(node, "blend",
        ("normal", SKBlendMode.SrcOver), ("multiply", SKBlendMode.Multiply), ("screen", SKBlendMode.Screen),
        ("overlay", SKBlendMode.Overlay), ("darken", SKBlendMode.Darken), ("lighten", SKBlendMode.Lighten),
        ("add", SKBlendMode.Plus));

    [GeneratedRegex(UniformNamePattern)]
    private static partial Regex Identifier();
}
