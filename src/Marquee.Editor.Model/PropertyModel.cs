using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

public enum PropertyTargetKind { Widgets, Layer, Canvas, Style, Rule, Ring }

/// The properties panel for a selection, built from the catalog (ThemeSchema) and the file, so a key the catalog
/// gains shows here without editor code unless its kind needs a new control:
/// - Rows: the widget type's keys in the catalog's order, `type` and the common keys first. Its `style` is the Style
///   section, its `when` rules are Rules, and a group's `children` are the layers panel's. A key of another form of
///   the widget (OnlyFor, such as an arc shape's `sweep`) is left out, and of keys that exclude each other (a value
///   widget's `format` and `spans`), only the one the file sets, or the first.
/// - Style: the style keys the type draws with (AppliesTo), in sections (StyleSections), written in place in the
///   widget's style; StyleUse says which named style it starts from and what it sets over it.
/// - Several widgets: the keys every one of them has, a change writing to all of them as one step; their lists are
///   left out, as each widget's items differ.
///
/// A snapshot: build a new one after each change. Its commands (PropertyModel.Writing.cs) are applied with
/// EditorSession.Apply, and work on the document as it is then.
public sealed partial class PropertyModel
{
    readonly LayoutDocument _document;

    PropertyModel(LayoutDocument document, IReadOnlyList<WidgetRef> widgets)
    {
        _document = document;
        Target = PropertyTargetKind.Widgets;
        Widgets = widgets;
        StyleNames = StyleValues.Names(document);
        var types = widgets.Select(w => w.Type).Distinct().ToList();
        Type = types.Count == 1 ? types[0] : null;
        if (widgets.Count == 0) return;

        Rows = WidgetRows(types);
        Style = Sections(StyleRows(widgets.Select(w => (w, w.Path.Child("style"))).ToList(), types, "style", []), types);
        if (widgets.Count == 1)
        {
            var style = widgets[0].Path.Child("style");
            StyleUse = new StyleUse(StyleValues.NameOf(document, style), StyleValues.Leaves(StyleValues.Own(document, style)));
            Rules = RuleSummaries.Of(document, widgets[0].Path);
        }
    }

    PropertyModel(LayoutDocument document, WidgetRef owner, PropertyTargetKind target, IReadOnlyList<SchemaBlock> blocks)
        : this(document, [])
    {
        Target = target;
        Type = owner.Type.Length == 0 ? null : owner.Type;
        var keys = blocks.SelectMany(block => block.Keys).ToList();
        Rows = blocks.SelectMany(block => block.Keys.Select(key =>
                new PropertyTarget(owner, owner.Path.Child(key.Name), key, owner.Path) { Siblings = keys, Groups = block }))
            .Where(property => target != PropertyTargetKind.Rule || Shows(property))
            .Select(property => Row(target == PropertyTargetKind.Canvas ? $"canvas.{property.Key.Name}" : property.Key.Name, [property]))
            .ToList();
        if (target == PropertyTargetKind.Rule && blocks[0].Id == "when")
        {
            var style = owner.Path.Child("style");
            var widget = WidgetRef.Owner(owner.Path.Parent!.Parent!, owner.Type);
            Style = Sections(StyleRows([(widget, style)], [owner.Type], "style", []), [owner.Type]);
            StyleUse = new StyleUse(StyleValues.NameOf(document, style), StyleValues.Leaves(StyleValues.Own(document, style)));
        }
    }

    PropertyModel(LayoutDocument document, string name) : this(document, [])
    {
        StyleCommands.RequireStyle(document, name);
        Target = PropertyTargetKind.Style;
        Type = "style";
        var path = YamlPath.Root.Child("styles").Child(name);
        var owner = WidgetRef.Owner(path, "style");
        var extends = ThemeSchema.Style.Key("extends")!;
        Rows = [Row("extends", [new PropertyTarget(owner, path.Child("extends"), extends, path)]) with
        {
            Choices = StyleNames.Where(other => other != name).ToList(),
        }];
        var allTypes = ThemeSchema.Widgets.Names.ToList();
        Style = Sections(StyleRows([(owner, path)], allTypes, "style", []), allTypes);
    }

    /// The properties of the selected widgets, background layer or named style, or the canvas when nothing is selected.
    public static PropertyModel For(EditorSession session) =>
        session.SelectedLayer is { } layer ? ForLayer(session.Document, layer)
        : session.SelectedStyle is { } style ? ForStyle(session.Document, style)
        : session.Selection.Count == 0 ? ForCanvas(session.Document)
        : Build(session.Document, session.Selection.Select(session.Widget).OfType<WidgetRef>().ToList());

