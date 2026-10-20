using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// How a list in layout.yaml writes its items, read from the list, so an item LayoutDocument writes there matches its
/// neighbours:
/// - a block list: the column of its dashes (DashColumn), and how far after the dash an item starts (Gap: 2 in
///   `- type: text`);
/// - a `[ ]` list: what goes between its items (Separator: `, `, `,`, or a comma and a line break);
/// - which items are `- { … }` one-liners (OneLiners).
sealed record YamlStyle(bool IsFlow, int DashColumn, int Gap, string Separator)
{
    /// What a list writes when the file shows nothing else: `- ` before an item, `, ` between items in `[ ]`.
    const int DefaultGap = 2;
    const string DefaultSeparator = ", ";

    /// The style of a block list with a new first item at `dashColumn`.
    public static YamlStyle NewBlock(int dashColumn) => new(false, dashColumn, DefaultGap, "");

    /// Throws SpliceException for a list the editor can't measure: an item reached through an alias, or a dash with
    /// more than spaces before it on its line (`- - a`).
    public static YamlStyle Of(string text, YamlSequenceNode list)
    {
        if (YamlSplice.IsFlow(list)) return new(true, 0, 0, SeparatorOf(text, list));

        // Every item of a block list has its dash in one column; an item may start on the line below its dash.
        var gap = DefaultGap;
        for (var i = 0; i < list.Children.Count; i++)
        {
            var item = Item(list, i);
            var dash = Dash(text, item);
            if (YamlSplice.LineStart(text, dash) != YamlSplice.LineStart(text, (int)item.Start.Index)) continue;
            gap = (int)item.Start.Index - dash;
            break;
        }

        return new(false, Column(text, Dash(text, Item(list, 0))), gap, "");
    }

    /// Item `index` of a list, refused (SpliceException) when it is an alias: YamlDotNet hands back the anchored node,
    /// whose text is elsewhere in the file.
    public static YamlNode Item(YamlSequenceNode list, int index) =>
        YamlSplice.NotAlias(list.Children[index], after: index == 0 ? list : list.Children[index - 1]);

    /// Where the dash of a block list's item is. Throws SpliceException when more than spaces come before the dash on
    /// its line, or more than spaces and line breaks between the dash and the item.
    public static int Dash(string text, YamlNode item)
    {
        var i = (int)item.Start.Index - 1;
        while (i >= 0 && text[i] is ' ' or '\r' or '\n') i--;
        var line = i < 0 ? 0 : YamlSplice.LineStart(text, i);
        if (i < 0 || text[i] != '-' || text.AsSpan(line, i - line).Trim(' ').Length > 0)
            throw new SpliceException("A list's item is written in a form the editor can't edit.");
        return i;
    }

    /// True for a `{ … }` mapping written on one line. One whose end can't be found (its last value is an alias) counts
    /// as not, so a new item beside it is written in block style.
    public static bool IsOneLiner(string text, YamlNode item)
    {
        if (item is not YamlMappingNode || !YamlSplice.IsFlow(item)) return false;
        var start = (int)item.Start.Index;
        try
        {
            return text.IndexOf('\n', start, YamlSplice.End(text, item) - start) < 0;
        }
        catch (SpliceException)
        {
            return false;
        }
    }

    /// Whether the mappings either side of a new item at `before` in a block list are `{ … }` one-liners: true when
    /// all of them are, false when one isn't, and null when neither neighbour is a mapping.
    public static bool? OneLiners(string text, YamlSequenceNode list, int before)
    {
        var neighbours = new[] { before - 1, before }
            .Where(i => i >= 0 && i < list.Children.Count)
            .Select(i => Item(list, i))
            .Where(item => item is YamlMappingNode)
            .ToList();
        return neighbours.Count == 0 ? null : neighbours.All(item => IsOneLiner(text, item));
    }

    /// How many columns in from its key the file puts a block list's dashes: the first such list's (2 in the example
    /// themes, 0 when the dashes sit under the key), or `fallback` when the file has none.
    public static int ListIndent(string text, YamlNode? root, int fallback) =>
        ListIndent(text, root, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance)) ?? fallback;

    static int? ListIndent(string text, YamlNode? node, HashSet<YamlNode> seen)
    {
        if (node is null || !seen.Add(node)) return null;
        switch (node)
        {
            case YamlMappingNode map:
                foreach (var (key, value) in map.Children)
                {
                    if (value is YamlSequenceNode { Children.Count: > 0 } list && !YamlSplice.IsFlow(list)
                        && list.Children[0].Start.Index > key.Start.Index && TryDash(text, list.Children[0]) is { } dash)
                        return Column(text, dash) - Column(text, (int)key.Start.Index);
                    if (ListIndent(text, value, seen) is { } indent) return indent;
                }

                return null;
            case YamlSequenceNode list:
                return list.Children.Select(item => ListIndent(text, item, seen)).FirstOrDefault(indent => indent is not null);
            default:
                return null;
        }
    }

    /// A list written in a form Dash refuses says nothing about the file's indentation.
    static int? TryDash(string text, YamlNode item)
    {
        try
        {
            return Dash(text, item);
        }
        catch (SpliceException)
        {
            return null;
        }
    }

    /// What goes between the last two items of a `[ ]` list, when it is a comma with spaces or line breaks; else `, `.
    static string SeparatorOf(string text, YamlSequenceNode list)
    {
        if (list.Children.Count < 2) return DefaultSeparator;
        var between = text[YamlSplice.End(text, Item(list, list.Children.Count - 2))..(int)Item(list, list.Children.Count - 1).Start.Index];
        return between.Count(c => c == ',') == 1 && between.All(c => c is ',' or ' ' or '\r' or '\n') ? between : DefaultSeparator;
    }

    static int Column(string text, int index) => index - YamlSplice.LineStart(text, index);
}
