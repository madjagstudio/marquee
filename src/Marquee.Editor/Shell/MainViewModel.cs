using System.Windows.Input;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;

namespace Marquee.Editor.Shell;

/// The main window: the open theme (EditorWorkspace), its canvas, the toolbar's commands (MainViewModel.Toolbar.cs) and
/// the status the window shows (MainViewModel.Status.cs). Opening another theme replaces this one in the same window,
/// after saving it: one window, one theme, as one editor per theme folder means.
///
/// Made and used on the UI thread.
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    readonly EditorEnvironment _environment;
    readonly SynchronizationContext _ui;
    readonly ScreenTheme _screen;
    EditorWorkspace? _workspace;
    string? _message;

    public MainViewModel(EditorEnvironment environment, EditorWorkspace workspace, SynchronizationContext ui)
    {
        _environment = environment;
        _ui = ui;
        _screen = new ScreenTheme(environment.ConfigFile, environment.Locator);
        Canvas = new CanvasViewModel(Run, environment.Clipboard, Report, environment.Time);
        InitializeCommands();
        Attach(workspace);
    }

    public CanvasViewModel Canvas { get; }

    /// The theme open now; null once the window has closed it.
    public EditorWorkspace? Workspace => _workspace;

    public string ThemeName => _workspace?.Name ?? "";

    public string Title => Strings.WindowTitle(ThemeName);

    public bool IsReadOnly => _workspace?.IsReadOnly ?? true;

    /// The last thing the status bar has to say about an action; null when there is nothing.
    public string? Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    /// Raised when another editor asked for this theme: the window comes forward.
    public event EventHandler? ShowRequested;

    /// Applies `command` to the open theme as one undo step, reporting in the status bar what the model refuses. True
    /// when it went through.
    public bool Run(EditorCommand command)
    {
        if (_workspace is not { } workspace) return false;
        if (workspace.IsReadOnly)
        {
            Report(Strings.ReadOnlyRefused);
            return false;
        }

        if (workspace.Session.IsPaused && !command.TakesBrokenTheme)
        {
            Report(Strings.PausedRefused);
            return false;
        }

        try
        {
            workspace.Session.Apply(command);
            Message = null;
            return true;
        }
        catch (Exception ex) when (ex is LayoutEditException or ThemeException or ArgumentException or InvalidOperationException
                                       or IOException or UnauthorizedAccessException)
        {
            Report(Strings.ActionFailed(command.Label, ex.Message));
            return false;
        }
    }

    /// A key went down in the window, outside a text box; true when it was used. Undo and redo are the window's, the
    /// rest the canvas's.
    public bool KeyDown(Key key, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Control) && !Canvas.IsDragging)
        {
            var shift = modifiers.HasFlag(ModifierKeys.Shift);
            if (key == Key.Z && !shift)
            {
                Undo();
                return true;
            }

            if (key == Key.Y || (key == Key.Z && shift))
            {
                Redo();
                return true;
            }
        }

        return Canvas.KeyDown(key, modifiers);
    }

    /// Before the window closes: saves what waits. When that fails, asks whether to close anyway, leaving the changes
    /// to be offered next time. False when the window should stay open.
    public bool TryClose()
    {
        if (_workspace is not { } workspace) return true;
        if (!workspace.Flush() && !_environment.Dialogs.AskCloseUnsaved(SaveDetail ?? "")) return false;
        Detach();
        workspace.Close();
        return true;
    }

    /// Opens the theme in `themeFolder` in this window, in place of the one open now, which is saved first. Nothing
    /// changes when the user declines, the theme is open in another editor (which comes forward), or it can't be opened.
    public void OpenTheme(string themeFolder)
    {
        if (_workspace is not { } current) return;
        if (string.Equals(ThemeIdentity.FullPath(themeFolder), ThemeIdentity.FullPath(current.Session.Folder), StringComparison.OrdinalIgnoreCase))
            return;
        if (!current.Flush() && !_environment.Dialogs.AskCloseUnsaved(SaveDetail ?? "")) return;
        if (EditorWorkspace.Open(themeFolder, _environment, _ui) is not { } next) return;

        Detach();
        current.Close();
        Attach(next);
    }

    /// Copies the theme in `themeFolder` into the user's themes and opens the copy: "New from an example", and "Make a
    /// copy to edit" for a read-only theme.
    public void CopyAndOpen(string themeFolder)
    {
        if (EditorWorkspace.MakeCopy(themeFolder, _environment) is { } copy) OpenTheme(copy);
    }

    /// Closes the theme without saving what waits, for a failure; TryClose is the clean way.
    public void Dispose()
    {
        var workspace = _workspace;
        Detach();
        workspace?.Dispose();
        Canvas.Dispose();
    }

    void Attach(EditorWorkspace workspace)
    {
        _workspace = workspace;
        workspace.Session.Changed += OnSessionChanged;
        workspace.Values.Updated += OnValuesUpdated;
        workspace.ShowRequested += OnShowRequested;
        workspace.RecoveryProblemChanged += OnRecoveryProblemChanged;
        if (workspace.Autosaver is { } autosaver) autosaver.StateChanged += OnSaveStateChanged;
        Canvas.Attach(workspace);
        Message = null;
        Raise(null);
        RefreshStatus();
    }

    void Detach()
    {
        if (_workspace is not { } workspace) return;
        Canvas.Detach();
        workspace.Session.Changed -= OnSessionChanged;
        workspace.Values.Updated -= OnValuesUpdated;
        workspace.ShowRequested -= OnShowRequested;
        workspace.RecoveryProblemChanged -= OnRecoveryProblemChanged;
        if (workspace.Autosaver is { } autosaver) autosaver.StateChanged -= OnSaveStateChanged;
        _workspace = null;
    }

    void Report(string message) => Message = message;

    void OnSessionChanged(object? sender, SessionChanges changes)
    {
        // A drag's every move: only whether undo and the toolbar's edits are on changes, as it begins and ends.
        if (changes == SessionChanges.Preview) RefreshCommands();
        else RefreshStatus();
    }

    void OnValuesUpdated(object? sender, EventArgs e) => RefreshTray();

    void OnSaveStateChanged(object? sender, SaveState state) => RefreshSaveState();

    void OnShowRequested(object? sender, EventArgs e) => ShowRequested?.Invoke(this, EventArgs.Empty);

    void OnRecoveryProblemChanged(object? sender, EventArgs e) => RefreshRecovery();
}
