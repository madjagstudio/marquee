using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// One widget of the open theme, as the layers panel and the canvas see it.
/// - Path: where it is in layout.yaml (`widgets[2]`, `widgets[5].children[0]`), which every command names it by.
/// - Type: its `type`, so a text widget written with `spans:` is "text" too.
/// - Name: its `name:`, or one made up from its type and what it shows ("Gauge · cpu.temp").
/// - Bounds: its box in canvas pixels, its groups' corners added; LocalBounds is its box as `at` and `size` write it,
///   relative to its group. Neither turns with `rotate`, its own or a group's.
/// - Hidden and Locked: its own `hidden:` and `locked:`. A hidden group hides its children, and a locked one keeps
///   them from the mouse too, which the canvas works out from the tree.
/// - IsEditable: false when it, or anything in it or above it, uses an anchor or alias, so the editor shows it
///   read-only.
/// - Spec: the parsed widget, with its children; Children: the group's widgets, back to front, as in the file.
public sealed record WidgetRef(
    YamlPath Path,
    string Type,
    string Name,
    SKRect Bounds,
    SKRect LocalBounds,
    float Rotate,
    bool Hidden,
    bool Locked,
    bool IsEditable,
    WidgetSpec Spec,
    IReadOnlyList<WidgetRef> Children)
{
    /// A properties-only stand-in for a background layer or the canvas, with nothing to draw. An empty group supplies
    /// the required Spec without assets or a theme; this owner is never added to the widget tree.
    internal static WidgetRef Owner(YamlPath path, string type) =>
        new(path, type, type, SKRect.Empty, SKRect.Empty, 0, Hidden: false, Locked: false, IsEditable: true,
            Spec: new GroupWidget([]), Children: []);

    /// Where its group's corner is on the canvas (the canvas's own for a top-level widget): canvas pixels less this
    /// are the coordinates `at` is written in.
    public SKPoint Offset => new(Bounds.Left - LocalBounds.Left, Bounds.Top - LocalBounds.Top);

    public bool IsGroup => Spec is GroupWidget;

    /// The type a parsed widget is written with in layout.yaml.
    public static string TypeOf(WidgetSpec spec) => spec switch
    {
        TextWidget or SpansWidget => "text",
        ValueWidget => "value",
        BarWidget => "bar",
        GaugeWidget => "gauge",
        GraphWidget => "graph",
        ImageWidget => "image",
        VideoWidget => "video",
        ShapeWidget => "shape",
        DonutWidget => "donut",
        GroupWidget => "group",
        _ => throw new ArgumentException($"There is no type for {spec.GetType().Name}.", nameof(spec)),
    };
}
