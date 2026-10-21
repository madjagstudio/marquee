using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Edits worked out on the document as it is when the command runs, made as one undo step: the properties panel's
/// writes, which may set a key on several widgets, rewrite a named style as overrides, or remove a key and then the
/// mapping it leaves empty. Each edit sees the document the one before it left.
public sealed record EditDocument(string Label, Func<LayoutDocument, LayoutDocument> Edit) : EditorCommand
{
    public override string Label { get; } = Label;

    public override CommandResult Run(CommandContext context) => Edit(context.Document);

    /// SetValue on `document`: the value set in place, or added with the mappings missing on the way to it.
    internal static LayoutDocument Set(LayoutDocument document, YamlPath path, YamlValue value) =>
        new SetValue(path, value).Run(Bare(document)).Document;

    /// Removes the key at `path` when the file has it; a key already left out is already reset.
    internal static LayoutDocument Remove(LayoutDocument document, YamlPath path) =>
        document.Exists(path) ? document.RemoveKey(path) : document;

    // SetValue reads only the document, so it runs without the session's widgets.
    static CommandContext Bare(LayoutDocument document) => new(document, WidgetTree.Empty, [], SKSizeI.Empty);
}
