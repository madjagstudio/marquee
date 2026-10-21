using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// The panel's writes, each one command, one undo step, for every selected widget at once. A value is checked against
/// the catalog first (Validate); a style key is written in the widget's style in place, turning a named style into
/// overrides on it (StyleValues).
public sealed partial class PropertyModel
{
    /// Why `value` can't go in `row`, checked for each selected widget's key, or null when it can.
    public string? Validate(PropertyRow row, YamlValue value)
    {
        if (!row.IsEditable) return NotEditable(row);
        return row.Targets.Select(target => PropertyValues.Check(target.Key, value, Context(target))).FirstOrDefault(error => error is not null);
    }

    /// Writes `value` to the row on every selected widget. Throws PropertyValueException, with the reason, for a value
    /// Validate refuses. `merge` makes a slider's run of changes to the row one undo step.
    ///
    /// Choosing a form (an arc shape's `shape`) also removes the keys only the old form takes (OnlyFor), which the
    /// parser would refuse.
    public EditorCommand Set(PropertyRow row, YamlValue value, bool merge = false)
    {
        if (Validate(row, value) is { } reason) throw new PropertyValueException(reason);
        var targets = row.Targets;
        return new EditDocument($"Set {row.Key.Name}", document => targets.Aggregate(document, (d, target) => Write(d, target, value)))
        {
            MergeKey = merge ? ("property", string.Join("|", row.Paths)) : null,
        };
    }

    /// Removes the row's key where a selected widget sets it, so it is its default again (or its named style's). A style
    /// key's block, or the style itself, goes too when that leaves it empty, and a style left with only its named style
    /// goes back to `style: name`. Throws PropertyValueException for a key the theme can't leave out (CanReset).
    public EditorCommand Reset(PropertyRow row)
    {
        if (!row.IsEditable) throw new PropertyValueException(NotEditable(row));
        if (!row.CanReset) throw new PropertyValueException($"{row.Key.Name} can't be left out, so it has no default to reset to");
        var targets = row.Targets;
        return new EditDocument($"Reset {row.Key.Name}", document => targets.Aggregate(document, (d, target) =>
            target.Style is { } style ? StyleValues.Remove(d, style, target.StyleKey) : EditDocument.Remove(d, target.Path)));
    }

    /// Returns each selected widget's style to its named style alone (`style: meter`), or removes it when there is none.
    public EditorCommand ClearOverrides() => ForEachStyle("Clear overrides", (document, style) => StyleValues.Clear(document, style));

    /// Makes each selected widget's style start from the named style `name`, keeping what it sets in place; null for no
    /// named style. Throws PropertyValueException for a name the theme's styles haven't.
    public EditorCommand PickStyle(string? name)
    {
        if (name is not null && !StyleNames.Contains(name)) throw new PropertyValueException($"There is no style named '{name}' in styles:.");
        return ForEachStyle(name is null ? "Use no style" : $"Use style {name}", (document, style) => StyleValues.Pick(document, style, name));
    }

    /// Adds an item at the end of a list: a copy of its last item, with its comments, or, for an empty list, the
    /// block's required keys.
    public EditorCommand AddItem(PropertyRow list)
    {
        if (!list.CanAdd) throw new PropertyValueException(list.IsEditable ? $"{list.Key.Name} can have at most {list.Key.MaxItems} items" : NotEditable(list));
        var target = list.Targets.Single();
        return new EditDocument($"Add {Singular(list.Key.Name)}", document =>
        {
            var count = document.GetNode(target.Path) is { Kind: LayoutNodeKind.List } node ? node.Count : 0;
            return count > 0
                ? document.InsertItem(target.Path, count, document.ItemYaml(target.Path.Item(count - 1)))
                : EditDocument.Set(document, target.Path, new YamlList([NewItem(document, target)]));
        });
    }

    /// Removes item `index` of a list, unless that would leave fewer than it needs.
    public EditorCommand RemoveItem(PropertyRow list, int index)
    {
        if (!list.CanRemove)
            throw new PropertyValueException(list.IsEditable ? $"{list.Key.Name} needs at least {list.Key.MinItems} {(list.Key.MinItems == 1 ? "item" : "items")}" : NotEditable(list));
        var path = list.Targets.Single().Path;
        return new EditDocument($"Remove {Singular(list.Key.Name)}", document => document.RemoveItem(path.Item(index)));
    }

