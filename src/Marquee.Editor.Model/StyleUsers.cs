using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Direct references to a named style, including rules and widgets inside groups.
public static class StyleUsers
{
    public static IReadOnlyList<YamlPath> Of(LayoutDocument doc, string name)
    {
        var users = new List<YamlPath>();
        void Check(YamlPath style)
        {
            if (StyleValues.NameOf(doc, style) == name) users.Add(style);
        }

        void Walk(YamlPath list)
        {
            if (doc.GetNode(list) is not { Kind: LayoutNodeKind.List } items) return;
            for (var i = 0; i < items.Count; i++)
            {
                var widget = list.Item(i);
                Check(widget.Child("style"));
                var rules = widget.Child("when");
                if (doc.GetNode(rules) is { Kind: LayoutNodeKind.List } when)
                    for (var r = 0; r < when.Count; r++) Check(rules.Item(r).Child("style"));
                Walk(widget.Child("children"));
            }
        }

        Walk(YamlPath.Root.Child("widgets"));
        foreach (var other in StyleValues.Names(doc))
        {
            var style = YamlPath.Root.Child("styles").Child(other);
            if (doc.TryGetScalar(style.Child("extends"), out var parent) && parent == name) users.Add(style);
        }
        return users;
    }

    public static int Count(LayoutDocument doc, string name) => Of(doc, name).Count;
}
