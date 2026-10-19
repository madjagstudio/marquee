using System.Reflection;
using Marquee.App.Resources;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Core.Files;
using Marquee.Core.Logging;

namespace Marquee.App;

/// The tray's menu and what its items do, with no WinForms: BuildMenu turns the engine's state, config.yaml's theme
/// folders and the tray's own state into TrayMenuItems, and each item's command runs here. TrayApp only draws the
/// items and forwards clicks, rebuilding the menu each time it opens so the status is current.
///
/// Commands are called on the UI thread. Anything that touches the engine or the disk runs on `worker`, never on the UI
/// thread, and reports a failure as a balloon and a log line rather than throwing. Engine events arrive on engine
/// threads; their balloons go through `host`, which moves them to the UI thread.
///
/// The L-Connect handover's prompt and items are in TrayModel.Handover.cs, the update offer's in TrayModel.Updates.cs.
sealed partial class TrayModel
{
    /// Menu lines are cut to this many characters (the first line only), so a long error doesn't make the menu as
    /// wide as the screen. The log has it in full.
    public const int MenuTextLimit = 100;

    /// A balloon's text holds at most this many characters (NOTIFYICONDATA's szInfo is 256 with its terminator).
    public const int BalloonTextLimit = 255;

    /// A theme or config.yaml that keeps failing (every save while the user edits it) shows a balloon at most this
    /// often; the menu always has the latest error. Each kind of error (its title) has its own minute.
    public static readonly TimeSpan BalloonEvery = TimeSpan.FromMinutes(1);

    /// Windows cuts a tray icon's tooltip at 127 characters, and WinForms throws for a longer one.
    const int TooltipLimit = 127;

    static readonly (Orientation Value, Func<string> Text)[] Orientations =
    [
        (Orientation.Landscape, () => Strings.OrientationLandscape),
        (Orientation.LandscapeFlipped, () => Strings.OrientationLandscapeFlipped),
        (Orientation.Portrait, () => Strings.OrientationPortrait),
        (Orientation.PortraitFlipped, () => Strings.OrientationPortraitFlipped),
    ];

    readonly ITrayEngine _engine;
    readonly TrayPaths _paths;
    readonly IShell _shell;
    readonly ITrayHost _host;
    readonly EngineWorker _worker;
    readonly TrayUpdates _updates;
    readonly ILog _log;
    readonly BalloonLimiter _balloons;

    public TrayModel(ITrayEngine engine, TrayPaths paths, IShell shell, ITrayHost host, EngineWorker worker, TrayUpdates updates,
        ILog log, Func<DateTime>? utcNow = null)
    {
        _engine = engine;
        _paths = paths;
        _shell = shell;
        _host = host;
        _worker = worker;
        _updates = updates;
        _log = log;
        _balloons = new BalloonLimiter(BalloonEvery, utcNow ?? (() => DateTime.UtcNow));
        engine.ThemeFailed += error => Warn(Strings.ThemeFailedTitle, error);
        engine.ConfigFailed += error => Warn(Strings.ConfigFailedTitle, error);
        engine.ConfigApplied += () => updates.SetEnabled(engine.CheckForUpdates);
        engine.ScreenBusy += OnScreenBusy;
        engine.HandoverFinished += OnHandoverFinished;
        engine.ScreenOpened += OnScreenOpened;
        updates.Offered += ShowOffer;
    }