    /// Moves item `from` of a list to `to`, its index once moved.
    public EditorCommand MoveItem(PropertyRow list, int from, int to)
    {
        if (!list.IsEditable) throw new PropertyValueException(NotEditable(list));
        var path = list.Targets.Single().Path;
        return new EditDocument($"Move {Singular(list.Key.Name)}", document => document.MoveItem(path.Item(from), path, to));
    }

    /// One text or value widget can become spans: it has none yet.
    public bool CanSplitIntoSpans =>
        Widgets.Count == 1 && Type is ("text" or "value") && !_document.Exists(Widgets[0].Path.Child("spans")) && Editable(Widgets[0].Path);

    /// Turns a text widget's `text`, or a value widget's `format`, into one span to start from, as one step.
    public EditorCommand SplitIntoSpans()
    {
        if (!CanSplitIntoSpans) throw new InvalidOperationException("Only one text or value widget without spans can be split into spans.");
        var widget = Widgets[0].Path;
        var from = Type == "text" ? "text" : "format";
        return new EditDocument("Split into spans", document =>
        {
            var text = document.TryGetScalar(widget.Child(from), out var value) ? value : null;
            var span = Type == "text" ? text ?? "" : SpanOf(text);
            return EditDocument.Set(EditDocument.Remove(document, widget.Child(from)), widget.Child("spans"),
                new YamlList([YamlMap.Empty.With("text", span)]));
        });
    }

    /// A value widget's format as a span's template: a .NET format is one already, and a helper goes after a `|`.
    static string SpanOf(string? format) => format switch
    {
        null => "{0}",
        _ when format.Contains("{0", StringComparison.Ordinal) => format,
        _ => $"{{0|{format}}}",
    };

    EditorCommand ForEachStyle(string label, Func<LayoutDocument, YamlPath, LayoutDocument> edit)
    {
        var styles = Widgets.Select(widget => widget.Path.Child("style")).ToList();
        foreach (var widget in Widgets)
            if (!Editable(widget.Path.Child("style"))) throw new PropertyValueException($"{widget.Name}'s style uses an anchor or alias: edit it in the file.");
        return new EditDocument(label, document => styles.Aggregate(document, edit));
    }

    /// One widget's write: in its style for a style key; else in place, removing the keys only another form takes.
    static LayoutDocument Write(LayoutDocument document, PropertyTarget target, YamlValue value)
    {
        if (target.Style is { } style) return StyleValues.Set(document, style, [(target.StyleKey, value)]);

        document = EditDocument.Set(document, target.Path, value);
        foreach (var other in target.Siblings)
        {
            if (other.OnlyFor is { } only && only.Key == target.Key.Name && value is YamlText text && text.Value != only.Value)
                document = EditDocument.Remove(document, target.Block.Child(other.Name));
        }

        return document;
    }

    /// A list's first item: its block's required keys, a source from the widget's own (or a sensor every PC has) and
    /// text for a template.
    static YamlMap NewItem(LayoutDocument document, PropertyTarget list)
    {
        var item = YamlMap.Empty;
        foreach (var key in ThemeSchema.Block(list.Key.Of!)!.Keys.Where(key => key.Required))
        {
            string? value = key.Kind switch
            {
                SchemaKind.Expression or SchemaKind.SensorKey =>
                    document.TryGetScalar(list.Widget.Path.Child("source"), out var source) ? source : "cpu.load",
                SchemaKind.Template => "Text",
                _ => key.Default,
            };
            if (value is not null) item = item.With(key.Name, value);
        }

        return item;
    }

    CheckContext Context(PropertyTarget target) =>
        new(StyleNames) { Min = MinOf(target), WithValue = target.Widget.Type == "value" };

    static string NotEditable(PropertyRow row) =>
        row.Key.Name == ThemeSchema.Widgets.Discriminator
            ? "A widget's type can't be changed; add a widget of the other type instead."
            : $"{row.Key.Name} uses an anchor (&) or alias (*): edit it in the file.";

    /// "series" is a series, "slices" a slice, "spans" a span: what undo calls one item.
    static string Singular(string list) => list switch
    {
        "series" => "series",
        _ when list.EndsWith('s') => list[..^1],
        _ => list,
    };
}
