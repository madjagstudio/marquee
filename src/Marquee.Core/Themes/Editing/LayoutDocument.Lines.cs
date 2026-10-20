using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// Finding the node a line of the file belongs to.
public sealed partial class LayoutDocument
{
    /// The path of the deepest node whose text covers `line` (1-based), so an error LayoutParser reports at a line can
    /// be shown on its widget or property: `widgets[3].style.color`. A mapping's entry covers its key's line to its
    /// value's last line. A line inside a node but between its children (a comment or blank line) belongs to the node,
    /// and a line holding several of its children (`at: [0, 0]`) to the node holding them all. An alias's text is
    /// elsewhere in the file, so the search stops at it. The top of the file is the path of a line outside every node;
    /// null is for a line the file hasn't, or an empty file.
    public YamlPath? PathAt(int line)
    {
        if (_root is null || line < 1 || line > LineCount()) return null;

        var (path, node) = (YamlPath.Root, (YamlNode?)_root);
        while (node is not null)
        {
            var covering = ChildSpans(node, path).Where(child => child.First <= line && line <= child.Last).Take(2).ToList();
            if (covering.Count != 1) break;
            (path, node) = (covering[0].Path, covering[0].Node);
        }

        return path;
    }

    /// The children of a mapping or list, with the lines they cover; Node is null for an alias, whose text is
    /// elsewhere. A complex key (`? [a, b]`) has no path, so its entry is left out.
    IEnumerable<(YamlPath Path, YamlNode? Node, int First, int Last)> ChildSpans(YamlNode node, YamlPath path)
    {
        switch (node)
        {
            case YamlMappingNode map:
                foreach (var (key, value) in map.Children)
                {
                    if (key is not YamlScalarNode { Value: { Length: > 0 } name }) continue;
                    var first = (int)key.Start.Line;
                    yield return value.Start.Index > key.Start.Index
                        ? (path.Child(name), value, first, Math.Max(first, LastLine(value)))
                        : (path.Child(name), null, first, first);
                }

                break;
            case YamlSequenceNode list:
                YamlNode after = list;
                for (var i = 0; i < list.Children.Count; i++)
                {
                    var item = list.Children[i];
                    if (item.Start.Index > after.Start.Index) yield return (path.Item(i), item, (int)item.Start.Line, LastLine(item));
                    after = item;
                }

                break;
        }
    }

    /// The line a node's text ends on, without the blank lines a block scalar or collection takes after it. A
    /// collection that ends in an alias, which YamlSplice can't measure, ends on the last line its children reach.
    int LastLine(YamlNode node)
    {
        var start = (int)node.Start.Index;
        try
        {
            return LineOf(Math.Max(start, ContentEnd(start, YamlSplice.End(Text, node)) - 1));
        }
        catch (SpliceException)
        {
            return ChildSpans(node, YamlPath.Root).Select(child => child.Last).Append((int)node.Start.Line).Max();
        }
    }

    /// The 1-based line holding the character at `index`.
    int LineOf(int index) => Text.AsSpan(0, Math.Min(index, Text.Length)).Count('\n') + 1;

    /// How many lines the file has; a line break at the end doesn't start another.
    int LineCount() => Text.AsSpan().Count('\n') + (Text.Length > 0 && Text[^1] != '\n' ? 1 : 0);
}
