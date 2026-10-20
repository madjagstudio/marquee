using YamlDotNet.Core;
using YamlDotNet.Core.Tokens;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// What the list and group edits share: an item's lines, an item's text to put elsewhere, and the splices that take an
/// item out of a list and put one in.
public sealed partial class LayoutDocument
{
    /// An item's text, ready to go into a list: Lines for a block list, with its dash at column 0 (`- type: text`, then
    /// `  at: [0, 0]`), or null when it can't be written there; OneLine for a `[ ]` list, or null when it takes more
    /// than one line. HasComments is true when Lines hold a comment, which OneLine can't.
    sealed record ItemText(IReadOnlyList<string>? Lines, string? OneLine, bool HasComments = false);

    /// Where an item goes in a list:
    /// - Block: a block list, with dashes at DashColumn and items Gap columns after them;
    /// - NewBlock: an empty value or `[]` under a key, which becomes a block list with its first item at DashColumn;
    /// - Flow: inside the brackets of a `[ ]` list, after Separator between items;
    /// - Fill: an empty value inside `{ }` or `[ ]`, which becomes `[item]`.
    enum TargetKind { Block, NewBlock, Flow, Fill }

    sealed record Target(TargetKind Kind, YamlStyle Style);

    /// How many items the list at `at` has, where an empty value (`children:`) is an empty list. Throws LayoutEditException
    /// for anything else.
    int ItemCount(Located at) => at.Node switch
    {
        YamlSequenceNode list => list.Children.Count,
        YamlScalarNode empty when YamlSplice.IsEmpty(empty) => 0,
        _ => throw new LayoutEditException($"{Name(at.Path)} isn't a list, so it can't take an item.", at.Line),
    };

    /// The node at `item`, which must be a list's item.
    Located LocateItem(YamlPath item)
    {
        if (item.Last is not { IsKey: false }) throw new LayoutEditException($"{Name(item)} isn't an item of a list.");
        return Locate(item);
    }

    /// "1 item", "3 items".
    static string Items(int count) => count == 1 ? "1 item" : $"{count} items";

    /// Where an item goes in the list at `at`. An empty `[]` under a key in block style becomes a block list when the
    /// item is better written there (`blockItem`: a mapping or list, which reads better there, or an item with
    /// comments, which only a block list can keep), and takes a single value inside its brackets.
    Target TargetOf(Located at, bool blockItem)
    {
        var underKey = at.Key is not null && !at.InFlow;
        return at.Node switch
        {
            YamlSequenceNode list when !YamlSplice.IsFlow(list) => new(TargetKind.Block, YamlStyle.Of(Body, list)),
            YamlSequenceNode { Children.Count: 0 } when blockItem && underKey => NewBlock(at.Key!),
            YamlSequenceNode list => new(TargetKind.Flow, YamlStyle.Of(Body, list)),
            _ when underKey => NewBlock(at.Key!),
            _ => new(TargetKind.Fill, YamlStyle.NewBlock(0)),
        };

        Target NewBlock(YamlNode key) => new(TargetKind.NewBlock, YamlStyle.NewBlock(Column((int)key.Start.Index) + ListIndent));
    }

    /// How many columns in from its key the file puts a block list's dashes.
    int ListIndent { get; }

    /// The lines of item `index` of a block list: from its dash's line, or from the comment lines right above it at the
    /// dash's column, which describe it (unless they start the file), to the end of its last line and the comment lines
    /// after it indented deeper than the dash, which are inside it. Start and End are line starts, or End is the end of
    /// a file without a last line break.
    (int Start, int End, int Dash) ItemLines(YamlSequenceNode list, int index)
    {
        var (start, dash) = ItemStart(list, index);
        var column = Column(dash);
        var item = YamlStyle.Item(list, index);
        var end = YamlSplice.LineStartAfter(Body, ContentEnd(dash + 1, Math.Max(dash + 1, YamlSplice.End(Body, item))));
        while (end < Body.Length && IsComment(end, c => c > column)) end = YamlSplice.NextLine(Body, end);
        return (start, end, dash);
    }

    /// Where ItemLines start, and the item's dash, without measuring where the item ends (which an alias at its end
    /// would stop).
    (int Start, int Dash) ItemStart(YamlSequenceNode list, int index)
    {
        var dash = YamlStyle.Dash(Body, YamlStyle.Item(list, index));
        var column = Column(dash);
        var start = YamlSplice.LineStart(Body, dash);
        var above = start;
        while (above > 0 && IsComment(YamlSplice.LineStart(Body, above - 1), c => c == column))
            above = YamlSplice.LineStart(Body, above - 1);
        return (above > 0 ? above : start, dash);
    }

