using System.Diagnostics.CodeAnalysis;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// Reading the file: a single value, and what a node is. The editor reads values for its panels here rather than from
/// the parsed theme, which has defaults and named styles filled in and can't say what the file itself holds.
public sealed partial class LayoutDocument
{
    /// The single value at `path` as LayoutParser reads it, without its quoting or tag: `30`, `#FF5A36`, the text of a
    /// `|` block, or "" for an empty value (`name:`). False when the file has no node there or it is a list or mapping.
    public bool TryGetScalar(YamlPath path, [NotNullWhen(true)] out string? value)
    {
        value = Resolve(path)?.Node is YamlScalarNode scalar ? scalar.Value ?? "" : null;
        return value is not null;
    }

    /// What the node at `path` is (LayoutNode says what it holds), or null when the file has none there.
    public LayoutNode? GetNode(YamlPath path)
    {
        if (Resolve(path) is not { } at) return null;

        var (kind, count, keys) = at.Node switch
        {
            YamlMappingNode map => (LayoutNodeKind.Mapping, map.Children.Count, ScalarKeys(map)),
            YamlSequenceNode list => (LayoutNodeKind.List, list.Children.Count, []),
            _ when YamlSplice.IsEmpty(at.Node) => (LayoutNodeKind.Empty, 0, []),
            _ => (LayoutNodeKind.Scalar, 0, Array.Empty<string>()),
        };
        var line = (int)(at.Key ?? at.Node).Start.Line;
        return new LayoutNode(path, kind, YamlSplice.IsFlow(at.Node), keys, count, line, IsEditable(path));
    }

    /// The whole value at `path` as the file says it, a list or mapping with everything in it, without quoting, layout
    /// or comments (as YamlValue.Parse reads a fragment): a gradient paint, or a style's own keys. Null when the file has
    /// no node there. TryGetScalar is the cheaper read of a single value. A value reached through an alias reads as what
    /// it names; one that names one of its own parts again with an alias throws LayoutEditException, as a YamlValue, a
    /// plain tree, can't hold it.
    public YamlValue? ValueAt(YamlPath path)
    {
        if (Resolve(path) is not { } at) return null;
        try
        {
            return YamlValue.From(YamlTree.From(at.Node)!);
        }
        catch (SpliceException)
        {
            throw new LayoutEditException(
                $"{Name(path)} repeats part of itself through an alias (*), which the editor can't show. Edit it in the file.",
                at.Line);
        }
    }

    /// The keys of the mapping at `mapping` whose values can be edited (IsEditable), in the file's order: what a
    /// property panel shows as editable when the widget as a whole isn't. Empty when there is no mapping there.
    public IReadOnlyList<string> EditableKeys(YamlPath mapping) =>
        Resolve(mapping)?.Node is YamlMappingNode map ? ScalarKeys(map).Where(key => IsEditable(mapping.Child(key))).ToArray() : [];

    /// The YAML text of the item at `item`, as InsertItem takes it, so the editor can copy, paste and duplicate an item
    /// with its comments and quoting: written from column 0, with `\n` line breaks. From a block list it is the item's
    /// lines without its dash, with the comment lines above it and inside it (RemoveItem says which); from a `[ ]` list,
    /// its own text. Throws LayoutEditException for a path that isn't a list's item, or an item whose text is
    /// elsewhere (an alias) or written in a form the editor can't read.
    public string ItemYaml(YamlPath item)
    {
        var at = LocateItem(item);
        var list = (YamlSequenceNode)at.Parent!;
        var index = item.Last!.Value.Index;
        try
        {
            if (YamlSplice.IsFlow(list))
            {
                var node = YamlStyle.Item(list, index);
                return Body[(int)node.Start.Index..YamlSplice.End(Body, node)].Replace("\r\n", "\n");
            }

            return string.Join("\n", ItemYamlLines(list, index));
        }
        catch (SpliceException ex)
        {
            throw new LayoutEditException($"The editor can't read {Name(item)}. {ex.Message} Copy it in the file.", at.Line);
        }
    }

    /// A block list item's lines (ItemLines), moved left so that what follows its dash is at column 0: the comment lines
    /// above the dash, at its column, stay at column 0; the dash goes, and with it its line when the item starts on the
    /// next one (YamlStyle.Dash allows nothing else there); the later lines move left as far as the item's first does,
    /// and a comment line short of that to column 0.
    IEnumerable<string> ItemYamlLines(YamlSequenceNode list, int index)
    {
        var (from, to, dash) = ItemLines(list, index);
        var start = (int)YamlStyle.Item(list, index).Start.Index;
        var dashLine = YamlSplice.LineStart(Body, dash);
        var sameLine = YamlSplice.LineStart(Body, start) == dashLine;
        var gap = sameLine ? start - dash : Column(start) - Column(dash);
        var lines = ShiftLines(Body[from..to], -Column(dash));
        var above = Body.AsSpan(from, dashLine - from).Count('\n');
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart(' ').Length;
            if (i < above) yield return line;
            else if (i > above) yield return Shift(line, line.TrimStart(' ').StartsWith('#') ? -Math.Min(gap, indent) : -gap);
            else if (sameLine) yield return line[gap..];
        }
    }

    static string[] ScalarKeys(YamlMappingNode map) =>
        map.Children.Keys.OfType<YamlScalarNode>().Select(key => key.Value ?? "").Where(key => key.Length > 0).ToArray();
}
