using Marquee.Core.Expressions;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// The theme open in the editor: its folder, its layout.yaml as a LayoutDocument (the source of truth), the theme
/// loaded from that text and a renderer for it, the selection, and the undo history. Every user action is a command
/// applied to it (EditorSession.Commands.cs); a drag is previewed in memory first (EditorSession.Preview.cs).
///
/// Each change loads the new text through the session's asset cache, swaps the new theme in, then disposes the old
/// one, so files both use stay loaded and a clip plays on. Text that doesn't load as a theme leaves the last good
/// theme on screen, with LoadError saying why: a command is refused then, but text from outside the editor
/// (ReplaceDocument) is taken, and editing, undo and redo pause until text from outside loads again (CanUndo).
///
/// Not thread-safe: the editor uses it from its UI thread, and draws frames there too.
public sealed partial class EditorSession : IDisposable
{
    readonly ThemeAssetCache _cache = new();
    LayoutDocument _document;
    WidgetTree _tree = WidgetTree.Empty;
    IReadOnlyList<YamlPath> _selection = [];
    TimeLocale? _locale;
    bool _disposed;

    EditorSession(string folder, LayoutDocument document, EditorSessionOptions options)
    {
        Folder = folder;
        _document = document;
        History = new UndoStack(options.Time, options.UndoLimit, options.MergeWindow);
        // Until the value sources are given, every key is missing and `now` is the session's clock.
        var empty = new SensorStore(options.Time.GetUtcNow);
        Values = empty.Snapshot;
    }