    /// The common layer keys followed by the keys of its kind, in catalog order.
    public static PropertyModel ForLayer(LayoutDocument document, YamlPath layer)
    {
        var kind = new[] { "fill", "image", "shader", "video" }.FirstOrDefault(kind => document.Exists(layer.Child(kind)));
        List<SchemaBlock> blocks = [ThemeSchema.Block("layer.common")!];
        if (kind is not null) blocks.Add(ThemeSchema.Block($"layer.{kind}")!);
        return new(document, WidgetRef.Owner(layer, kind ?? ""), PropertyTargetKind.Layer, blocks);
    }

    /// The canvas's orientation and frame rate, at canvas.*.
    public static PropertyModel ForCanvas(LayoutDocument document) =>
        new(document, WidgetRef.Owner(YamlPath.Root.Child("canvas"), "canvas"), PropertyTargetKind.Canvas,
            [ThemeSchema.Block("canvas")!]);

    public static PropertyModel ForStyle(LayoutDocument document, string name) => new(document, name);

    /// A rule's one condition and its widget style or ring overrides, written at the rule's path.
    public static PropertyModel ForRule(LayoutDocument document, YamlPath rule)
    {
        var owner = RuleCommands.OwnerOf(document, rule);
        var ring = owner == RuleCommands.Ring;
        var type = ring ? "ring" : document.TryGetScalar(owner.Child("type"), out var written) ? written : "";
        return new(document, WidgetRef.Owner(rule, type), PropertyTargetKind.Rule,
            [ThemeSchema.Block(ring ? "ring.when" : "when")!]);
    }

    /// The ring's catalog keys, including defaults for fields it leaves out.
    public static PropertyModel ForRing(LayoutDocument document) =>
        new(document, WidgetRef.Owner(RuleCommands.Ring, "ring"), PropertyTargetKind.Ring, [ThemeSchema.Block("ring")!]);

    /// The properties of `widgets`, read from `document`.
    public static PropertyModel Build(LayoutDocument document, IReadOnlyList<WidgetRef> widgets) => new(document, widgets);

    public IReadOnlyList<WidgetRef> Widgets { get; }

    public PropertyTargetKind Target { get; }

    /// The selected widgets' shared type, a layer's kind, or "canvas"; null for a missing kind, mixed types or no widgets in Build.
    public string? Type { get; }

    public IReadOnlyList<PropertyRow> Rows { get; } = [];

    public IReadOnlyList<PropertySection> Style { get; } = [];

    /// For one widget: the named style it uses and the keys it sets over it ("meter + 2 overrides"). Null for several.
    public StyleUse? StyleUse { get; }

    /// The theme's named styles, in the file's order: the style dropdown's choices.
    public IReadOnlyList<string> StyleNames { get; }

    /// For one widget: its `when` rules in plain words, read-only.
    public IReadOnlyList<RuleSummary> Rules { get; } = [];

    /// The row named `name` (`size`, `needle.width`, `style.stroke.width`, `series[1].source`), or null.
    public PropertyRow? Row(string name) =>
        Rows.Concat(Style.SelectMany(section => section.Rows)).Select(row => row.Find(name)).FirstOrDefault(row => row is not null);

    List<PropertyRow> WidgetRows(IReadOnlyList<string> types)
    {
        var keys = ThemeSchema.WidgetKeys(types[0])
            .Where(key => types.All(type => ThemeSchema.WidgetKeys(type).Any(k => k.Name == key.Name)))
            .Where(key => key.Kind != SchemaKind.Style && key.Of is not ("when" or "widget"))
            .Where(key => Widgets.Count == 1 || key.Kind != SchemaKind.List);

        var rows = new List<PropertyRow>();
        foreach (var key in keys)
        {
            var targets = Widgets.Select(widget =>
            {
                var siblings = ThemeSchema.WidgetKeys(widget.Type);
                return new PropertyTarget(widget, widget.Path.Child(key.Name), siblings.First(k => k.Name == key.Name), widget.Path)
                {
                    Siblings = siblings,
                    Groups = ThemeSchema.Widget(widget.Type)!,
                };
            }).ToList();
            if (targets.All(Shows)) rows.Add(Row(key.Name, targets));
        }

        return rows;
    }

