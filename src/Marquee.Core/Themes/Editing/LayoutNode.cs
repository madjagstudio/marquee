namespace Marquee.Core.Themes.Editing;

/// What a node of layout.yaml is, as the editor needs to know it to choose an edit: whether `style` is there to take
/// AddKey or must be added, whether a list is `[ ]` or block, how many items it has for an insert at its end.
///
/// - Keys: a mapping's keys that a YamlPath can name (single values), in the file's order; empty for anything else.
/// - Count: a list's items or a mapping's entries (complex keys too); 0 for a single or empty value.
/// - Line: 1-based, the line of its key for a mapping's value, else the line it starts on.
/// - IsEditable: as LayoutDocument.IsEditable says, for the node and everything in it.
public sealed record LayoutNode(
    YamlPath Path, LayoutNodeKind Kind, bool IsFlow, IReadOnlyList<string> Keys, int Count, int Line, bool IsEditable);

public enum LayoutNodeKind
{
    /// A key with nothing after it (`children:`): an unset value, which InsertItem treats as an empty list and AddKey as
    /// an empty mapping.
    Empty,

    /// A single value: plain, quoted or a `|` or `>` text.
    Scalar,

    Mapping,

    List,
}
