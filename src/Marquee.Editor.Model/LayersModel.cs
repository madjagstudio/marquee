using System.Globalization;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

public enum LayerKind { Widget, Group, Background }

/// Where a layer dragged in the panel is dropped, against the row it is dropped on: in front of it (above it in the
/// panel), behind it (below), or into it, a group, on top of its children.
public enum DropPlace { Above, Below, Into }

/// One row of the layers panel.
/// - Path: the widget's or background layer's place in layout.yaml.
/// - Type: a widget's `type`, or a background layer's kind (`fill`, `image`, `shader`, `video`).
/// - Name: its `name:`, or one made up from what it shows ("Gauge · cpu.temp", "Fill · #000000"); IsNamed says which.
/// - Hidden and Locked: its own `hidden:` and `locked:`.
/// - IsEditable: false when it uses an anchor or alias, so the panel can't change it.
/// - Children: a group's, front to back.
public sealed record LayerNode(
    YamlPath Path,
    LayerKind Kind,
    string Type,
    string Name,
    bool IsNamed,
    bool Hidden,
    bool Locked,
    bool IsEditable,
    IReadOnlyList<LayerNode> Children);

/// The layers panel's tree, from front to back as image editors list layers: the top row draws on top, so it is the
/// YAML's last widget. Groups hold their children, also front to back. The background layers come in a section of
/// their own, front to back too (the last layer is drawn last); E1 only hides and locks them (SetLayerFlag). Built from
/// the session after each change (Of); the panel's edits are commands: RenameLayer, SetLayerFlag, MoveLayer (Drop
/// works one out), GroupWidgets, UngroupWidgets, PasteWidgets, DuplicateWidgets and DeleteWidgets.
public sealed class LayersModel
{
    readonly Dictionary<YamlPath, LayerNode> _byPath = [];

    LayersModel(LayoutDocument document, IReadOnlyList<WidgetRef> widgets)
    {
        Widgets = Nodes(widgets);
        Background = BackgroundNodes(document);
    }

    public static LayersModel Of(EditorSession session) => new(session.Document, session.Widgets);

    /// The widgets, front to back.
    public IReadOnlyList<LayerNode> Widgets { get; }

    /// The background layers, front to back.
    public IReadOnlyList<LayerNode> Background { get; }

    /// The row for `path`, or null when there is none.
    public LayerNode? Find(YamlPath path) => _byPath.GetValueOrDefault(path);

    /// The move that drops the widget at `item` `place` the row at `target`; null when the drop goes nowhere: onto a
    /// background layer (whose order E1 doesn't change), into a widget that isn't a group, into the widget itself or a
    /// group inside it, or where it already is.
    public MoveLayer? Drop(YamlPath item, YamlPath target, DropPlace place)
    {
        if (Find(item) is not { Kind: not LayerKind.Background } || Find(target) is not { Kind: not LayerKind.Background } row)
            return null;
        if (target == item || LayerEdits.Inside(target, item)) return null;

        YamlPath? group;
        int index;
        if (place == DropPlace.Into)
        {
            if (row.Kind != LayerKind.Group) return null;
            group = target;
            index = row.Children.Count - (CanvasGeometry.GroupOf(item) == target ? 1 : 0);
        }
        else
        {
            group = CanvasGeometry.GroupOf(target);
            // The target's place in its list once the item has left it.
            var at = target.Last!.Value.Index;
            if (CanvasGeometry.GroupOf(item) == group && item.Last!.Value.Index < at) at--;
            index = place == DropPlace.Above ? at + 1 : at;
        }

        if (CanvasGeometry.GroupOf(item) == group && item.Last!.Value.Index == index) return null;
        return new MoveLayer(item, group, index);
    }

    List<LayerNode> Nodes(IReadOnlyList<WidgetRef> widgets) => widgets.Reverse().Select(Node).ToList();

    LayerNode Node(WidgetRef widget) => Add(new LayerNode(widget.Path, widget.IsGroup ? LayerKind.Group : LayerKind.Widget,
        widget.Type, widget.Name, widget.Spec.Name is not null, widget.Hidden, widget.Locked, widget.IsEditable,
        Nodes(widget.Children)));

    List<LayerNode> BackgroundNodes(LayoutDocument document)
    {
        var list = YamlPath.Root.Child("background");
        if (document.GetNode(list) is not { Kind: LayoutNodeKind.List, Count: var count }) return [];

        var layers = new List<LayerNode>(count);
        for (var i = count - 1; i >= 0; i--)
        {
            var path = list.Item(i);
            var kind = document.GetNode(path)?.Keys.FirstOrDefault(key => key is "fill" or "image" or "shader" or "video") ?? "";
            var name = document.TryGetScalar(path.Child("name"), out var written) && written.Length > 0 ? written : null;
            layers.Add(Add(new LayerNode(path, LayerKind.Background, kind, name ?? LayerName(document, path, kind),
                name is not null, Flag(document, path, "hidden"), Flag(document, path, "locked"), document.IsEditable(path), [])));
        }

        return layers;
    }

    /// A background layer without a `name:`: its kind and what it draws, "Fill · #000000", "Image · images/bg.png", or
    /// for a gradient its kind, "Fill · linear".
    static string LayerName(LayoutDocument document, YamlPath layer, string kind)
    {
        var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(kind.Length > 0 ? kind : "layer");
        var value = layer.Child(kind);
        var detail = kind.Length == 0 ? null
            : document.TryGetScalar(value, out var text) ? text
            : document.GetNode(value)?.Keys.FirstOrDefault();
        return detail is { Length: > 0 } ? $"{title} · {WidgetNames.Shortened(detail)}" : title;
    }

    static bool Flag(LayoutDocument document, YamlPath layer, string key) =>
        document.TryGetScalar(layer.Child(key), out var value) && value == "true";

    LayerNode Add(LayerNode node)
    {
        _byPath[node.Path] = node;
        return node;
    }
}