    /// A row for one key of each target, with its block's or list's rows under it.
    PropertyRow Row(string name, IReadOnlyList<PropertyTarget> targets)
    {
        var key = targets[0].Key;
        var values = targets.Select(target => Read(target.Path)).ToList();
        var written = values.Select(value => value is null ? null : YamlWriter.Flow(value)).Distinct().ToList();
        var isSet = values.All(value => value is not null);
        var defaults = targets.Select(target => target.Key.DefaultFor.GetValueOrDefault(target.Widget.Type) ?? target.Key.Default).Distinct().ToList();
        var mins = targets.Select(MinOf).Distinct().ToList();

        var row = new PropertyRow(name, key)
        {
            Min = mins.Count == 1 ? mins[0] : mins.Max(),
            Max = key.Max,
            Step = Step(key),
            Default = defaults.Count == 1 ? defaults[0] : null,
            Value = isSet && written.Count == 1 ? values[0] : null,
            IsSet = isSet,
            IsMixed = written.Count > 1,
            // A widget's type isn't changed in place: another type is another widget.
            IsEditable = !(key.Name == ThemeSchema.Widgets.Discriminator && targets[0].Block.Equals(targets[0].Widget.Path))
                && targets.All(target => Editable(target.Path)
                    && !(Target == PropertyTargetKind.Layer
                        && _document.TryGetScalar(target.Widget.Path.Child("locked"), out var value)
                        && bool.TryParse(value, out var locked) && locked)),
            Choices = key.Kind is SchemaKind.Enum or SchemaKind.FontWeight ? key.Values
                : key.Kind is SchemaKind.StyleName or SchemaKind.Style ? StyleNames
                : [],
            Targets = targets,
        };

        return key.Kind switch
        {
            SchemaKind.Block when ThemeSchema.Block(key.Of!) is { } block => row with { Children = BlockRows(name, targets, block) },
            SchemaKind.List when targets.Count == 1 && key.Of is { } of && ThemeSchema.Block(of) is { } block =>
                row with { Items = Items(name, targets[0], block) },
            _ => row,
        };
    }

    List<PropertyRow> BlockRows(string name, IReadOnlyList<PropertyTarget> targets, SchemaBlock block) =>
        block.Keys
            .Where(key => targets.All(target => Applies(key, target.Widget.Type)))
            .Select(key => Row($"{name}.{key.Name}", targets.Select(target =>
                new PropertyTarget(target.Widget, target.Path.Child(key.Name), key, target.Path) { Siblings = block.Keys, Groups = block })
                .ToList()))
            .ToList();

    /// A list's items, each with a row per key of its block; an item's `style` has the style keys under it.
    List<PropertyItem> Items(string name, PropertyTarget list, SchemaBlock block)
    {
        var count = _document.GetNode(list.Path) is { Kind: LayoutNodeKind.List } node ? node.Count : 0;
        return Enumerable.Range(0, count).Select(i =>
        {
            var path = list.Path.Item(i);
            var prefix = $"{name}[{i}]";
            var rows = block.Keys.Select(key =>
            {
                var target = new PropertyTarget(list.Widget, path.Child(key.Name), key, path) { Siblings = block.Keys, Groups = block };
                var row = Row($"{prefix}.{key.Name}", [target]);
                return key.Kind == SchemaKind.Style
                    ? row with { Children = StyleRows([(list.Widget, target.Path)], [list.Widget.Type], row.Name, key.StyleKeys) }
                    : row;
            }).ToList();
            return new PropertyItem(i, path, rows);
        }).ToList();
    }

    /// The style keys the owners' types draw with (or only `allowed`, when a key limits them), each written in place in
    /// its owner's style at `style`, with what its named style (and, for a widget, its groups) gives when it doesn't
    /// set it.
    List<PropertyRow> StyleRows(IReadOnlyList<(WidgetRef Widget, YamlPath Style)> owners, IReadOnlyList<string> types, string name,
        IReadOnlyList<string> allowed)
    {
        // What draws where the owner doesn't set a key: its named style, over what its groups pass a widget down.
        var named = owners.Select(owner =>
        {
            var style = StyleValues.Named(_document, StyleValues.NameOf(_document, owner.Style));
            return owner.Style == owner.Widget.Path.Child("style")
                ? StyleValues.Merge(StyleValues.FromGroups(_document, owner.Widget.Path), style)
                : style;
        }).ToList();
        var rows = new List<PropertyRow>();
        foreach (var key in ThemeSchema.Style.Keys.Where(key => key.Kind != SchemaKind.StyleName))
        {
            if (allowed.Count > 0 ? !allowed.Contains(key.Name) : Target != PropertyTargetKind.Style && !types.All(type => Applies(key, type))) continue;
            var targets = owners.Select(owner => StyleTarget(owner.Widget, owner.Style, [key.Name], key)).ToList();
            var row = Row($"{name}.{key.Name}", targets) with { Inherited = Inherited(named, [key.Name]) };
            if (key.Kind == SchemaKind.Block && ThemeSchema.Block(key.Of!) is { } block)
            {
                row = row with
                {
                    Children = block.Keys.Where(inner => Target == PropertyTargetKind.Style || types.All(type => Applies(inner, type))).Select(inner =>
                        Row($"{row.Name}.{inner.Name}", owners.Select(owner => StyleTarget(owner.Widget, owner.Style, [key.Name, inner.Name], inner)).ToList())
                            with { Inherited = Inherited(named, [key.Name, inner.Name]) }).ToList(),
                };
            }

            rows.Add(row);
        }

        return rows;
    }

