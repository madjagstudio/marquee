using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

static class BackgroundParser
{
    public static IReadOnlyList<BackgroundLayer> Parse(YamlNode node) =>
        Yaml.List(node, "background").Select(Layer).ToList();

    public static ImageFit Fit(YamlNode node, string what) => Yaml.Choice(node, what,
        ("cover", ImageFit.Cover), ("contain", ImageFit.Contain), ("stretch", ImageFit.Stretch), ("none", ImageFit.None));

    static BackgroundLayer Layer(YamlNode node)
    {
        const string what = "background layer";
        var fields = Yaml.Map(node, what, "fill", "image", "shader", "video", "fit", "opacity", "blend");
        var kind = fields.OneOf("fill", "image", "shader", "video");
        if (kind is "shader" or "video")
            throw Yaml.Error(fields.Required(kind), $"{kind} background layers arrive in a later version of Marquee; use fill or image");

        var opacity = fields.Optional("opacity") is { } o ? Yaml.Float(o, $"{what} opacity", 0, 1) : 1f;
        var blend = fields.Optional("blend") is { } b ? Blend(b) : SKBlendMode.SrcOver;

        if (kind == "fill")
        {
            if (fields.Optional("fit") is { } fitNode)
                throw Yaml.Error(fitNode, "fit only applies to image layers");
            return new FillLayer(PaintParser.Paint(fields.Required("fill"), "fill"), opacity, blend);
        }

        var fit = fields.Optional("fit") is { } f ? Fit(f, "fit") : ImageFit.Cover;
        return new ImageLayer(Yaml.Text(fields.Required("image"), "image"), fit, opacity, blend);
    }

    static SKBlendMode Blend(YamlNode node) => Yaml.Choice(node, "blend",
        ("normal", SKBlendMode.SrcOver), ("multiply", SKBlendMode.Multiply), ("screen", SKBlendMode.Screen),
        ("overlay", SKBlendMode.Overlay), ("darken", SKBlendMode.Darken), ("lighten", SKBlendMode.Lighten),
        ("add", SKBlendMode.Plus));
}
