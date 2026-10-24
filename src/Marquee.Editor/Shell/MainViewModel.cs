using System.Windows.Input;
using System.Windows;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Host;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Canvas;
using Marquee.Editor.Layers;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Properties;
using Marquee.Editor.Resources;
using Marquee.Editor.Session;
using Marquee.Editor.Values;
using Marquee.Editor.Styles;

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
        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanvasViewModel.VisibilityWarning)) Raise(nameof(StatusMessage));
        };
        Layers = new LayersViewModel(Run, Canvas, Report, AddBackground, DeleteSelection);
        Styles = new StylesViewModel(Run, Canvas, Report);
        Properties = new PropertiesViewModel(Run, Report, Canvas, environment.Dialogs, ChangeOrientation, ChangeBackgroundKind, environment.ConfigFile);
        TestValues = new TestValuesViewModel();
        InitializeCommands();
        Attach(workspace);
    }

    public CanvasViewModel Canvas { get; }

    /// The layers panel.
    public LayersViewModel Layers { get; }
    public StylesViewModel Styles { get; }

    /// The properties panel.
    public PropertiesViewModel Properties { get; }

    /// The test values panel.
    public TestValuesViewModel TestValues { get; }

    /// The theme open now; null once the window has closed it.
    public EditorWorkspace? Workspace => _workspace;

    public string ThemeName => _workspace?.Name ?? "";
    public string ThemeHeading => ThemeName.ToUpperInvariant();

    bool _isLayersOpen;
    GridLength _leftPanelWidth = new(260);
    public bool IsLayersOpen
    {
        get => _isLayersOpen;
        set
        {
            if (Set(ref _isLayersOpen, value)) Raise(nameof(LeftPanelWidth));
        }
    }

    public GridLength LeftPanelWidth
    {
        get => IsLayersOpen ? _leftPanelWidth : new GridLength(0);
        set { if (IsLayersOpen) Set(ref _leftPanelWidth, value); }
    }

    Marquee.Core.Devices.Orientation _screenOrientation;
    bool ThemeIsPortrait => _workspace?.Session.Theme?.Spec.Canvas.Orientation == ThemeOrientation.Portrait;
    public string ScreenTag => Strings.ScreenTag(_screenOrientation.Name(), _screenOrientation.CanvasSize().Width, _screenOrientation.CanvasSize().Height);
    public bool ScreenMismatch => _screenOrientation.IsPortrait() != ThemeIsPortrait;
    public string ScreenToolTip => ScreenMismatch
        ? Strings.ScreenMismatchTip(ThemeIsPortrait ? "portrait" : "landscape", _screenOrientation.IsPortrait() ? "portrait" : "landscape")
        : Strings.ScreenOrientationTip;

    public void RefreshScreen()
    {
        _screenOrientation = ConfigFile.Load(_environment.ConfigFile, NullLog.Instance).Orientation ?? Marquee.Core.Devices.Orientation.Landscape;
        Raise(nameof(ScreenTag));
        Raise(nameof(ScreenMismatch));
        Raise(nameof(ScreenToolTip));
    }

    public string Title => Strings.WindowTitle(ThemeName);

    public bool IsReadOnly => _workspace?.IsReadOnly ?? true;

    /// The last thing the status bar has to say about an action; null when there is nothing.
    public string? Message
    {
        get => _message;
        private set
        {
            if (Set(ref _message, value)) Raise(nameof(StatusMessage));
        }
    }

    public string? StatusMessage => Canvas.VisibilityWarning ?? Message;

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
        catch (Exception ex) when (ex is LayoutEditException or ThemeException or PropertyValueException or ArgumentException or InvalidOperationException
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
        if (key == Key.Delete && modifiers == ModifierKeys.None && _workspace?.Session.SelectedLayer is not null)
        {
            DeleteSelection();
            return true;
        }
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

    void DeleteSelection()
    {
        if (!Layers.CanEdit) return;
        if (_workspace?.Session.SelectedLayer is { } layer) Run(new RemoveLayer(layer));
        else Canvas.DeleteSelection();
    }

    bool ChangeOrientation(string orientation)
    {
        if (!Properties.CanEdit || _environment.Dialogs.AskRearrange(orientation) is not { } rearrange) return false;
        if (!Run(new SetOrientation(orientation, rearrange))) return false;
        if (rearrange && SetOrientation.PartlyOffCanvas(_workspace!.Session) is var count && count > 0)
            Report(Strings.PartlyOffCanvas(count));
        return true;
    }

    void AddBackground(LayerKindChoice kind)
    {
        if (_workspace is not { } workspace || !Layers.CanEdit) return;
        if (kind == LayerKindChoice.Fill)
            Run(new AddLayer(kind, null, AddLayer.AddIndex(workspace.Session)));
        else ImportBackground(kind, change: false);
    }

    void ChangeBackgroundKind(LayerKindChoice kind)
    {
        if (!Properties.CanEdit || _workspace?.Session.SelectedLayer is not { } layer) return;
        if (kind == LayerKindChoice.Fill) Run(new ChangeLayerKind(layer, kind, null));
        else ImportBackground(kind, change: true);
    }

    void ImportBackground(LayerKindChoice kind, bool change)
    {
        if (_workspace is not { } workspace || _environment.Dialogs.PickLayerFile(kind) is not { } file) return;
        try
        {
            var session = workspace.Session;
            var import = new AssetImport(session.Folder);
            var imported = change
                ? import.ChangeLayerKind(session, session.SelectedLayer!, kind, file)
                : import.AddLayer(session, kind, file);
            PropertyChoices.Forget();
            Message = imported.Warning;
        }
        catch (AssetImportException ex)
        {
            _environment.Dialogs.ShowError(Strings.ImportRefused(Path.GetFileName(file), ex.Reason));
        }
        catch (Exception ex) when (ex is LayoutEditException or ThemeException or ArgumentException or InvalidOperationException
                                       or IOException or UnauthorizedAccessException)
        {
            _environment.Dialogs.ShowError(Strings.ImportRefused(Path.GetFileName(file), ex.Message));
        }
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
        Layers.Attach(workspace.Session);
        Styles.Attach(workspace.Session);
        Properties.Attach(workspace);
        TestValues.Attach(workspace.Values);
        Message = null;
        Raise(null);
        RefreshStatus();
    }

    void Detach()
    {
        if (_workspace is not { } workspace) return;
        TestValues.Detach();
        Properties.Detach();
        Styles.Detach();
        Layers.Detach();
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
