using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Editing;

/// What a YAML file says, without how it is written: values, keys in order, tags and anchors, but no quoting, layout
/// or comments. LayoutDocument checks each edit with it: the tree of the file before, changed the way the edit means,
/// must be the tree of the file after. An alias is kept as a reference to its anchor's name, not a copy of what it
/// points at, so a list holding an alias of itself is a finite tree.
abstract record YamlTree(string Tag, string Anchor)
{
    /// The tree of a parsed node; null for an empty file.
    public static YamlTree? From(YamlNode? node) => node is null ? null : Build(node, new HashSet<YamlNode>(ReferenceEqualityComparer.Instance));

    /// A scalar holding `value`, with no tag or anchor, as a new value written without either reads.
    public static YamlTree Text(string value) => new YamlTreeScalar(value, "", "");

    static YamlTree Build(YamlNode node, HashSet<YamlNode> seen)
    {
        var anchor = node.Anchor.IsEmpty ? "" : node.Anchor.Value;
        // YamlDotNet hands back the anchored node itself where an alias names it; the first time is the anchor.
        if (!seen.Add(node) && anchor.Length > 0) return new YamlTreeAlias(anchor);

        var tag = node.Tag.IsEmpty ? "" : node.Tag.Value;
        return node switch
        {
            YamlScalarNode scalar => new YamlTreeScalar(scalar.Value ?? "", tag, anchor),
            YamlSequenceNode list => new YamlTreeList(list.Children.Select(item => Build(item, seen)).ToList(), tag, anchor),
            YamlMappingNode map => new YamlTreeMap(
                map.Children.Select(entry => new YamlTreeEntry(Build(entry.Key, seen), Build(entry.Value, seen))).ToList(), tag, anchor),
            _ => throw new SpliceException("The file holds a kind of node the editor doesn't know."),
        };
    }

    /// True when this node or any under it has an anchor or is an alias.
    public bool UsesAnchors() => Anchor.Length > 0 || this switch
    {
        YamlTreeAlias => true,
        YamlTreeList list => list.Items.Any(item => item.UsesAnchors()),
        YamlTreeMap map => map.Entries.Any(entry => entry.Key.UsesAnchors() || entry.Value.UsesAnchors()),
        _ => false,
    };

    /// The same content. An empty mapping at the top counts as the same as an empty file, which is what removing the
    /// last key of a file leaves.
    public static bool SameDocument(YamlTree? a, YamlTree? b) => Same(EmptyAsNull(a), EmptyAsNull(b));

    static YamlTree? EmptyAsNull(YamlTree? tree) => tree is YamlTreeMap { Entries.Count: 0, Tag: "", Anchor: "" } ? null : tree;

    public static bool Same(YamlTree? a, YamlTree? b) => (a, b) switch
    {
        (null, null) => true,
        (YamlTreeScalar x, YamlTreeScalar y) => x == y,
        (YamlTreeAlias x, YamlTreeAlias y) => x == y,
        (YamlTreeList x, YamlTreeList y) => x.Tag == y.Tag && x.Anchor == y.Anchor && x.Items.Count == y.Items.Count
            && x.Items.Zip(y.Items).All(p => Same(p.First, p.Second)),
        (YamlTreeMap x, YamlTreeMap y) => x.Tag == y.Tag && x.Anchor == y.Anchor && x.Entries.Count == y.Entries.Count
            && x.Entries.Zip(y.Entries).All(p => Same(p.First.Key, p.Second.Key) && Same(p.First.Value, p.Second.Value)),
        _ => false,
    };

    /// The tree with the node at `path` changed by `change`, which gets null for the top of an empty file.
    public static YamlTree Change(YamlTree? root, YamlPath path, Func<YamlTree?, YamlTree> change) =>
        Change(root, path.Segments, 0, change);

    static YamlTree Change(YamlTree? node, IReadOnlyList<YamlPathSegment> path, int depth, Func<YamlTree?, YamlTree> change)
    {
        if (depth == path.Count) return change(node);

        var segment = path[depth];
        switch (node)
        {
            case YamlTreeMap map when segment.Key is { } key:
                var at = map.IndexOf(key);
                if (at < 0) break;
                var entries = map.Entries.ToList();
                entries[at] = entries[at] with { Value = Change(entries[at].Value, path, depth + 1, change) };
                return map with { Entries = entries };
            case YamlTreeList list when !segment.IsKey && segment.Index < list.Items.Count:
                var items = list.Items.ToList();
                items[segment.Index] = Change(items[segment.Index], path, depth + 1, change);
                return list with { Items = items };
        }

        throw new SpliceException("The edit's path isn't in the file.");
    }
}

sealed record YamlTreeScalar(string Value, string Tag, string Anchor) : YamlTree(Tag, Anchor);

sealed record YamlTreeAlias(string Name) : YamlTree("", "");

sealed record YamlTreeList(IReadOnlyList<YamlTree> Items, string Tag, string Anchor) : YamlTree(Tag, Anchor);

sealed record YamlTreeMap(IReadOnlyList<YamlTreeEntry> Entries, string Tag, string Anchor) : YamlTree(Tag, Anchor)
{
    public static YamlTreeMap Empty { get; } = new([], "", "");

    /// The index of the entry whose key is the scalar `key`, or -1.
    public int IndexOf(string key)
    {
        for (var i = 0; i < Entries.Count; i++)
            if (Entries[i].Key is YamlTreeScalar { Value: var name } && name == key) return i;
        return -1;
    }

    public YamlTreeMap With(string key, YamlTree value) => this with { Entries = [.. Entries, new(Text(key), value)] };

    public YamlTreeMap Without(string key)
    {
        var at = IndexOf(key);
        return this with { Entries = Entries.Where((_, i) => i != at).ToList() };
    }
}

readonly record struct YamlTreeEntry(YamlTree Key, YamlTree Value);
