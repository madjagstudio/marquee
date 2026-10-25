using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Applying commands as undo steps, and undoing and redoing them as whole texts.
public sealed partial class EditorSession
{
    /// False while editing is paused, since autosave would write the undone text over the hand edit.
    /// Text from outside that loads (the file fixed, or "Revert to opened version") ends the pause.
    /// False during a drag in a panel too (IsInteracting): its step isn't finished.
    public bool CanUndo => History.CanUndo && !IsPaused && !IsInteracting;

    /// False while editing is paused or a panel drag is under way, as CanUndo is.
    public bool CanRedo => History.CanRedo && !IsPaused && !IsInteracting;

    /// Makes the command's edits as one undo step (or merges it into the last, by its MergeKey), loads the new text and
    /// swaps its theme in. A command that changes nothing is no step. Throws, changing nothing:
    /// - LayoutEditException when the file can't take the edits;
    /// - ThemeException, with its line, when the new text wouldn't load as a theme (unless TakesBrokenTheme);
    /// - InvalidOperationException during a drag's preview, or while editing is paused.
    public void Apply(EditorCommand command)
    {
        ThrowIfDisposed();
        ThrowIfPreviewing();
        if (IsPaused && !command.TakesBrokenTheme)
            throw new InvalidOperationException($"Editing is paused until layout.yaml loads as a theme again: {LoadError}");

        var result = command.Run(new CommandContext(_document, _tree, _selection, CanvasSize));
        var document = result.Document;
        if (document.Text == _document.Text)
        {
            // No step, but text from outside that is the session's own ends a pause on a file that wasn't YAML.
            if (command.TakesBrokenTheme && IsFileUnreadable) Raise(EndUnreadable());
            return;
        }

        LoadError? error = null;
        var loaded = command.TakesBrokenTheme ? TryLoad(document, out error) : Load(document);
        var (text, selection) = (_document.Text, _selection);
        var changes = Show(document, loaded, error) | SetSelection(StillThere(result.Selection ?? _selection));
        History.Push(new UndoStep(command.Label, text, document.Text, selection, _selection) { MergeKey = command.MergeKey });
        Raise(changes);
    }

    /// Takes the last step back: its text before, loaded, and its selection before. False when there is none to undo,
    /// or while CanUndo is false (paused, or a panel drag under way); nothing changes then.
    public bool Undo()
    {
        ThrowIfDisposed();
        ThrowIfPreviewing();
        if (IsPaused || IsInteracting || History.Undo() is not { } step) return false;
        try
        {
            Restore(step.Before, step.SelectionBefore);
        }
        catch
        {
            // Nothing was swapped in, so the step goes back where it was, still there to undo.
            History.Redo();
            throw;
        }

        return true;
    }

    /// Makes the last undone step again. False when there is nothing to redo, or while CanRedo is false because
    /// editing is paused or a panel drag is under way.
    public bool Redo()
    {
        ThrowIfDisposed();
        ThrowIfPreviewing();
        if (IsPaused || IsInteracting || History.Redo() is not { } step) return false;
        try
        {
            Restore(step.After, step.SelectionAfter);
        }
        catch
        {
            History.Undo();
            throw;
        }

        return true;
    }

    /// Swaps in a text the session had before. It opened then, so it opens now; if it didn't load as a theme then,
    /// editing pauses again. Anything else that stops it loading (not a ThemeException) is thrown before anything
    /// changes, and Undo and Redo put the step back.
    void Restore(string text, IReadOnlyList<YamlPath> selection)
    {
        var document = LayoutDocument.Open(text);
        var changes = Show(document, TryLoad(document, out var error), error);
        Raise(changes | SetSelection(StillThere(selection)));
    }

    void ThrowIfPreviewing()
    {
        if (IsPreviewing) throw new InvalidOperationException("A drag is being previewed: commit or cancel it first.");
    }
}
