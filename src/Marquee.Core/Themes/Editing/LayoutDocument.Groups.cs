using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// Wrapping a list's items in a new group, and unwrapping a group into its parent list.
public sealed partial class LayoutDocument
{
    const string Children = "children";

    /// Wraps the items at `indexes` of the list at `list` (any of them, in any order, not necessarily side by side) in a
    /// new group: a mapping of `groupHeaderYaml` (its keys, such as `type: group`, `at` and `size`, without `children`)
    /// and a `children` list holding the items in the order the list had them. The group goes where the first of them
    /// was. In a block list the group is written in block style, each item keeping its text and comments, one level
    /// deeper; in a `[ ]` list it is a `{ }` one-liner. A header written in the other style is rewritten by YamlWriter,
    /// which writes no tags, so such a header with a tag is refused.
    ///
    /// The items' `at` values stay as they were, but inside a group they count from its corner: the caller rewrites
    /// them afterwards, with SetScalar or ReplaceValue, to keep each item where it was on screen.
    public LayoutDocument WrapInGroup(YamlPath list, IReadOnlyList<int> indexes, string groupHeaderYaml) =>
        WrapInGroup(list, indexes, groupHeaderYaml, out _);

    /// WrapInGroup, also giving `moved`: where each item went in the new document, in the order of `indexes`
    /// (`moved[i]` is the item that was at `indexes[i]`), for the editor to select them and rewrite their `at` values.
    /// The group itself is `list` item `indexes.Min()`.
    public LayoutDocument WrapInGroup(YamlPath list, IReadOnlyList<int> indexes, string groupHeaderYaml, out IReadOnlyList<YamlPath> moved)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        var at = Locate(list);
        CheckEditable(at, inside: false);
        if (at.Node is not YamlSequenceNode sequence)
            throw new LayoutEditException($"{Name(list)} isn't a list, so it has no items to wrap.", at.Line);
        if (indexes.Count == 0) throw new LayoutEditException("Choose at least one item to wrap in a group.");
        if (indexes.Distinct().Count() < indexes.Count) throw new LayoutEditException("An item can't go in a group more than once.");
        foreach (var index in indexes)
        {
            if (index < 0 || index >= sequence.Children.Count)
                throw new LayoutEditException($"{Name(list)} has {Items(sequence.Children.Count)}, so there is no item {index}.", at.Line);
            CheckEditable(Locate(list.Item(index)), inside: true);
        }

        var header = Fragment(groupHeaderYaml);
        if (header.Tree is not YamlTreeMap headerMap)
            throw new LayoutEditException("The group's header must be a mapping of its keys, such as type: group.");
        if (headerMap.IndexOf(Children) >= 0)
            throw new LayoutEditException("The group's header can't have children: the items become them.");

