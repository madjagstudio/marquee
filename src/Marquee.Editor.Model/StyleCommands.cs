using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Validation shared by the named-style commands and the properties panel.
public static class StyleCommands
{
    internal static readonly YamlPath Styles = YamlPath.Root.Child("styles");

    public static string? StyleNameProblem(LayoutDocument doc, string name, string? renaming = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return "A style name can't be empty.";
        if (name != renaming && doc.Exists(Styles.Child(name))) return $"A style named '{name}' already exists.";
        if (renaming is null) return null;

        var seen = new HashSet<string>(StringComparer.Ordinal) { name, renaming };
        var at = StyleValues.NameOf(doc, Styles.Child(renaming));
        while (at is not null)
        {
            if (!seen.Add(at)) return "That name would make an extends loop.";
            at = StyleValues.NameOf(doc, Styles.Child(at));
        }
        return null;
    }

    public static string? ExtendsProblem(LayoutDocument doc, string name, string? extends)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { name };
        for (var at = extends; at is not null; at = StyleValues.NameOf(doc, Styles.Child(at)))
        {
            if (!seen.Add(at)) return "A style can't extend itself, even through another style.";
            if (!doc.Exists(Styles.Child(at))) return $"There is no style named '{at}' in styles:.";
        }
        return null;
    }

    internal static void RequireStyle(LayoutDocument doc, string name)
    {
        if (!doc.Exists(Styles.Child(name))) throw new PropertyValueException($"There is no style named '{name}' in styles:.");
    }

    internal static LayoutDocument Add(LayoutDocument doc, string name, YamlMap value)
    {
        if (StyleNameProblem(doc, name) is { } problem) throw new PropertyValueException(problem);
        if (!doc.Exists(Styles)) doc = doc.AddKey(YamlPath.Root, "styles", "{}");
        return doc.AddKey(Styles, name, YamlWriter.Flow(value));
    }
}

public sealed record NewStyle(string Name) : EditorCommand
{
    public override string Label => "New style";

    public override CommandResult Run(CommandContext context) =>
        new(StyleCommands.Add(context.Document, Name, YamlMap.Empty), [StyleCommands.Styles.Child(Name)]);
}

public sealed record RenameStyle(string From, string To) : EditorCommand
{
    public override string Label => "Rename style";

    public override CommandResult Run(CommandContext context)
    {
        var doc = context.Document;
        StyleCommands.RequireStyle(doc, From);
        if (StyleCommands.StyleNameProblem(doc, To, From) is { } problem) throw new PropertyValueException(problem);
        if (From == To) return doc;
        var users = StyleUsers.Of(doc, From);
        var oldPath = StyleCommands.Styles.Child(From);
        var newPath = StyleCommands.Styles.Child(To);
        doc = doc.RenameKey(oldPath, To);
        foreach (var path in users)
            doc = doc.SetScalar(doc.TryGetScalar(path, out _) ? path : path.Child("extends"), To);
        return new(doc, context.Selection.Select(path => path == oldPath ? newPath : path).ToList());
    }
}

public sealed record DeleteStyle(string Name) : EditorCommand
{
    public override string Label => "Delete style";
    public int Copied { get; private set; }

    public override CommandResult Run(CommandContext context)
    {
        var doc = context.Document;
        StyleCommands.RequireStyle(doc, Name);
        var users = StyleUsers.Of(doc, Name);
        var inherited = StyleValues.Named(doc, Name);
        foreach (var path in users)
            doc = doc.ReplaceValue(path, YamlWriter.Flow(StyleValues.Merge(inherited, StyleValues.Own(doc, path))));
        doc = doc.RemoveKey(StyleCommands.Styles.Child(Name));
        if (doc.GetNode(StyleCommands.Styles) is { Count: 0 }) doc = doc.RemoveKey(StyleCommands.Styles);
        Copied = users.Count;
        return doc;
    }
}

public sealed record SaveAsStyle(YamlPath Widget, string Name) : EditorCommand
{
    public override string Label => "Save as style";

    public override CommandResult Run(CommandContext context)
    {
        context.RequireWidget(Widget);
        var doc = context.Document;
        var path = Widget.Child("style");
        var own = StyleValues.Own(doc, path);
        if (StyleValues.NameOf(doc, path) is { } parent) own = own.With("extends", parent);
        doc = StyleCommands.Add(doc, Name, own);
        return EditDocument.Set(doc, path, Name);
    }
}
