using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// layout.yaml → ThemeSpec. Unknown keys are errors, so a typo never silently does nothing.
public static class LayoutParser
{
    /// Stands in for a missing or invalid canvas in the partial spec a collecting parse returns, so the other sections
    /// can still be checked. Nothing draws that spec: it comes with the canvas error.
    static readonly CanvasSpec FallbackCanvas = new(ThemeOrientation.Landscape, CanvasSpec.DefaultFps);

    /// Throws the first error, as `marquee run` and hot reload want.
    public static ThemeSpec Parse(string yaml) => Parse(yaml, ParseErrors.Throwing);

    /// For `marquee validate`: adds every independent error to `errors`, in document order, and returns the spec built
    /// from the parts that were valid. Recovery is per section, style, background layer and widget (through groups), so
    /// one mistake doesn't hide the others, and a problem that only follows from one already reported isn't reported
    /// again. A YAML syntax error or a limit (layers, widgets, group depth) stops what it applies to. At most
    /// ParseErrors.MaxErrors are kept; then a last error, with no line, says how many more there were.
    public static ThemeSpec Parse(string yaml, List<ThemeException> errors)
    {
        var collected = ParseErrors.Collecting();
        var spec = Parse(yaml, collected);
        errors.AddRange(collected.InDocumentOrder());
        return spec;
    }

    static ThemeSpec Parse(string yaml, ParseErrors errors)
    {
        var empty = new ThemeSpec(FallbackCanvas, [], []);
        var fields = errors.Recover(() =>
        {
            var root = Yaml.Load(yaml) ?? throw new ThemeException("layout.yaml is empty. It needs at least a canvas section.");
            return Yaml.Map(root, "layout.yaml", errors, "canvas", "styles", "background", "widgets");
        });
        if (fields is null) return empty; // no tree, or a root that isn't a mapping: there is nothing more to check

        var canvas = errors.Recover(() => Canvas(fields.Required("canvas"))) ?? FallbackCanvas;
        var styles = StyleTable.Parse(fields.Optional("styles"), errors);
        var background = fields.Optional("background") is { } b ? errors.Recover(() => BackgroundParser.Parse(b, errors)) ?? [] : [];
        var widgets = fields.Optional("widgets") is { } w ? errors.Recover(() => WidgetParser.ParseList(w, styles, "widgets", errors)) ?? [] : [];
        return new ThemeSpec(canvas, background, widgets);
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