        var chosen = indexes.Order().ToList();
        var children = list.Item(chosen[0]).Child(Children);
        moved = indexes.Select(index => children.Item(chosen.IndexOf(index))).ToList();
        return Edit(list, () =>
        {
            var splices = YamlSplice.IsFlow(sequence)
                ? WrapInFlow(sequence, list, chosen, header)
                : WrapInBlock(sequence, list, chosen, header);
            // Checked against the header as the caller wrote it: one restyled by YamlWriter that lost its tag is refused.
            return (splices, tree => YamlTree.Change(tree, list, l => Wrapped((YamlTreeList)l!, chosen, headerMap)));
        });
    }

    List<Splice> WrapInBlock(YamlSequenceNode list, YamlPath path, List<int> chosen, YamlFragment header)
    {
        var style = YamlStyle.Of(Body, list);
        if (!header.IsBlockCollection) header = Fragment(YamlWriter.Block((YamlMap)YamlValue.From(header.Tree), Step));

        // The group's lines with its dash at column 0, as for any item, then its children one list level in.
        var lines = ItemLinesOf(header, style.Gap);
        lines.Add(Pad(style.Gap) + Children + ":");
        foreach (var index in chosen)
            lines.AddRange(Indent(BlockLines(TextOf(list, index), path.Item(index)), style.Gap + ListIndent));

        var (start, end, _) = ItemLines(list, chosen[0]);
        var group = new Splice(start, end - start, string.Join(Eol, Indent(lines, style.DashColumn)) + Eol);
        var removals = Merged(chosen.Skip(1).Select(index =>
        {
            var (from, to, _) = ItemLines(list, index);
            return RemoveLines(from, to);
        }));
        return [group, .. removals];
    }

    List<Splice> WrapInFlow(YamlSequenceNode list, YamlPath path, List<int> chosen, YamlFragment header)
    {
        var model = (YamlMap)YamlValue.From(header.Tree);
        var items = chosen.Select(index => OneLineOf(TextOf(list, index), path.Item(index)));
        var entries = model.Entries.Count > 0 ? YamlWriter.FlowEntries(model) + ", " : "";
        var text = $"{{ {entries}{Children}: [{string.Join(", ", items)}] }}";

        var first = YamlStyle.Item(list, chosen[0]);
        var group = new Splice((int)first.Start.Index, YamlSplice.End(Body, first) - (int)first.Start.Index, text);
        // Each other item goes with the comma before it, so items side by side leave no comma behind.
        return [group, .. chosen.Skip(1).Select(index => CommaAndItem(list, index))];
    }

    /// The list with the chosen items gone and the group, holding them, where the first of them was.
    static YamlTreeList Wrapped(YamlTreeList list, List<int> chosen, YamlTreeMap header)
    {
        var group = header.With(Children, new YamlTreeList(chosen.Select(i => list.Items[i]).ToList(), "", ""));
        var items = list.Items.Select((item, i) => i == chosen[0] ? group : item).Where((_, i) => i == chosen[0] || !chosen.Contains(i));
        return list with { Items = items.ToList() };
    }

    /// Replaces the group at `group`, an item of a list, with its children, in order, keeping their text and comments,
    /// moved out to the list's indentation. The group's other keys go, and the comment lines right above it stay, now
    /// above its first child. A group with no children is removed, as RemoveItem removes it.
    ///
    /// The children's `at` values stay as they were, counted from the group's corner: the caller rewrites them
    /// afterwards, with SetScalar or ReplaceValue, adding the group's `at` to keep each child where it was on screen.
    public LayoutDocument UnwrapGroup(YamlPath group) => UnwrapGroup(group, out _);

    /// UnwrapGroup, also giving `moved`: each child's path in the new document, in order, for the editor to select them
    /// and rewrite their `at` values. Empty when the group had no children.
    public LayoutDocument UnwrapGroup(YamlPath group, out IReadOnlyList<YamlPath> moved)
    {
        var at = LocateItem(group);
        CheckEditable(at, inside: true);
        var children = at.Node is YamlMappingNode map
            ? map.Children.FirstOrDefault(e => e.Key is YamlScalarNode { Value: Children }).Value
            : null;
        if (!(children is YamlSequenceNode || (children is YamlScalarNode empty && YamlSplice.IsEmpty(empty))))
            throw new LayoutEditException($"{Name(group)} has no children list, so it isn't a group to unwrap.", at.Line);
        var list = group.Parent!;
        var index = group.Last!.Value.Index;
        var count = children is YamlSequenceNode items ? items.Children.Count : 0;
        moved = Enumerable.Range(index, count).Select(list.Item).ToList();
        if (children is not YamlSequenceNode { Children.Count: > 0 } childList) return RemoveItem(group);

        return Edit(group, () =>
        {
            var parent = (YamlSequenceNode)Locate(list).Node;
            var texts = Enumerable.Range(0, childList.Children.Count)
                .Select(i => (Text: TextOf(childList, i), Path: group.Child(Children).Item(i)))
                .ToList();
            Splice splice;
            if (YamlSplice.IsFlow(parent))
            {
                var node = YamlStyle.Item(parent, index);
                var start = (int)node.Start.Index;
                splice = new Splice(start, YamlSplice.End(Body, node) - start,
                    string.Join(YamlStyle.Of(Body, parent).Separator, texts.Select(t => OneLineOf(t.Text, t.Path))));
            }
            else
            {
                var dashColumn = YamlStyle.Of(Body, parent).DashColumn;
                var (_, end, dash) = ItemLines(parent, index);
                var start = YamlSplice.LineStart(Body, dash);
                var lines = texts.SelectMany(t => Indent(BlockLines(t.Text, t.Path), dashColumn));
                splice = new Splice(start, end - start, string.Join(Eol, lines) + Eol);
            }

            return ([splice], tree => YamlTree.Change(tree, list, l =>
            {
                var items = (YamlTreeList)l!;
                var groupTree = (YamlTreeMap)items.Items[index];
                var kids = ((YamlTreeList)groupTree.Entries[groupTree.IndexOf(Children)].Value).Items;
                return items with { Items = [.. items.Items.Take(index), .. kids, .. items.Items.Skip(index + 1)] };
            }));
        });
    }
}
