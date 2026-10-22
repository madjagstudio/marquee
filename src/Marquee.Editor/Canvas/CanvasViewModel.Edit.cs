using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Canvas;

/// The edits on the selection that the canvas's keys, the layers panel's menu and the properties panel's buttons share:
/// copy, paste, duplicate, delete, group and ungroup, and copy and paste style. Paste goes into the group being worked
/// in. Each is one undo step, and what the model refuses is reported in the status bar.
public sealed partial class CanvasViewModel
{
    /// Something is selected for the edits to work on.
    public bool HasSelection => Session is { Selection.Count: > 0, SelectedLayer: null };

    /// A style has been copied (Ctrl+Shift+C), so it can be pasted.
    public bool HasCopiedStyle => _styles.Style is not null;

    /// The clipboard holds widgets the editor copied, or wrote as layout.yaml writes them.
    public bool CanPaste => _clipboard.GetText() is { } text && WidgetClipboard.Items(text) is not null;

    /// Ctrl+C: the selected widgets' YAML onto the clipboard.
    public void CopySelection()
    {
        if (Session is not { Selection.Count: > 0, SelectedLayer: null } session) return;
        try
        {
            if (!_clipboard.SetText(WidgetClipboard.Copy(session.Document, session.Selection))) _report(Strings.ClipboardBusy);
        }
        catch (LayoutEditException ex)
        {
            _report(Strings.ActionFailed(Strings.ActionCopy, ex.Message));
        }
    }

    /// Ctrl+V: the clipboard's widgets, 20 px down and right, into the group being worked in.
    public void Paste()
    {
        if (_clipboard.GetText() is { } text && WidgetClipboard.Items(text) is not null) _run(new PasteWidgets(text, _group));
    }

    /// Ctrl+D.
    public void DuplicateSelection() => OnSelection(selection => new DuplicateWidgets(selection));

    /// Del.
    public void DeleteSelection() => OnSelection(selection => new DeleteWidgets(selection));

    /// Ctrl+G.
    public void GroupSelection() => OnSelection(selection => new GroupWidgets(selection));

    /// Ctrl+Shift+G.
    public void UngroupSelection() => OnSelection(selection => new UngroupWidgets(selection));

    /// Ctrl+Shift+C: the first selected widget's style as it draws.
    public void CopyStyle()
    {
        if (HasSelection && Session is { } session) Try(Strings.ActionCopyStyle, () => _styles.Copy(session));
    }

    /// Ctrl+Shift+V: the copied style onto every selected widget, as overrides.
    public void PasteStyle()
    {
        if (Session is not { } session || _styles.Style is null) return;
        var targets = session.Selection.Select(session.Widget).OfType<WidgetRef>().ToList();
        if (targets.Count > 0) _run(_styles.Paste(targets));
    }

    void OnSelection(Func<IReadOnlyList<YamlPath>, EditorCommand> command)
    {
        if (Session is { Selection.Count: > 0, SelectedLayer: null } session) _run(command(session.Selection));
    }
}
