using System.Globalization;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// Widgets as clipboard text: YAML a person can paste into a theme's `widgets:` too. Copy writes the widgets as a block
/// list, each item as the file writes it, comments and quoting and all (LayoutDocument.ItemYaml), under a comment line
/// that says what it is. Items reads back widget YAML from the editor or from anywhere else: such a list, or a single
/// widget written as a mapping.
public static class WidgetClipboard
{
    /// What Copy writes first. A blank line follows it, so the first widget's own comments stay its own (comment lines
    /// that start a file describe the file).
    public const string Header = "# Marquee widgets";

    /// How far Paste and Duplicate place a copy down and right of where its YAML has it, in theme pixels.
    public const float PasteOffset = 20;

    /// The widgets at `widgets` as clipboard text, in the file's order, so they paste in the same order front to back.
    /// A widget inside another being copied goes with it. Throws LayoutEditException for a widget whose text the editor
    /// can't take (an alias).
    public static string Copy(LayoutDocument document, IReadOnlyList<YamlPath> widgets)
    {
        var lines = new List<string> { Header, "" };
        foreach (var path in LayerEdits.Outermost(widgets).OrderBy(path => path, FileOrder.Instance))
            lines.AddRange(ListItem(document.ItemYaml(path)));
        return string.Join("\n", lines) + "\n";
    }

    /// The YAML of each widget in clipboard `text`, as InsertItem takes it; null when the text isn't widgets: it must
    /// parse, and be a list of mappings, or one mapping, each with a `type` that names a widget.
    public static IReadOnlyList<string>? Items(string text)
    {
        LayoutDocument document;
        try
        {
            document = LayoutDocument.Open(text.Replace("\r\n", "\n"));
        }
        catch (LayoutEditException)
        {
            return null;
        }

        var root = YamlPath.Root;
        var items = document.GetNode(root) switch
        {
            { Kind: LayoutNodeKind.List, Count: > 0 } list => Enumerable.Range(0, list.Count).Select(root.Item).ToList(),
            { Kind: LayoutNodeKind.Mapping } => [root],
            _ => null,
        };
        if (items is null || !items.All(item => IsWidget(document, item))) return null;
        try
        {
            return items.Select(item => item.IsRoot ? document.Text : document.ItemYaml(item)).ToList();
        }
        catch (LayoutEditException)
        {
            return null;
        }
    }

    /// The widget YAML `item` with its `at` moved PasteOffset down and right, number by number so the rest of its text,
    /// comments too, stays as it was. YAML with no `at` (or one that isn't two numbers) is left as it is: the theme
    /// will say what is wrong when it loads.
    internal static string Offset(string item)
    {
        var document = LayoutDocument.Open(item);
        var at = YamlPath.Root.Child("at");
        for (var i = 0; i < 2; i++)
        {
            if (!document.TryGetScalar(at.Item(i), out var text)
                || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return item;
            document = document.SetScalar(at.Item(i), PlaceWidgets.Number(value + PasteOffset));
        }

        return document.Text;
    }

    /// An item's YAML (written from column 0) as a block list's item at column 0: the comment lines above it as they
    /// are, then `- ` before its first line and two spaces before the rest.
    static IEnumerable<string> ListItem(string yaml)
    {
        var first = true;
        foreach (var line in yaml.Split('\n'))
        {
            if (first && (line.Length == 0 || line.StartsWith('#')))
            {
                yield return line;
                continue;
            }

            yield return first ? "- " + line : line.Length == 0 ? line : "  " + line;
            first = false;
        }
    }

    static bool IsWidget(LayoutDocument document, YamlPath item) =>
        document.GetNode(item) is { Kind: LayoutNodeKind.Mapping }
        && document.TryGetScalar(item.Child("type"), out var type) && ThemeSchema.Widget(type) is not null;
}

/// Pastes clipboard widgets (WidgetClipboard.Items) on top of the list being worked in: the group at `Group`'s
/// children, or the top-level widgets. Each keeps its YAML, placed PasteOffset down and right. The pasted widgets
/// become the selection. Throws ArgumentException when the clipboard holds no widgets.
public sealed record PasteWidgets(string Clipboard, YamlPath? Group = null) : EditorCommand
{
    public override string Label => "Paste";

    public override CommandResult Run(CommandContext context)
    {
        var items = WidgetClipboard.Items(Clipboard)
            ?? throw new ArgumentException("The clipboard holds no widgets.", nameof(Clipboard));
        if (Group is not null && !context.RequireWidget(Group).IsGroup)
            throw new ArgumentException($"{Group} isn't a group.", nameof(Group));

        var list = LayerEdits.ListOf(Group);
        var document = context.Document;
        // A theme without a widgets list gains an empty one, which takes the first item in block style.
        if (!document.Exists(list)) document = document.AddKey(list.Parent!, list.Last!.Value.Key!, "[]");
        var count = document.GetNode(list)!.Count;
        var pasted = new List<YamlPath>();
        foreach (var item in items)
        {
            document = document.InsertItem(list, count, WidgetClipboard.Offset(item), asWritten: true);
            pasted.Add(list.Item(count++));
        }

        return new CommandResult(GroupFit.Around(document, pasted), pasted);
    }
}

/// Duplicates the widgets at `Widgets` (Ctrl+D): each copy, its YAML as it is placed PasteOffset down and right, goes
/// just in front of its widget, in the same list. A widget inside another being duplicated goes with it. The copies
/// become the selection, in the order of `Widgets`.
public sealed record DuplicateWidgets(IReadOnlyList<YamlPath> Widgets) : EditorCommand
{
    public override string Label => "Duplicate";

    public override CommandResult Run(CommandContext context)
    {
        var widgets = LayerEdits.Outermost(Widgets);
        foreach (var path in widgets) context.RequireWidget(path);

        // Last in the file first, so each insertion leaves the paths still to copy as they were; copies made already
        // move along as later ones go in before them.
        var document = context.Document;
        var copies = new Dictionary<YamlPath, YamlPath>();
        foreach (var path in widgets.OrderByDescending(path => path, FileOrder.Instance))
        {
            var (list, index) = (path.Parent!, path.Last!.Value.Index + 1);
            document = document.InsertItem(list, index, WidgetClipboard.Offset(document.ItemYaml(path)), asWritten: true);
            foreach (var done in copies.Keys.ToList()) copies[done] = LayerEdits.Shifted(copies[done], list, index, 1);
            copies[path] = list.Item(index);
        }

        var selected = widgets.Select(path => copies[path]).ToList();
        return new CommandResult(GroupFit.Around(document, selected), selected);
    }
}
