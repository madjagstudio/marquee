using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// Direct references to a named style, including rules and widgets inside groups.
public static class StyleUsers
{
    public static IReadOnlyList<YamlPath> Of(LayoutDocument doc, string name)
        => References(doc, name).Select(user => user.Path).ToList();

    internal sealed record Reference(YamlPath Path, IReadOnlyList<string> StyleKeys);

    internal static IReadOnlyList<Reference> References(LayoutDocument doc, string name)
    {
        var users = new List<Reference>();

        void Walk(YamlPath path, string schema)
        {
            if (doc.GetNode(path) is not { Kind: LayoutNodeKind.Mapping }) return;
            IReadOnlyList<SchemaKey> keys;
            if (ThemeSchema.Union(schema) is { } union)
            {
                var variant = union.Discriminator is { } discriminator && doc.TryGetScalar(path.Child(discriminator), out var type)
                    ? union.Variant(type)
                    : union.Variants.FirstOrDefault(v => doc.Exists(path.Child(v.Name)));
                keys = variant is null ? union.Common.Keys : union.KeysOf(variant);
            }
            else keys = ThemeSchema.Block(schema)?.Keys ?? [];

            foreach (var key in keys)
            {
                var child = path.Child(key.Name);
                if (key.Kind == SchemaKind.Style && StyleValues.NameOf(doc, child) == name)
                    users.Add(new(child, key.StyleKeys));
                else if (key.Of is { } of)
                {
                    if (key.Kind == SchemaKind.List && doc.GetNode(child) is { Kind: LayoutNodeKind.List } list)
                        for (var i = 0; i < list.Count; i++) Walk(child.Item(i), of);
                    else if (key.Kind == SchemaKind.Block) Walk(child, of);
                }
            }
        }

        Walk(YamlPath.Root, ThemeSchema.Layout.Id);
        foreach (var other in StyleValues.Names(doc))
        {
            var style = YamlPath.Root.Child("styles").Child(other);
            if (doc.TryGetScalar(style.Child("extends"), out var parent) && parent == name) users.Add(new(style, []));
        }
        return users;
    }

    public static int Count(LayoutDocument doc, string name) => Of(doc, name).Count;
}
