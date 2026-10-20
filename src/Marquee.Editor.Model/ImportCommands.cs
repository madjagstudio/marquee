using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Adds an image or video widget (`Type`) showing `File`, a path AssetImport gave: AddWidget's widget, placed as it
/// places one, with its `src` set. AssetImport.AddImage and AddVideo apply it once the file is in the theme.
public sealed record AddImportedWidget(string Type, string File, YamlPath? Group = null) : EditorCommand
{
    public override string Label => $"Add {Type}";

    public override CommandResult Run(CommandContext context)
    {
        if (Type is not ("image" or "video"))
            throw new ArgumentException($"A '{Type}' widget shows no file; use AddWidget.", nameof(Type));

        var added = new AddWidget(Type, Group).Run(context);
        // The catalog's example names a placeholder file; the new widget is the one AddWidget selects.
        var src = added.Selection![0].Child("src");
        return added with { Document = added.Document.SetScalar(src, File) };
    }
}

/// Sets the file key at `Key` (`src`, `font`, `image` or `video`) to `File`, a path AssetImport gave, as SetValue
/// sets a value, adding the key when the file hasn't it. A style's `font` is written as the properties panel writes a
/// style key (StyleValues.Set): over a named style (`style: meter`) the style becomes `{ extends: meter, font: … }`.
/// AssetImport.SetFile applies it once the file is in the theme.
public sealed record SetImportedFile(YamlPath Key, string File) : EditorCommand
{
    public override string Label => $"Import {Path.GetFileName(File)}";

    public override CommandResult Run(CommandContext context) =>
        StyleValues.InStyle(Key) is (var style, var key)
            ? StyleValues.Set(context.Document, style, [(key, File)])
            : new SetValue(Key, File).Run(context);
}
