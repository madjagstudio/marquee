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

    /// The keys of the mapping at `mapping` whose values can be edited (IsEditable), in the file's order: what a
    /// property panel shows as editable when the widget as a whole isn't. Empty when there is no mapping there.
    public IReadOnlyList<string> EditableKeys(YamlPath mapping) =>
        Resolve(mapping)?.Node is YamlMappingNode map ? ScalarKeys(map).Where(key => IsEditable(mapping.Child(key))).ToArray() : [];

    static string[] ScalarKeys(YamlMappingNode map) =>
        map.Children.Keys.OfType<YamlScalarNode>().Select(key => key.Value ?? "").Where(key => key.Length > 0).ToArray();
}
