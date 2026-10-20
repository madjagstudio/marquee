using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;

namespace Marquee.Editor.Model;

/// Applying commands as undo steps, and undoing and redoing them as whole texts.
public sealed partial class EditorSession
{
    public bool CanUndo => History.CanUndo;

    public bool CanRedo => History.CanRedo;

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
        if (document.Text == _document.Text) return;

        LoadError? error = null;
        var loaded = command.TakesBrokenTheme ? TryLoad(document, out error) : Load(document);
        var (text, selection) = (_document.Text, _selection);
        var changes = Show(document, loaded, error) | SetSelection(StillThere(result.Selection ?? _selection));
        History.Push(new UndoStep(command.Label, text, document.Text, selection, _selection) { MergeKey = command.MergeKey });
        Raise(changes);
    }

    /// Takes the last step back: its text before, loaded, and its selection before. False when there is none to undo.
    public bool Undo()
    {
        ThrowIfDisposed();
        ThrowIfPreviewing();
        if (History.Undo() is not { } step) return false;
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

    /// Makes the last undone step again. False when there is nothing to redo.
    public bool Redo()
    {
        ThrowIfDisposed();
        ThrowIfPreviewing();
        if (History.Redo() is not { } step) return false;
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
