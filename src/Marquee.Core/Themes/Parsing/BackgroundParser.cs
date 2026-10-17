using System.Text.RegularExpressions;
using Marquee.Core.Expressions;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

static partial class BackgroundParser
{
    /// Uniforms Marquee sets on every shader; a theme can't bind them.
    public static readonly string[] BuiltInUniforms = ["time", "resolution"];

    const int MaxUniforms = 32;

    /// A float4x4 has 16 components, the most any SkSL uniform type has.
    const int MaxComponents = 16;

    public static IReadOnlyList<BackgroundLayer> Parse(YamlNode node) =>
        Yaml.List(node, "background").Select(Layer).ToList();

    public static ImageFit Fit(YamlNode node, string what) => Yaml.Choice(node, what,
        ("cover", ImageFit.Cover), ("contain", ImageFit.Contain), ("stretch", ImageFit.Stretch), ("none", ImageFit.None));

    static BackgroundLayer Layer(YamlNode node)
    {
        const string what = "background layer";
        var fields = Yaml.Map(node, what, "fill", "image", "shader", "video", "fit", "uniforms", "scale", "opacity", "blend");
        var kind = fields.OneOf("fill", "image", "shader", "video");
        if (kind is "video")
            throw Yaml.Error(fields.Required(kind), "video background layers arrive in a later version of Marquee; use fill, image or shader");

        foreach (var (key, only) in new[] { ("fit", "image"), ("uniforms", "shader"), ("scale", "shader") })
            if (kind != only && fields.Optional(key) is { } misplaced)
                throw Yaml.Error(misplaced, $"{key} only applies to {only} layers");

        var opacity = fields.Optional("opacity") is { } o ? Yaml.Float(o, $"{what} opacity", 0, 1) : 1f;
        var blend = fields.Optional("blend") is { } b ? Blend(b) : SKBlendMode.SrcOver;

        switch (kind)
        {
            case "fill":
                return new FillLayer(PaintParser.Paint(fields.Required("fill"), "fill"), opacity, blend);
            case "image":
                var fit = fields.Optional("fit") is { } f ? Fit(f, "fit") : ImageFit.Cover;
                return new ImageLayer(Yaml.Text(fields.Required("image"), "image"), fit, opacity, blend);
            default:
                var uniforms = fields.Optional("uniforms") is { } u ? Uniforms(u) : [];
                var scale = fields.Optional("scale") is { } s ? Yaml.Float(s, "scale", 0.1, 1) : 1f;
                return new ShaderLayer(Yaml.Text(fields.Required("shader"), "shader"), uniforms, scale, opacity, blend);
        }
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

    static SKBlendMode Blend(YamlNode node) => Yaml.Choice(node, "blend",
        ("normal", SKBlendMode.SrcOver), ("multiply", SKBlendMode.Multiply), ("screen", SKBlendMode.Screen),
        ("overlay", SKBlendMode.Overlay), ("darken", SKBlendMode.Darken), ("lighten", SKBlendMode.Lighten),
        ("add", SKBlendMode.Plus));

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
