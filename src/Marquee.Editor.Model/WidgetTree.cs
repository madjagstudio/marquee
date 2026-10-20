using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// The loaded theme's widgets as WidgetRefs, found by path.
///
/// How a path is matched to its parsed widget: by walking `widgets`, and each group's `children`, in order. The parser
/// keeps a list's order and, for text that loads, drops nothing (a bad widget is an error, not a gap), so item i of a
/// list in the file is widget i of the parsed list; a list item that is an alias is one parsed widget too. The loaded
/// theme's spec keeps its hidden widgets (only its assets leave them out, and the renderer skips them), so each
/// WidgetRef's Spec is the very widget the renderer draws, which a drag's preview stands in for. Build checks each
/// item's `type` and that the file's lists have no more items than the spec, and throws if they disagree, rather than
/// let a command name the wrong widget.
sealed class WidgetTree
{
    readonly Dictionary<YamlPath, WidgetRef> _byPath = [];

    public static WidgetTree Empty { get; } = new();

    /// The top-level widgets, back to front.
    public IReadOnlyList<WidgetRef> Roots { get; private set; } = [];

    public WidgetRef? Find(YamlPath path) => _byPath.GetValueOrDefault(path);

    /// `spec` is the theme loaded from the document's text.
    public static WidgetTree Build(LayoutDocument document, ThemeSpec spec)
    {
        var tree = new WidgetTree();
        tree.Roots = tree.List(document, YamlPath.Root.Child("widgets"), spec.Widgets, SKPoint.Empty);
        return tree;
    }

    /// The widgets of the list at `list`, whose group's corner is at `origin` on the canvas.
    List<WidgetRef> List(LayoutDocument document, YamlPath list, IReadOnlyList<WidgetSpec> specs, SKPoint origin)
    {
        var widgets = new List<WidgetRef>(specs.Count);
        for (var i = 0; i < specs.Count; i++)
        {
            var path = list.Item(i);
            var spec = specs[i];
            var type = WidgetRef.TypeOf(spec);
            if (!document.TryGetScalar(path.Child("type"), out var written) || written != type)
                throw Mismatch(path);

            var bounds = spec.Bounds;
            bounds.Offset(origin);
            var children = spec is GroupWidget group
                ? List(document, path.Child("children"), group.Children, bounds.Location)
                : [];

            var widget = new WidgetRef(path, type, spec.Name ?? WidgetNames.Of(document, path, type), bounds, spec.Bounds,
                spec.Rotate, spec.Hidden, spec.Locked, document.IsEditable(path), spec, children);
            _byPath[path] = widget;
            widgets.Add(widget);
        }

        if (document.Exists(list.Item(specs.Count))) throw Mismatch(list.Item(specs.Count));
        return widgets;
    }

    static InvalidOperationException Mismatch(YamlPath at) =>
        new($"The loaded theme doesn't line up with layout.yaml at {at}, so its widgets can't be matched to the file.");
}
