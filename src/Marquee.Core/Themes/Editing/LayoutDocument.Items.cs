using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// Inserting, removing and moving a list's items: widgets, a group's children, rules, a position's two numbers.
public sealed partial class LayoutDocument
{
    /// Inserts `itemYaml` (written as for ReplaceValue) as item `index` of the list at `list`, from 0 to its count.
    ///
    /// In a block list it goes at the list's indentation, above the comment lines that describe the item it goes
    /// before, and a mapping is written in the style of its neighbours: a `- { … }` one-liner when the mappings either
    /// side are one-liners, block style when one isn't (or when it holds a list of mappings, as the examples write a
    /// widget with `when` rules), and as written when it has no mapping beside it. Written in another style, it keeps
    /// its values but not its comments or quoting (YamlWriter writes it), and one with a tag (`!t`) is refused.
    ///
    /// With `asWritten`, an item in a block list is written as the caller wrote it, only indented to fit: a `{ … }`
    /// one-liner stays one beside block items, and a block mapping stays one beside one-liners, each with its quoting
    /// and comments (`- { … }` reads the same next to any item). Paste and duplicate use it, as they keep a widget's YAML.
    ///
    /// In a `[ ]` list it goes inside the brackets, on one line. An empty value (`children:`) under a key becomes a
    /// block list, as does an empty `[]` under a key for a mapping or list; inside `{ }` or `[ ]` an empty value becomes
    /// `[item]`.
    public LayoutDocument InsertItem(YamlPath list, int index, string itemYaml, bool asWritten = false)
    {
        var at = Locate(list);
        CheckEditable(at, inside: false);
        var count = ItemCount(at);
        if (index < 0 || index > count)
            throw new LayoutEditException($"{Name(list)} has {Items(count)}, so a new one goes at 0 to {count}, not {index}.", at.Line);
        var fragment = Fragment(itemYaml);

        return Edit(list, () =>
        {
            var target = TargetOf(at, blockItem: fragment.Tree is not YamlTreeScalar);
            var block = target.Kind is TargetKind.Block or TargetKind.NewBlock;
            var written = block
                ? asWritten ? fragment : Styled(fragment, target.Kind == TargetKind.Block ? YamlStyle.OneLiners(Body, (YamlSequenceNode)at.Node, index) : null)
                : OneLine(fragment);
            var text = block ? new ItemText(ItemLinesOf(written, target.Style.Gap), null) : new ItemText(null, written.Lines[0]);
            // Checked against the item as the caller wrote it: one restyled by YamlWriter that lost its tag is refused.
            return ([Insertion(at, target, index, text, list)], tree => YamlTree.Change(tree, list, l => Inserted(l, index, fragment.Tree)));
        });
    }

    /// The fragment as InsertItem writes it in a block list, given whether its neighbours are one-liners.
    YamlFragment Styled(YamlFragment fragment, bool? oneLiners)
    {
        if (fragment.Tree is not YamlTreeMap || oneLiners is not { } oneLine) return fragment;

        var model = (YamlMap)YamlValue.From(fragment.Tree);
        if (oneLine && !YamlWriter.NeedsBlock(model))
            return fragment is { IsMultiLine: false, IsBlockCollection: false } ? fragment : Fragment(YamlWriter.Flow(model));
        return fragment.IsBlockCollection ? fragment : Fragment(YamlWriter.Block(model, Step));
    }

    /// The fragment on one line, for inside `{ }` or `[ ]`: as written when it reads the same there, else as YamlWriter
    /// writes it.
    static YamlFragment OneLine(YamlFragment fragment)
    {
        if (!fragment.IsMultiLine && !fragment.IsBlockCollection && ReadsTheSameInBrackets(fragment)) return fragment;
        return Fragment(YamlWriter.Flow(YamlValue.From(fragment.Tree)));
    }

    /// A plain `a, b` is one value on its own line and two inside brackets.
    static bool ReadsTheSameInBrackets(YamlFragment fragment)
    {
        try
        {
            return YamlFragment.Parse($"[{fragment.Lines[0]}]").Tree is YamlTreeList { Items: [var only] }
                && YamlTree.Same(only, fragment.Tree);
        }
        catch (SpliceException)
        {
            return false;
        }
    }