    /// Opens the theme in `themeFolder`. Its layout.yaml is read as bytes (LayoutDocument.FromBytes), so a byte order
    /// mark stays in the text and saving (ToBytes) keeps it. A theme with an error opens paused, with LoadError set and
    /// no theme to draw, so the error can be fixed. Throws, leaving nothing open:
    /// - ThemeException for a missing folder or layout.yaml;
    /// - LayoutEditException, with its line, for a file that isn't UTF-8 YAML the editor can edit;
    /// - IOException or UnauthorizedAccessException when layout.yaml, or a file the theme loads, can't be read:
    ///   another program has it locked, or access is denied.
    /// Each one's Message is written for the user, so the shell shows it as it is (and may offer to try again for the
    /// last two).
    public static EditorSession Open(string themeFolder, EditorSessionOptions? options = null)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(themeFolder));
        if (!Directory.Exists(folder))
            throw new ThemeException($"Theme folder {folder} does not exist.");
        var layout = Path.Combine(folder, ThemeLoader.LayoutFileName);
        if (!File.Exists(layout))
            throw new ThemeException($"Theme folder {folder} has no {ThemeLoader.LayoutFileName}.");

        var document = LayoutDocument.FromBytes(File.ReadAllBytes(layout));
        var session = new EditorSession(folder, document, options ?? new EditorSessionOptions());
        try
        {
            session.Show(document, session.TryLoad(document, out var error), error);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// The theme folder's full path.
    public string Folder { get; }

    public string LayoutPath => Path.Combine(Folder, ThemeLoader.LayoutFileName);

    /// The layout text as it is now, saved or not.
    public LayoutDocument Document => _document;

    /// The theme loaded from the last text that loaded: Document's, unless LoadError says otherwise. Null when no text
    /// has loaded since the theme was opened.
    public LoadedTheme? Theme { get; private set; }

    /// Draws Theme. A new one comes with each new theme, so animation state (smoothing) starts again after a change.
    public ThemeRenderer? Renderer { get; private set; }

    /// Why Document's text doesn't load as a theme; null when it does.
    public LoadError? LoadError { get; private set; }

    /// True while Document's text doesn't load (Pause says why): only text from outside the editor is taken; commands,
    /// undo and redo wait (CanUndo).
    public bool IsPaused => Pause != PauseReason.None;

    /// The canvas in theme pixels; empty when no theme has loaded.
    public SKSizeI CanvasSize => Theme?.Spec.Canvas.Size ?? SKSizeI.Empty;

    /// The widgets of Theme, back to front, with their children.
    public IReadOnlyList<WidgetRef> Widgets => _tree.Roots;

    /// The widget at `path`, or null when there is none.
    public WidgetRef? Widget(YamlPath path) => _tree.Find(path);

    /// The selected widgets' paths, in the order they were chosen, or one background layer or named style.
    public IReadOnlyList<YamlPath> Selection => _selection;

    /// The selected background layer, or null when widgets or nothing are selected.
    public YamlPath? SelectedLayer => _selection is [var only] && IsBackgroundLayer(only) ? only : null;

    /// The selected named style's key, or null for another selection.
    public string? SelectedStyle => _selection is [var only] && IsNamedStyle(only) ? only.Last!.Value.Key : null;

    public bool IsNamedStyle(YamlPath path) =>
        path.Parent == YamlPath.Root.Child("styles") && path.Last is { IsKey: true } && _document.Exists(path);

    /// Whether `path` names an existing item of the background list.
    public bool IsBackgroundLayer(YamlPath path) =>
        path.Parent == YamlPath.Root.Child("background") && path.Last is { IsKey: false } && _document.Exists(path);

    /// The undo steps, for their labels and for sealing a merge.
    public UndoStack History { get; }

    /// Where frames' values come from: an empty store until the editor's value sources set this.
    public Func<SensorSnapshot> Values { get; set; }

    /// The time zone and culture clocks and dates are drawn in; null is the PC's own. Every renderer gets it.
    public TimeLocale? Locale
    {
        get => _locale;
        set
        {
            _locale = value;
            if (Renderer is not null) Renderer.Locale = value;
        }
    }

    /// Raised after every change, with what changed: the text, the theme, the selection or a drag's preview.
    public event EventHandler<SessionChanges>? Changed;

    /// Raised when the selection changes, before Changed.
    public event EventHandler? SelectionChanged;

    /// Chooses widgets, each once in order, or exactly one background layer or named style. Throws ArgumentException
    /// for missing paths or a layer/style mixed with another selection, and the selection stays as it was.
    public void Select(IEnumerable<YamlPath> widgets)
    {
        ThrowIfDisposed();
        var paths = widgets.Distinct().ToList();
        if (paths.Any(path => IsBackgroundLayer(path) || IsNamedStyle(path)) && paths.Count != 1)
            throw new ArgumentException("A background layer or named style is selected on its own.", nameof(widgets));
        foreach (var path in paths)
            if (_tree.Find(path) is null && !IsBackgroundLayer(path) && !IsNamedStyle(path))
                throw new ArgumentException($"There is no widget, background layer or named style at {path}.", nameof(widgets));
        Raise(SetSelection(paths));
    }

    /// A frame of the theme, or of the drag being previewed, with the session's values. Null when no theme has loaded.
    /// The caller disposes it. `time` is animation time (pulse, smoothing, video), from one clock for the whole session
    /// that only goes forward, not restarted for each theme: a clip carried over from the last theme keeps the clock it
    /// first saw, and would stand still while a restarted clock caught up.
    public SKImage? RenderFrame(TimeSpan time)
    {
        ThrowIfDisposed();
        return RenderFrame(Values(), time);
    }

    /// A frame with the values given.
    public SKImage? RenderFrame(SensorSnapshot sensors, TimeSpan time)
    {
        ThrowIfDisposed();
        if (Renderer is null || Theme is null) return null;
        return _preview is { } preview
            ? preview.Render(Renderer, Theme.Spec.Widgets, sensors, time)
            : Renderer.Render(sensors, time);
    }

    /// Disposes the renderer, then the theme, then the asset cache, in that order: the cache owns the files the theme
    /// draws. A frame already handed out is the caller's and stays valid.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preview = null;
        Renderer?.Dispose();
        Theme?.Dispose();
        _cache.Dispose();
    }

    /// For tests: runs before each load, so a test can make one fail as nothing else in a theme folder can.
    internal Action<LayoutDocument>? LoadHook { get; set; }

    /// The theme loaded from `document`'s text, with its widgets matched to the file. Throws ThemeException for text
    /// that doesn't load; the theme on screen is untouched either way.
    Loaded Load(LayoutDocument document)
    {
        LoadHook?.Invoke(document);
        // The loader reads YAML, which would take a byte order mark for part of the first key.
        var text = document.Text.StartsWith('\uFEFF') ? document.Text[1..] : document.Text;
        var theme = ThemeLoader.Load(Folder, text, _cache);
        try
        {
            return new Loaded(theme, WidgetTree.Build(document, theme.Spec));
        }
        catch
        {
            theme.Dispose();
            throw;
        }
    }

    /// Load, with null and the error for text that doesn't load.
    Loaded? TryLoad(LayoutDocument document, out LoadError? error)
    {
        try
        {
            error = null;
            return Load(document);
        }
        catch (ThemeException ex)
        {
            error = new LoadError(ex.Reason, ex.Line);
            return null;
        }
    }

    /// Makes `document` the text, with `loaded` on screen, or, when it didn't load, the last good theme and `error`;
    /// and lists its problems.
    SessionChanges Show(LayoutDocument document, Loaded? loaded, LoadError? error)
    {
        _document = document;
        // The text is the session's own now, so a pause on an unreadable file ends, whatever it paused over.
        _beforeUnreadable = null;
        Problems = ProblemsOf(document, loaded, error);
        if (loaded is null)
        {
            LoadError = error;
            Pause = PauseReason.Theme;
        }
        else
        {
            // The new theme goes in before the old one is disposed, so the files both use carry over in the cache.
            var (theme, renderer) = (Theme, Renderer);
            Theme = loaded.Theme;
            Renderer = new ThemeRenderer(loaded.Theme) { Locale = _locale };
            _tree = loaded.Tree;
            LoadError = null;
            Pause = PauseReason.None;
            renderer?.Dispose();
            theme?.Dispose();
        }

        return SessionChanges.Document | SessionChanges.Theme;
    }

    SessionChanges SetSelection(IReadOnlyList<YamlPath> paths)
    {
        if (paths.SequenceEqual(_selection)) return SessionChanges.None;
        _selection = paths;
        return SessionChanges.Selection;
    }

    /// The paths that are still widgets or background layers, once the tree is current; while paused it is the last
    /// good theme's, so they are kept as they are.
    IReadOnlyList<YamlPath> StillThere(IReadOnlyList<YamlPath> paths) =>
        IsPaused ? paths : paths.Distinct().Where(path => _tree.Find(path) is not null || IsBackgroundLayer(path) || IsNamedStyle(path)).ToList();

    void Raise(SessionChanges changes)
    {
        if (changes == SessionChanges.None) return;
        if (changes.HasFlag(SessionChanges.Selection)) SelectionChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, changes);
    }

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// A theme loaded from a document's text, with its widget tree.
    sealed record Loaded(LoadedTheme Theme, WidgetTree Tree);
}
