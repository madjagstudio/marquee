using System.Globalization;
using System.Text;
using Marquee.Core.Themes.Schema;

namespace Marquee.SchemaDocs;

/// docs/themes-reference.md: a section for each part of layout.yaml and a table for each block, in the order a theme is
/// usually written. A block that isn't a section of its own (a style's stroke, the ring's rules) follows the section that
/// uses it, one heading level down.
static class ReferenceWriter
{
    sealed record Section(string Title, string Help, SchemaBlock? Block, SchemaUnion? Union);

    static IReadOnlyList<Section> Sections =>
    [
        Of(ThemeSchema.Layout), Of(ThemeSchema.Canvas), Of(ThemeSchema.Style), Of(ThemeSchema.Gradient),
        Of(ThemeSchema.Background), Of(ThemeSchema.Widgets), Of(ThemeSchema.When), Of(ThemeSchema.Ring),
    ];

    static Section Of(SchemaBlock block) => new(block.Title, block.Help, block, null);

    static Section Of(SchemaUnion union) => new(union.Title, union.Help, null, union);

    public static string Write()
    {
        var md = new StringBuilder();
        md.Append($"""
            # Theme reference

            <!-- Generated from the theme schema catalog (src/Marquee.Core/Themes/Schema) by tools/Marquee.SchemaDocs.
                 Don't edit it by hand: change the catalog, then run `{GeneratedDocs.Command}`. -->

            Every key a theme's `layout.yaml` can have: what it takes, what it is when it's left out, and what it does.
            Marquee's theme catalog generates this page, and [`layout.schema.json`](layout.schema.json) too, which lets
            VS Code's YAML extension complete and check theme files.

            - **Unknown keys are errors**, so a typo never silently does nothing. `marquee validate` lists every mistake,
              with its line.
            - A **required** key must be there. A **default** is what a key is when it's left out; a key without one does
              nothing until it is set, unless its description says otherwise.
            - **Kinds of value**, such as colors, paints and expressions, are explained at the end.


            """);

        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in Sections)
        {
            if (section.Block is { } block)
                WriteBlock(md, block, block.Keys, level: 2, written);
            else
                WriteUnion(md, section.Union!, written);
        }

        WriteKinds(md);

