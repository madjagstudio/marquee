using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Sets the value at `Path`, as the properties panel does. A single value over a single value keeps its quoting
/// (SetScalar); anything else replaces the value whole, written in the theme's style (YamlWriter). A key the file
/// hasn't is added, with any mappings missing on the way to it: setting `widgets[2].style.stroke.width` on a widget
/// without a style adds `style: { stroke: { width: 2 } }`.
///
/// A list of mappings (spans, slices), which YamlWriter writes in block style under its key, is written on one line,
/// `[{ … }]`, where it goes inside `{ }` or `[ ]`: block style can't be spliced inside brackets.
public sealed record SetValue(YamlPath Path, YamlValue Value) : EditorCommand
{
    public override string Label => $"Set {Key(Path)}";

    public override CommandResult Run(CommandContext context)
    {
        var document = context.Document;
        if (document.GetNode(Path) is { } node)
        {
            return node.Kind == LayoutNodeKind.Scalar && Value is YamlText text
                ? document.SetScalar(Path, text.Value)
                : document.ReplaceValue(Path, Written(document, Path.Parent, Value));
        }

        // Up to the deepest mapping the file has, then the missing keys, outermost first, as one nested value.
        var at = Path;
        var missing = new List<string>();
        while (!at.IsRoot && !document.Exists(at))
        {
            if (at.Last is not { IsKey: true } segment) throw new LayoutEditException($"{at} isn't in the file.");
            missing.Insert(0, segment.Key!);
            at = at.Parent!;
        }

        if (missing.Count == 0) throw new LayoutEditException("The file is empty, so it has no value to set.");
        var value = Value;
        for (var i = missing.Count - 1; i > 0; i--) value = YamlMap.Empty.With(missing[i], value);
        return document.AddKey(at, missing[0], Written(document, at, value));
    }

    /// `value` as it goes into the node at `holder`: on one line inside `{ }` or `[ ]`, else as YamlWriter writes it.
    static string Written(LayoutDocument document, YamlPath? holder, YamlValue value) =>
        YamlWriter.NeedsBlock(value) && holder is not null && document.GetNode(holder) is { IsFlow: true }
            ? YamlWriter.Flow(value)
            : YamlWriter.Write(value);

    /// The last key on the path, which names what changed: `color` for `widgets[0].style.color`.
    internal static string Key(YamlPath path) =>
        path.Segments.LastOrDefault(segment => segment.IsKey).Key ?? "value";
}

/// Adds `Key`, set to `Value`, as the last entry of the mapping at `Mapping`.
public sealed record AddKey(YamlPath Mapping, string Key, YamlValue Value) : EditorCommand
{
    public override string Label => $"Add {Key}";

    public override CommandResult Run(CommandContext context) =>
        context.Document.AddKey(Mapping, Key, YamlWriter.Write(Value));
}

/// Removes the key at `Path` with its value: the properties panel's Reset, which returns the key to its default.
public sealed record ResetKey(YamlPath Path) : EditorCommand
{
    public override string Label => $"Reset {SetValue.Key(Path)}";

    public override CommandResult Run(CommandContext context) => context.Document.RemoveKey(Path);
}
