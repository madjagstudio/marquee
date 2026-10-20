using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Replaces the whole text, for changes that come from outside the editor's own commands: a hand edit reloaded
/// ("Changed outside the editor"), a revert to the opened version, restored unsaved changes. `Description` is the undo
/// step's label. Text that isn't YAML is refused (LayoutEditException); text that is, but doesn't load as a theme, is
/// taken, and the session pauses editing and shows the error until the text loads again.
public sealed record ReplaceDocument(string Text, string Description) : EditorCommand
{
    public override string Label => Description;

    public override bool TakesBrokenTheme => true;

    public override CommandResult Run(CommandContext context) => LayoutDocument.Open(Text);
}
