using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;

namespace Marquee.Editor.Layers;

/// One row of the layers panel: a widget, a group or a background layer (LayerNode), indented by its depth in the
/// groups, with what the row shows (an icon by type, the name, hide and lock) and its state in the panel: selected,
/// expanded, renaming, and where a drag over it would drop.
public sealed class LayerItem : ObservableObject
{
    bool _isSelected;
    bool _isExpanded;
    bool _isRenaming;
    string _editName = "";
    DropPlace? _dropHint;

    public LayerItem(LayerNode node, int depth, bool isExpanded)
    {
        Node = node;
        Depth = depth;
        _isExpanded = isExpanded;
    }

    public LayerNode Node { get; }

    public YamlPath Path => Node.Path;

    public LayerKind Kind => Node.Kind;

    public string Name => Node.Name;

    /// The name comes from the file's `name:`; otherwise it is made up from what the widget shows, and shown in italics.
    public bool IsNamed => Node.IsNamed;

    public bool Hidden => Node.Hidden;

    public bool Locked => Node.Locked;

    /// False for a widget or layer that uses an anchor or alias: the panel shows it read-only.
    public bool IsEditable => Node.IsEditable;

    public bool IsGroup => Node.Kind == LayerKind.Group;

    public bool IsBackground => Node.Kind == LayerKind.Background;

    public int Depth { get; }

    /// The row's left margin: 16 units a level.
    public System.Windows.Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// The icon, a glyph by type.
    public string Glyph => Glyphs.For(Node);

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// A group's children are listed under it.
    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value);
    }

    /// The name is being edited in place.
    public bool IsRenaming
    {
        get => _isRenaming;
        set => Set(ref _isRenaming, value);
    }

    /// The text in the rename box.
    public string EditName
    {
        get => _editName;
        set => Set(ref _editName, value);
    }

    /// Where a widget dragged over this row would go; null when none is.
    public DropPlace? DropHint
    {
        get => _dropHint;
        set
        {
            if (Set(ref _dropHint, value))
            {
                Raise(nameof(DropAbove));
                Raise(nameof(DropBelow));
                Raise(nameof(DropInto));
            }
        }
    }

    public bool DropAbove => DropHint == DropPlace.Above;

    public bool DropBelow => DropHint == DropPlace.Below;

    public bool DropInto => DropHint == DropPlace.Into;

    /// The row's glyphs, in Segoe UI Symbol, which every Windows has.
    static class Glyphs
    {
        public static string For(LayerNode node) => node.Kind switch
        {
            LayerKind.Group => "▣",
            LayerKind.Background => node.Type switch
            {
                "image" => "▨",
                "video" => "▶",
                "shader" => "✦",
                _ => "▧",
            },
            _ => node.Type switch
            {
                "text" => "T",
                "value" => "#",
                "bar" => "▬",
                "gauge" => "◔",
                "graph" => "∿",
                "image" => "▨",
                "video" => "▶",
                "shape" => "◆",
                "donut" => "◎",
                _ => "•",
            },
        };
    }
}