    /// Item `index` of `list` as text to put elsewhere. From a block list it is its lines, comments and all, moved left
    /// so its dash is at column 0, and on one line its own text when that isn't a block mapping or list. From a `[ ]`
    /// list it is its text, when that is one line.
    ItemText TextOf(YamlSequenceNode list, int index)
    {
        var item = YamlStyle.Item(list, index);
        var start = (int)item.Start.Index;
        var own = Body[start..YamlSplice.End(Body, item)];
        var oneLine = own.Contains('\n') ? null : own;
        if (YamlSplice.IsFlow(list)) return new ItemText(oneLine is null ? null : ["- " + oneLine], oneLine);

        var (from, to, dash) = ItemLines(list, index);
        var block = item is YamlMappingNode or YamlSequenceNode && !YamlSplice.IsFlow(item);
        return new ItemText(ShiftLines(Body[from..to], -Column(dash)), block ? null : oneLine, HasComment(from, to));
    }

    /// True when a comment starts between `from` and `to`. YamlDotNet's scanner finds them, so a `#` inside quotes or a
    /// `|` text doesn't count.
    bool HasComment(int from, int to)
    {
        var scanner = new Scanner(new StringReader(Body), skipComments: false);
        while (scanner.MoveNext())
        {
            if (scanner.Current is not { } token || token.Start.Index >= to) return false;
            if (token is Comment && token.Start.Index >= from) return true;
        }

        return false;
    }