    static PropertyTarget StyleTarget(WidgetRef widget, YamlPath style, IReadOnlyList<string> key, SchemaKey schemaKey)
    {
        var path = StyleValues.At(style, key);
        return new PropertyTarget(widget, path, schemaKey, path.Parent!) { Style = style, StyleKey = key };
    }

    /// The widget style's rows in their sections, in the sections' order, each section's rows in the table's order.
    static List<PropertySection> Sections(List<PropertyRow> rows, IReadOnlyList<string> types)
    {
        var font = ThemeSchema.Style.Key("font")!;
        var drawsText = types.All(type => Applies(font, type));
        var titles = StyleSections.All.Select(section => section.Title).Append(StyleSections.More).ToList();
        return rows
            .GroupBy(row => StyleSections.Of(row.Key.Name, drawsText))
            .OrderBy(group => titles.IndexOf(group.Key))
            .Select(group => new PropertySection(group.Key, group.OrderBy(row => StyleSections.Order(row.Key.Name, drawsText)).ToList()))
            .ToList();
    }

    /// The named style's value for a key, when every owner's gives the same.
    static YamlValue? Inherited(IReadOnlyList<YamlMap> named, IReadOnlyList<string> key)
    {
        var values = named.Select(style => StyleValues.Get(style, key)).ToList();
        return values.Select(value => value is null ? null : YamlWriter.Flow(value)).Distinct().Count() == 1 ? values[0] : null;
    }

    /// Whether the target's key belongs: its form's (OnlyFor) and, of keys that exclude each other, the chosen one.
    bool Shows(PropertyTarget target)
    {
        var key = target.Key;
        if (key.OnlyFor is { } only && ValueOrDefault(target, only.Key) != only.Value) return false;
        foreach (var group in new[] { target.Groups?.ExactlyOneOf ?? [], target.Groups?.AtMostOneOf ?? [] })
        {
            if (!group.Contains(key.Name)) continue;
            var chosen = group.FirstOrDefault(name => _document.Exists(target.Block.Child(name))) ?? group[0];
            if (chosen != key.Name) return false;
        }

        return true;
    }

    /// A key's lower bound for this target: a MinFor's when the widget is in that form, else its Min, and a size's 1
    /// when it has none (SchemaKind.Size).
    double? MinOf(PropertyTarget target) =>
        target.Key.MinFor is { } minFor && ValueOrDefault(target, minFor.Key) == minFor.Value ? minFor.Min
        : target.Key.Min ?? (target.Key.Kind == SchemaKind.Size ? 1 : null);

    /// The value of a single-valued sibling of the target's key, or its default.
    string? ValueOrDefault(PropertyTarget target, string sibling) =>
        _document.TryGetScalar(target.Block.Child(sibling), out var value)
            ? value
            : target.Siblings.FirstOrDefault(k => k.Name == sibling)?.Default;

    YamlValue? Read(YamlPath path)
    {
        try
        {
            return _document.ValueAt(path);
        }
        catch (LayoutEditException)
        {
            // A value that repeats itself through an alias reads as not set, and isn't editable.
            return null;
        }
    }

    /// The path can be edited, or, when the file hasn't it yet, the nearest node above it that it has can.
    bool Editable(YamlPath path)
    {
        var at = path;
        while (!at.IsRoot && !_document.Exists(at)) at = at.Parent!;
        return (!at.IsRoot || Target == PropertyTargetKind.Ring) && _document.IsEditable(at);
    }

    static bool Applies(SchemaKey key, string type) => key.AppliesTo.Count == 0 || key.AppliesTo.Contains(type);

    /// A slider's step: whole numbers by 1, fractions by a hundredth, multipliers by a twentieth, times by 10 ms.
    static double Step(SchemaKey key) => key.Kind == SchemaKind.Integer ? 1 : key.Unit switch
    {
        SchemaUnit.Fraction => 0.01,
        SchemaUnit.Multiplier => 0.05,
        SchemaUnit.Ms => 10,
        _ => 1,
    };
}
