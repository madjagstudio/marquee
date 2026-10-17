using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// layout.yaml → ThemeSpec. Unknown keys are errors, so a typo never silently does nothing.
public static class LayoutParser
{
    public static ThemeSpec Parse(string yaml)
    {
        var root = Yaml.Load(yaml) ?? throw new ThemeException("layout.yaml is empty. It needs at least a canvas section.");
        var fields = Yaml.Map(root, "layout.yaml", "canvas", "styles", "background");

        var canvas = Canvas(fields.Required("canvas"));
        // Parsed (and its extends chains checked) even before any widget uses it, so a broken style always reports.
        _ = StyleTable.Parse(fields.Optional("styles"));
        var background = fields.Optional("background") is { } b ? BackgroundParser.Parse(b) : [];
        return new ThemeSpec(canvas, background);
    }

    static CanvasSpec Canvas(YamlNode node)
    {
        var fields = Yaml.Map(node, "canvas", "orientation", "fps");
        var orientation = Yaml.Choice(fields.Required("orientation"), "canvas.orientation",
            ("portrait", ThemeOrientation.Portrait), ("landscape", ThemeOrientation.Landscape));
        var fps = fields.Optional("fps") is { } f ? Yaml.Integer(f, "canvas.fps", 1, CanvasSpec.MaxFps) : CanvasSpec.DefaultFps;
        return new CanvasSpec(orientation, fps);
    }
}