    /// The lines of `text`, without their line breaks, each moved `by` columns: right by adding spaces before it, left
    /// by taking them away. An empty line stays empty, and a line of spaces too short to lose them becomes empty.
    /// Throws SpliceException when a line with text on it hasn't the spaces to lose.
    static List<string> ShiftLines(string text, int by)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.Select(line => Shift(line, by)).ToList();
    }

    static string Shift(string line, int by)
    {
        if (line.Length == 0) return line;
        if (by >= 0) return Pad(by) + line;
        var spaces = line.Length - line.TrimStart(' ').Length;
        if (spaces >= -by) return line[-by..];
        return line.Trim().Length == 0 ? "" : throw new SpliceException("Its lines can't be moved left as far as the list's.");
    }

    /// An item's lines with its dash moved from column 0 to `dashColumn`.
    static IEnumerable<string> Indent(IEnumerable<string> lines, int dashColumn) => lines.Select(line => Shift(line, dashColumn));

    /// A block list item's lines for a fragment: the comment lines it starts with, at the dash's column, where comments
    /// that describe an item go (as ItemYaml gives them); then `-`, the next line `gap` columns after the dash and the
    /// later ones under it.
    static List<string> ItemLinesOf(YamlFragment fragment, int gap)
    {
        var comments = fragment.Lines.TakeWhile(line => line.TrimStart(' ').StartsWith('#')).Count();
        return [.. fragment.Lines.Take(comments),
            .. fragment.Lines.Skip(comments).Select((line, i) => i == 0 ? "-" + Pad(gap - 1) + line : Shift(line, gap))];
    }

    /// The splice that puts `text` into the list at `at`, a `target` there, before its item `before` (or after its
    /// last item when `before` is its count). Throws LayoutEditException when the item's text can't be written there.
    Splice Insertion(Located at, Target target, int before, ItemText text, YamlPath item)
    {
        switch (target.Kind)
        {
            case TargetKind.Block:
            {
                var list = (YamlSequenceNode)at.Node;
                var position = before < list.Children.Count ? ItemStart(list, before).Start : ItemLines(list, list.Children.Count - 1).End;
                return InsertLines(position, Indent(BlockLines(text, item), target.Style.DashColumn));
            }

            case TargetKind.NewBlock:
            {
                // After the key's colon, keeping its comment on its line: `children:` or `children: []`, then the item.
                var lines = Eol + string.Join(Eol, Indent(BlockLines(text, item), target.Style.DashColumn));
                var colonEnd = YamlSplice.ColonEnd(Body, at.Key!);
                var close = at.Node is YamlSequenceNode empty ? YamlSplice.End(Body, empty) : colonEnd;
                var lineEnd = YamlSplice.LineBreakAfter(Body, close);
                var last = lineEnd == Body.Length ? Eol : "";
                return close == colonEnd
                    ? Splice.Insert(lineEnd, lines + last)
                    : new Splice(colonEnd, lineEnd - colonEnd, CommentAfter(close) + lines + last);
            }

            case TargetKind.Flow:
            {
                var list = (YamlSequenceNode)at.Node;
                var one = OneLineOf(text, item);
                if (list.Children.Count == 0)
                {
                    var open = YamlSplice.ContentStart(Body, list) + 1;
                    return Splice.Insert(open, Body[open] == ']' ? one : " " + one);
                }

                var separator = target.Style.Separator;
                return before < list.Children.Count
                    ? Splice.Insert((int)YamlStyle.Item(list, before).Start.Index, one + separator)
                    : Splice.Insert(YamlSplice.End(Body, YamlStyle.Item(list, list.Children.Count - 1)), separator + one);
            }

            default:
                return YamlSplice.Fill(Body, (YamlScalarNode)at.Node, $"[{OneLineOf(text, item)}]");
        }
    }

    static IReadOnlyList<string> BlockLines(ItemText text, YamlPath item) => text.Lines ?? throw new LayoutEditException(
        $"{Name(item)} is written over several lines inside [ ], so the editor can't move it into a block list. Edit it in the file.");

    static string OneLineOf(ItemText text, YamlPath item)
    {
        if (text.HasComments)
            throw new LayoutEditException($"{Name(item)} has comments, which can't go inside [ ] with it. Edit it in the file.");
        return text.OneLine ?? throw new LayoutEditException(
            $"{Name(item)} is written over several lines, so it can't go inside [ ]. Edit it in the file.");
    }

    /// Inserts lines at a line start, or at the end of a file, after a line break if it has none at its end.
    Splice InsertLines(int at, IEnumerable<string> lines)
    {
        var text = string.Join(Eol, lines) + Eol;
        return Splice.Insert(at, at == Body.Length && Body.Length > 0 && Body[^1] != '\n' ? Eol + text : text);
    }

    /// The splices that take item `index` out of the list at `list`: from a block list, its lines (ItemLines) and a
    /// blank line as RemoveKey takes one; from a `[ ]` list, the item and the comma after it (or, for the last item,
    /// before it). Taking out the only item leaves `[]`, after the list's key.
    List<Splice> Removal(Located list, int index)
    {
        var node = (YamlSequenceNode)list.Node;
        if (YamlSplice.IsFlow(node)) return [RemoveFromFlowList(node, index)];

        var (start, end, _) = ItemLines(node, index);
        var lines = RemoveLines(start, end);
        if (node.Children.Count > 1) return [lines];
        if (list.Key is not { } key) throw new SpliceException("Its list would be left empty, with no key to write [] after.");
        return [Splice.Insert(YamlSplice.ColonEnd(Body, key), " []"), lines];
    }

    Splice RemoveFromFlowList(YamlSequenceNode list, int index)
    {
        var item = YamlStyle.Item(list, index);
        var start = (int)item.Start.Index;
        if (list.Children.Count == 1)
        {
            var open = YamlSplice.ContentStart(Body, list) + 1;
            return new Splice(open, YamlSplice.Closing(Body, YamlSplice.End(Body, item)) - open, "");
        }

        if (index < list.Children.Count - 1)
            return new Splice(start, (int)YamlStyle.Item(list, index + 1).Start.Index - start, "");
        return CommaAndItem(list, index);
    }

    /// Item `index` of a `[ ]` list (not the first) with the comma before it.
    Splice CommaAndItem(YamlSequenceNode list, int index)
    {
        var previous = YamlSplice.End(Body, YamlStyle.Item(list, index - 1));
        return new Splice(previous, YamlSplice.End(Body, YamlStyle.Item(list, index)) - previous, "");
    }

    /// Removals made one: where two take the same blank line, or meet, the text between goes once.
    static List<Splice> Merged(IEnumerable<Splice> removals)
    {
        var merged = new List<Splice>();
        foreach (var removal in removals.OrderBy(r => r.At))
        {
            if (merged.Count > 0 && removal.At <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new Splice(last.At, Math.Max(last.End, removal.End) - last.At, "");
            }
            else
            {
                merged.Add(removal);
            }
        }

        return merged;
    }

    static YamlTree Inserted(YamlTree? list, int index, YamlTree item) => list is YamlTreeList items
        ? items with { Items = [.. items.Items.Take(index), item, .. items.Items.Skip(index)] }
        : new YamlTreeList([item], "", ""); // an empty value, now a list

    static YamlTreeList Removed(YamlTree? list, int index)
    {
        var items = (YamlTreeList)list!;
        return items with { Items = items.Items.Where((_, i) => i != index).ToList() };
    }

    /// The path of the same node once the item at `removed` has gone from its list: an index past it, on the way, is one
    /// less. The path mustn't lead into the removed item.
    static YamlPath AfterRemoving(YamlPath path, YamlPath removed)
    {
        var list = removed.Parent!.Segments;
        var at = path.Segments;
        if (at.Count <= list.Count || !at.Take(list.Count).SequenceEqual(list) || at[list.Count].Index <= removed.Last!.Value.Index)
            return path;

        var result = YamlPath.Root;
        for (var i = 0; i < at.Count; i++)
        {
            var segment = at[i];
            result = segment.Key is { } key ? result.Child(key) : result.Item(i == list.Count ? segment.Index - 1 : segment.Index);
        }

        return result;
    }
}