    /// The version About shows: the assembly's informational version without the commit hash the SDK appends.
    public static string Version =>
        (typeof(TrayModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+')[0];

    /// The icon's tooltip: Marquee and the status line.
    public string Tooltip => Cut(Strings.Tooltip(StatusLine), TooltipLimit);

    /// What the engine is doing, in a line; while an update downloads, how far it has got, and while it installs, that.
    public string StatusLine
    {
        get
        {
            if (_updates.Downloading is { } download) return Strings.StatusDownloading(VersionOf(download.Offer), download.Percent);
            if (Installing is { } installing) return Strings.StatusInstalling(VersionOf(installing));
            if (_engine.Settings is not { } settings) return Strings.StatusNoTheme;
            if (_engine.IsPaused) return Strings.StatusPaused;
            if (LConnectHasScreen) return Strings.StatusLConnectBusy;
            if (!_engine.IsRunning) return _engine.ThemeError is null ? Strings.StatusWaitingForScreen : Strings.StatusThemeFailed;
            var status = _engine.Status;
            return Strings.StatusShowing(status.Theme ?? Path.GetFileName(settings.ThemeFolder), status.Stats.Fps, status.Device ?? "");
        }
    }

    /// Starts the engine on the worker, then the daily update check, which runs even when the engine didn't start (it
    /// may be the fix). On the first run (config.yaml names no theme, and the hint hasn't been shown) a balloon says
    /// where to pick a theme and the orientation; not while L-Connect has the screen, though, where the handover's
    /// offer matters more and a later balloon would hide it: the hint then waits for a later start.
    public async Task Start()
    {
        await Run(() =>
        {
            _engine.Start();
            if (_engine.UsesDefaultTheme && !LConnectHasScreen && FirstRun.ClaimHint(_paths.DataFolder))
                _host.ShowBalloon(Strings.FirstRunHintTitle, Strings.FirstRunHint, isError: false);
        }).ConfigureAwait(false);
        _updates.SetEnabled(_engine.CheckForUpdates);
    }

    /// The menu, top to bottom: the status and any problems, the theme and orientation pickers, the windows and folders,
    /// the handover's items and pause, then the update's item, About and Quit.
    public IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        var settings = _engine.Settings;
        var hasEngine = settings is not null;
        List<TrayMenuItem> menu =
        [
            new(MenuKeys.Status, StatusLine) { Enabled = false },
            .. _engine.Problems.Select(problem => new TrayMenuItem(MenuKeys.Problem, MenuText(problem)) { Enabled = false }),
            .. HandoverProblems(),
            TrayMenuItem.Separator,
            new(MenuKeys.Themes, Strings.MenuThemes) { Children = ThemeItems(settings?.ThemeFolder) },
            new(MenuKeys.Orientation, Strings.MenuOrientation) { Enabled = hasEngine, Children = OrientationItems(settings?.Orientation) },
            TrayMenuItem.Separator,
            new(MenuKeys.Preview, Strings.MenuOpenPreview) { Enabled = hasEngine, Command = _host.OpenPreview },
            new(MenuKeys.ThemesFolder, Strings.MenuOpenThemesFolder) { Command = () => _ = OpenThemesFolder() },
            new(MenuKeys.Logs, Strings.MenuOpenLogs) { Command = () => _ = OpenLogs() },
            new(MenuKeys.Settings, Strings.MenuEditSettings) { Command = () => _ = EditSettings() },
            TrayMenuItem.Separator,
            .. HandoverItems(settings),
            _engine.IsPaused
                ? new(MenuKeys.Resume, Strings.MenuResume) { Enabled = hasEngine, Command = () => _ = Run(_engine.Resume) }
                : new(MenuKeys.Pause, Strings.MenuPause) { Enabled = hasEngine, Command = () => _ = Run(_engine.Pause) },
            TrayMenuItem.Separator,
            UpdateItem(),
            new(MenuKeys.About, Strings.MenuAbout) { Command = () => _host.ShowMessage(Strings.MenuAbout, Strings.About(Version)) },
            new(MenuKeys.Quit, Strings.MenuQuit) { Command = () => _ = Quit() },
        ];
        return menu;
    }

    /// Writes the theme's name to config.yaml, keeping the rest of the file as it is, then shows it. config.yaml is
    /// written first so the choice survives a restart; if it can't be written (a broken file, a link on its path), the
    /// theme isn't switched either, so the screen never shows something config.yaml would undo at the next edit. The
    /// config watcher sees the write too and applies the same settings, which changes nothing.
    public Task SwitchTheme(ThemeEntry theme) => Run(() =>
    {
        ConfigEditor.SetInFile(_paths.ConfigFile, "theme", theme.Name);
        _engine.SwitchTheme(theme.Folder);
    });

    /// As SwitchTheme, for `orientation:`.
    public Task SetOrientation(Orientation orientation) => Run(() =>
    {
        ConfigEditor.SetInFile(_paths.ConfigFile, "orientation", orientation.Name());
        _engine.SetOrientation(orientation);
    });

    /// The user's own themes folder, made if it isn't there yet, so there is somewhere to put a theme. The tray runs
    /// elevated and the folder is in one the user's programs control, so it is made only through a path without a
    /// junction or link on it (SafeFile.CreateDirectory). One that is already there is only opened, which Explorer does
    /// as the user, so a junction the user made to keep their themes elsewhere still works.
    public Task OpenThemesFolder() => Run(() =>
    {
        if (!Directory.Exists(_paths.UserThemes)) SafeFile.CreateDirectory(_paths.UserThemes);
        _shell.Open(_paths.UserThemes);
    });

    public Task OpenLogs() => Run(() =>
    {
        Directory.CreateDirectory(_paths.Logs);
        _shell.Open(_paths.Logs);
    });

    /// Opens config.yaml in the user's editor. A missing one is written first, naming the theme a missing one means,
    /// so the editor opens a file rather than an error, and the theme on show stays the same.
    public Task EditSettings() => Run(() =>
    {
        if (!File.Exists(_paths.ConfigFile)) ConfigEditor.SetInFile(_paths.ConfigFile, "theme", FirstRun.DefaultTheme);
        _shell.Open(_paths.ConfigFile);
    });

    /// Cancels an update download, stops the engine (which lets go of the screen and saves the Claude limits), then
    /// ends the tray.
    public async Task Quit()
    {
        _updates.Dispose();
        await Run(_engine.Dispose).ConfigureAwait(false);
        _host.Exit();
    }

    /// The Themes submenu: the user's themes, then the installed ones, each group sorted, with the one on show checked.
    IReadOnlyList<TrayMenuItem> ThemeItems(string? current)
    {
        var items = new List<TrayMenuItem>();
        foreach (var group in ThemeCatalog.List(_paths.Locator.ThemeFolders).Where(group => group.Count > 0))
        {
            if (items.Count > 0) items.Add(TrayMenuItem.Separator);
            items.AddRange(group.Select(theme => new TrayMenuItem(MenuKeys.Theme, theme.Name)
            {
                Checked = string.Equals(theme.Folder, current, StringComparison.OrdinalIgnoreCase),
                Command = () => _ = SwitchTheme(theme),
            }));
        }

        return items.Count > 0 ? items : [new TrayMenuItem(MenuKeys.NoThemes, Strings.MenuNoThemes) { Enabled = false }];
    }

    IReadOnlyList<TrayMenuItem> OrientationItems(Orientation? current) =>
        Orientations.Select(orientation => new TrayMenuItem(MenuKeys.Orientation + "-" + orientation.Value.Name(), orientation.Text())
        {
            Checked = orientation.Value == current,
            Command = () => _ = SetOrientation(orientation.Value),
        }).ToList();

    /// Runs `work` on the worker. A failure is the user's to know about (config.yaml can't be changed, Explorer
    /// didn't start): it goes to the log and a balloon, and the task still completes.
    Task Run(Action work) => _worker.Run(() =>
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            _log.Error(Strings.CommandFailed(ex.Message));
            _host.ShowBalloon(Strings.CommandFailedTitle, Cut(ex.Message, BalloonTextLimit), isError: true);
        }
    });

    void Warn(string title, string error)
    {
        if (_balloons.TryTake(title)) _host.ShowBalloon(title, Cut(error, BalloonTextLimit), isError: true);
    }

    static string MenuText(string text) => Cut(text.Split('\n')[0].TrimEnd('\r'), MenuTextLimit);

    static string Cut(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "…";
}