        var missing = ThemeSchema.Blocks.Select(b => b.Id).Where(id => !written.Contains(id)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"The reference has no place for: {string.Join(", ", missing)}. "
                + "Make it a section in ReferenceWriter, or give it a key that uses it.");
        // The raw string above has this source file's line ends, which depend on how Git checked it out.
        return md.ToString().ReplaceLineEndings("\n");
    }

    static void WriteUnion(StringBuilder md, SchemaUnion union, HashSet<string> written)
    {
        md.Append($"## {union.Title}\n\n");
        if (UsedBy(union.Id) is { Length: > 0 } usedBy) md.Append($"{usedBy}\n\n");
        md.Append($"{union.Help}\n\n");
        var common = union.DiscriminatorKey is { } discriminator ? [discriminator, .. union.Common.Keys] : union.Common.Keys;
        if (common.Count > 0)
            WriteBlock(md, union.Common, common, level: 3, written);
        foreach (var variant in union.Variants)
            WriteBlock(md, variant, variant.Keys, level: 3, written);
    }

    static void WriteBlock(StringBuilder md, SchemaBlock block, IReadOnlyList<SchemaKey> keys, int level, HashSet<string> written)
    {
        written.Add(block.Id);
        md.Append($"{new string('#', level)} {block.Title}\n\n");
        if (UsedBy(block.Id) is { Length: > 0 } usedBy) md.Append($"{usedBy}\n\n");
        if (block.Help.Length > 0) md.Append($"{block.Help}\n\n");

        md.Append("| Key | Kind | Values | Default | What it does |\n");
        md.Append("|---|---|---|---|---|\n");
        foreach (var key in keys)
            md.Append($"| `{key.Name}` | {Cell(Kind(key))} | {Cell(Values(key))} | {Cell(Default(key))} | {Cell(Help(key))} |\n");
        md.Append('\n');

        if (block.ExactlyOneOf.Count > 0)
            md.Append($"Each needs exactly one of {Words(block.ExactlyOneOf, "and")}"
                + (block.AtLeastOneOf.Count > 0 ? $", and at least one of {Words(block.AtLeastOneOf, "and")}.\n\n" : ".\n\n"));
        else if (block.AtLeastOneOf.Count > 0)
            md.Append($"Each needs at least one of {Words(block.AtLeastOneOf, "and")}.\n\n");

        // Blocks only this one uses, such as a style's stroke, come next.
        foreach (var child in ThemeSchema.Blocks.Where(b => !written.Contains(b.Id) && !IsSection(b) && keys.Any(k => k.Of == b.Id)))
            WriteBlock(md, child, child.Keys, Math.Min(level + 1, 4), written);
    }

    static bool IsSection(SchemaBlock block) => Sections.Any(s => s.Block?.Id == block.Id);

    /// Where a block goes, from the keys that hold it: "Under `stroke:` in a [style](#styles)."
    static string UsedBy(string id)
    {
        var places = new List<string>();
        foreach (var parent in ThemeSchema.Blocks)
            foreach (var key in parent.Keys.Where(k => k.Of == id))
                places.Add($"`{key.Name}:` in {Link(parent.Title, parent.Id)}");
        foreach (var union in ThemeSchema.Unions)
            foreach (var block in union.Variants.Prepend(union.Common))
                foreach (var key in block.Keys.Where(k => k.Of == id))
                    places.Add($"`{key.Name}:` in {Link(block == union.Common ? union.Title : block.Title, block == union.Common ? union.Id : block.Id)}");
        return places.Count == 0 ? "" : $"Goes under {Words(places.Distinct().ToList(), "or")}.";
    }

    static string Kind(SchemaKey key)
    {
        var name = ThemeSchema.KindName(key.Kind);
        return key.Kind switch
        {
            SchemaKind.Number or SchemaKind.Integer when key.Unit != SchemaUnit.None => $"{name} ({Unit(key.Unit)})",
            SchemaKind.Block when key.Of is { } of => $"block: {Link(TitleOf(of), of)}",
            SchemaKind.List when key.Of is { } of => $"list of {Link(TitleOf(of), of)}",
            SchemaKind.List when key.ItemKind is { } item => $"list of {ThemeSchema.KindName(item)}s",
            SchemaKind.Map when key.Of is { } of => $"named {Link(TitleOf(of), of)}",
            _ => name,
        };
    }

    static string Unit(SchemaUnit unit) => unit switch
    {
        SchemaUnit.Px => "px",
        SchemaUnit.Degrees => "degrees",
        SchemaUnit.Ms => "ms",
        SchemaUnit.Fraction => "fraction",
        SchemaUnit.Percent => "%",
        SchemaUnit.Multiplier => "times",
        _ => "",
    };

    static string Values(SchemaKey key) => key.Kind switch
    {
        SchemaKind.Enum => Codes(key.Values),
        SchemaKind.FontWeight => $"{Range(key)}, or {Codes(key.Values)}",
        SchemaKind.List or SchemaKind.Map or SchemaKind.Uniforms => Count(key.MinItems, key.MaxItems),
        SchemaKind.ThemeFile when key.FileTypes.Count > 0 => Codes(key.FileTypes),
        _ => Range(key) + (key.MinFor is { } lower ? $", or {Number(lower.Min)} with `{lower.Key}: {lower.Value}`" : ""),
    };

    static string Range(SchemaKey key) => (key.Min, key.Max) switch
    {
        ({ } min, { } max) => $"{Number(min)} to {Number(max)}",
        ({ } min, null) => $"at least {Number(min)}",
        (null, { } max) => $"at most {Number(max)}",
        _ => "",
    };

    static string Count(int? min, int? max) => (min, max) switch
    {
        ({ } a, { } b) => $"{a} to {b}",
        ({ } a, null) => $"at least {a}",
        (null, { } b) => $"up to {b}",
        _ => "",
    };

    /// The key's help, and the form of its block it belongs to, if just one.
    public static string Help(SchemaKey key) =>
        key.OnlyFor is { } only ? $"{key.Help} Only with `{only.Key}: {only.Value}`." : key.Help;

    static string Default(SchemaKey key) => key.Required ? "required" : key.Default is { } value ? $"`{value}`" : "";

    static string TitleOf(string id) => ThemeSchema.Block(id)?.Title ?? ThemeSchema.Union(id)?.Title ?? id;

    static string Link(string title, string id)
    {
        // A union's common keys and its variants are headings of their own, but a link to the union reads better.
        var heading = ThemeSchema.Union(id)?.Title ?? ThemeSchema.Block(id)?.Title ?? title;
        return $"[{title}](#{Anchor(heading)})";
    }

    /// The anchor GitHub gives a heading: lower case, punctuation dropped, spaces as hyphens.
    static string Anchor(string heading) =>
        new string(heading.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Replace(' ', '-');

    static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    static string Codes(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"`{v}`"));

    static string Words(IReadOnlyList<string> words, string last)
    {
        var quoted = words.Select(w => w.StartsWith('`') ? w : $"`{w}`").ToList();
        return quoted.Count == 1 ? quoted[0] : $"{string.Join(", ", quoted.SkipLast(1))} {last} {quoted[^1]}";
    }

    /// A table cell can't hold a raw | or a line break.
    static string Cell(string text) => text.Replace("|", "\\|").Replace("\n", " ");

    static void WriteKinds(StringBuilder md)
    {
        md.Append("## Kinds of value\n\n| Kind | How to write it |\n|---|---|\n");
        foreach (var kind in Enum.GetValues<SchemaKind>())
            md.Append($"| {ThemeSchema.KindName(kind)} | {Cell(ThemeSchema.KindHelp(kind))} |\n");
    }
}