    /// Removes the item at `item` from its list, with the comment lines that belong to it, as RemoveKey takes a key's:
    /// the ones right above its dash at the dash's column, and the ones after it indented deeper; and a blank line when
    /// it had one above and below. From a `[ ]` list it takes the item and a comma. Removing the only item leaves `[]`.
    public LayoutDocument RemoveItem(YamlPath item)
    {
        var at = LocateItem(item);
        CheckEditable(at, inside: true);
        var list = item.Parent!;
        var index = item.Last!.Value.Index;

        return Edit(item, () => (Removal(Locate(list), index), tree => YamlTree.Change(tree, list, l => Removed(l, index))));
    }

    /// Moves the item at `item` to `index` of the list at `toList`: within its own list (reordering) or into another
    /// one, such as a group's `children` and out again. `toList` is named as the file is before the move, and `index`
    /// is where the item ends up, counted without it: from 0 to the list's count (less one within its own list).
    ///
    /// The item keeps its text and the comment lines that belong to it (RemoveItem says which), moved left or right to
    /// the new list's indentation. A block mapping or list can't go inside `[ ]`, nor can an item with comments, which
    /// would be lost there: an empty `[]` under a key in block style becomes a block list for it instead. An empty list
    /// or value otherwise takes it as InsertItem says, and the list it leaves, if it was its only item, becomes `[]`.
    /// It is one edit, checked as one.
    public LayoutDocument MoveItem(YamlPath item, YamlPath toList, int index) => MoveItem(item, toList, index, out _);

    /// MoveItem, also giving `moved`: the item's path in the new document, item `index` of `toList` as it is after the
    /// move (an index in a list that came after the item's in its own list is one less), for the editor's selection.
    public LayoutDocument MoveItem(YamlPath item, YamlPath toList, int index, out YamlPath moved)
    {
        var from = LocateItem(item);
        CheckEditable(from, inside: true);
        if (toList.Segments.Count >= item.Segments.Count && toList.Segments.Take(item.Segments.Count).SequenceEqual(item.Segments))
            throw new LayoutEditException($"{Name(item)} can't go inside itself.", from.Line);

        var to = Locate(toList);
        CheckEditable(to, inside: false);
        var list = item.Parent!;
        var current = item.Last!.Value.Index;
        var same = toList == list;
        var count = ItemCount(to) - (same ? 1 : 0);
        if (index < 0 || index > count)
        {
            throw new LayoutEditException(same
                ? $"{Name(toList)} has {Items(count + 1)}, so an item moved in it goes at 0 to {count}, not {index}."
                : $"{Name(toList)} has {Items(count)}, so a new one goes at 0 to {count}, not {index}.", to.Line);
        }

        moved = AfterRemoving(toList, item).Item(index);
        if (same && index == current) return this;

        // In the file as it is, the item goes before the one now at `before`.
        var before = same && index >= current ? index + 1 : index;
        return Edit(item, () =>
        {
            var text = TextOf((YamlSequenceNode)from.Parent!, current);
            var target = TargetOf(to, blockItem: from.Node is YamlMappingNode or YamlSequenceNode || text.HasComments);
            var removal = Removal(Locate(list), current);
            var insertion = Clear(Insertion(to, target, before, text, item), removal);
            return ([insertion, .. removal], tree => Moved(tree, item, toList, index));
        });
    }

    /// An insertion that lands inside a removal (the end of a list whose last item, and the blank line after it, are
    /// going) goes at the removal's end instead: the text between is gone either way.
    static Splice Clear(Splice insertion, IEnumerable<Splice> removals)
    {
        foreach (var removal in removals)
            if (removal.Length > 0 && insertion.At > removal.At && insertion.At < removal.End)
                return insertion with { At = removal.End };
        return insertion;
    }

    static YamlTree Moved(YamlTree? root, YamlPath item, YamlPath toList, int index)
    {
        var current = item.Last!.Value.Index;
        YamlTree? moved = null;
        var without = YamlTree.Change(root, item.Parent!, list =>
        {
            moved = ((YamlTreeList)list!).Items[current];
            return Removed(list, current);
        });
        return YamlTree.Change(without, AfterRemoving(toList, item), list => Inserted(list, index, moved!));
    }
}
