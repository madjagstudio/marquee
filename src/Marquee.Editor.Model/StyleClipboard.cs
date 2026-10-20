using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// Copy and paste style (Ctrl+Shift+C, Ctrl+Shift+V). Copy takes the style a widget draws with: what its groups pass
/// down (StyleValues.FromGroups), then its named style, flattened through `extends`, with what it sets in place over
/// it. Paste writes, onto each target, the copied keys its type draws with, in place, as overrides of whatever named
/// style it uses, all as one undo step. A key the target already draws the same is left as it is, so pasting a
/// widget's own style changes nothing.
public sealed class StyleClipboard
{
    /// The copied style, without `extends`; null until something is copied.
    public YamlMap? Style { get; private set; }

    /// Copies the style the widget at `widget` draws with.
    public void Copy(LayoutDocument document, WidgetRef widget) =>
        Style = StyleValues.Drawn(document, widget.Path);

    /// Copies the style of the session's first selected widget. False when nothing is selected.
    public bool Copy(EditorSession session)
    {
        if (session.Selection.Count == 0 || session.Widget(session.Selection[0]) is not { } widget) return false;
        Copy(session.Document, widget);
        return true;
    }

    /// The paste onto `targets` as one command. Throws InvalidOperationException when nothing has been copied.
    public EditorCommand Paste(IReadOnlyList<WidgetRef> targets)
    {
        var copied = Style ?? throw new InvalidOperationException("No style has been copied.");
        return new EditDocument("Paste style", document => targets.Aggregate(document, (d, target) =>
        {
            var style = target.Path.Child("style");
            var current = StyleValues.Drawn(d, target.Path);
            return StyleValues.Set(d, style, Writes(copied, target.Type, current));
        }));
    }

    /// Pastes onto the session's selection. False when nothing is copied or selected, or the paste changes nothing.
    public bool Paste(EditorSession session)
    {
        var targets = session.Selection.Select(session.Widget).OfType<WidgetRef>().ToList();
        if (Style is null || targets.Count == 0) return false;
        var before = session.Document.Text;
        session.Apply(Paste(targets));
        return session.Document.Text != before;
    }

    /// The copied keys `type` draws with, each block's keys one by one, less those `current` already has the same.
    static List<(IReadOnlyList<string> Key, YamlValue Value)> Writes(YamlMap copied, string type, YamlMap current)
    {
        var writes = new List<(IReadOnlyList<string>, YamlValue)>();
        foreach (var (name, value) in copied.Entries)
        {
            if (ThemeSchema.Style.Key(name) is not { } key || !Applies(key, type)) continue;
            if (key.Kind == SchemaKind.Block && value is YamlMap block && ThemeSchema.Block(key.Of!) is { } schema)
            {
                foreach (var (inner, innerValue) in block.Entries)
                {
                    if (schema.Key(inner) is { } innerKey && Applies(innerKey, type) && !Same(StyleValues.Get(current, [name, inner]), innerValue))
                        writes.Add(([name, inner], innerValue));
                }
            }
            else if (!Same(current[name], value))
            {
                writes.Add(([name], value));
            }
        }

        return writes;
    }

    static bool Applies(SchemaKey key, string type) => key.AppliesTo.Count == 0 || key.AppliesTo.Contains(type);

    static bool Same(YamlValue? a, YamlValue? b) => a is not null && b is not null && YamlWriter.Flow(a) == YamlWriter.Flow(b);
}
