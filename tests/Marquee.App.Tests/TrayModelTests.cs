using Marquee.App.Tests.TestSupport;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Host;

namespace Marquee.App.Tests;

/// The menu as data and what its commands do, with the engine, Explorer and the window side faked.
public sealed class TrayModelTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly FakeTrayEngine _engine = new();
    readonly FakeShell _shell = new();
    readonly FakeHost _host = new();
    readonly EngineWorker _worker = new();
    readonly FakePowerEvents _power = new();
    readonly WakeClock _time = new();
    readonly UpdateRig _updates;
    readonly TrayPaths _paths;
    DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public TrayModelTests()
    {
        _paths = new TrayPaths(_dir.File("config.yaml"), new ThemeLocator([_dir.File("user"), _dir.File("installed")], _dir.Path),
            _dir.File("logs"), _dir.File("data"));
        _updates = new UpdateRig(_dir);
        Model = new TrayModel(_engine, _paths, _shell, _host, _worker, _updates.Updates, NullLog.Instance, () => _now,
            _power, _time);
    }

    TrayModel Model { get; }

    public void Dispose()
    {
        Model.Dispose();
        _worker.Dispose();
        _updates.Dispose();
        _dir.Dispose();
    }

    string Theme(string folder, string name) =>
        Path.GetDirectoryName(_dir.Write(Path.Combine(folder, name, "layout.yaml"), "canvas: {}\n"))!;

    static TrayMenuItem Item(IEnumerable<TrayMenuItem> items, string key) => Assert.Single(items, item => item.Key == key);

    TrayMenuItem Item(string key) => Item(Model.BuildMenu(), key);

    [Fact]
    public async Task Waking_reopens_once_on_the_engine_worker_and_debounces_repeated_signals()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        var workerThread = 0;
        await _worker.Run(() => workerThread = Environment.CurrentManagedThreadId);

        _power.Wake();
        _power.Wake();
        await _worker.Run(() => { }).WaitAsync(Wait.Timeout);
        _power.Wake();
        await _worker.Run(() => { }).WaitAsync(Wait.Timeout);

        Assert.Equal(("ReopenAfterSleep", workerThread), Assert.Single(_engine.Calls));

        _time.Now += TimeSpan.FromSeconds(4);
        _power.Wake();
        await _worker.Run(() => { }).WaitAsync(Wait.Timeout);
        Assert.Equal(["ReopenAfterSleep", "ReopenAfterSleep"], _engine.CallNames);
    }

    [Fact]
    public async Task Waking_does_nothing_after_dispose_even_when_already_queued()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        using var release = new ManualResetEventSlim();
        var waiting = _worker.Run(() => release.Wait(Wait.Timeout));
        try
        {
            _power.Wake();
            Model.Dispose();
            Assert.False(_power.HasSubscribers);
            _power.Wake();
        }
        finally
        {
            release.Set();
        }
        await waiting.WaitAsync(Wait.Timeout);
        await _worker.Run(() => { }).WaitAsync(Wait.Timeout);

        Assert.Empty(_engine.Calls);
    }

    sealed class FakePowerEvents : IPowerEvents
    {
        public event Action? Woke;

        public bool HasSubscribers => Woke is not null;

        public void Wake() => Woke?.Invoke();
    }

    sealed class WakeClock : TimeProvider
    {
        public TimeSpan Now { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now.Ticks;
    }

    [Fact]
    public void Running_the_status_line_says_theme_fps_and_screen()
    {
        _engine.Showing(Theme("installed", "example-landscape"));

        var menu = Model.BuildMenu();

        var status = menu[0];
        Assert.Equal(MenuKeys.Status, status.Key);
        Assert.Equal("example-landscape · 30 fps · Universal Screen 8.8", status.Text);
        Assert.False(status.Enabled);
        Assert.Equal("Pause", Item(menu, MenuKeys.Pause).Text);
        Assert.DoesNotContain(menu, item => item.Key == MenuKeys.Resume);
        Assert.Equal("Marquee: example-landscape · 30 fps · Universal Screen 8.8", Model.Tooltip);
    }

    [Fact]
    public void The_menu_has_its_items_in_order()
    {
        _engine.Showing(Theme("installed", "example-landscape"));

        var keys = Model.BuildMenu().Select(item => item.Key);

        Assert.Equal([
            MenuKeys.Status, MenuKeys.Separator, MenuKeys.Themes, MenuKeys.Orientation, MenuKeys.Separator,
            MenuKeys.Preview, MenuKeys.EditTheme, MenuKeys.NewTheme, MenuKeys.ThemesFolder, MenuKeys.Logs, MenuKeys.Settings, MenuKeys.Separator,
            MenuKeys.AlwaysTakeOver, MenuKeys.Pause, MenuKeys.Separator, MenuKeys.CheckForUpdates, MenuKeys.About, MenuKeys.Quit,
        ], keys);
    }

    [Fact]
    public async Task With_the_screen_to_take_over_and_an_update_the_menu_has_those_in_their_places()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        _engine.RaiseScreenBusy(new ScreenBusyInfo(LConnectState.Service, HandoverAction.None, CanTakeOver: true));
        await Model.CheckForUpdates().WaitAsync(Wait.Timeout);

        var keys = Model.BuildMenu().Select(item => item.Key);

        Assert.Equal([
            MenuKeys.Status, MenuKeys.Separator, MenuKeys.Themes, MenuKeys.Orientation, MenuKeys.Separator,
            MenuKeys.Preview, MenuKeys.EditTheme, MenuKeys.NewTheme, MenuKeys.ThemesFolder, MenuKeys.Logs, MenuKeys.Settings, MenuKeys.Separator,
            MenuKeys.TakeOver, MenuKeys.AlwaysTakeOver, MenuKeys.Pause, MenuKeys.Separator, MenuKeys.InstallUpdate, MenuKeys.About,
            MenuKeys.Quit,
        ], keys);
    }

    [Fact]
    public void Unelevated_the_menu_has_no_handover_items()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        _engine.IsElevated = false;

        Assert.DoesNotContain(Model.BuildMenu(), item => item.Key is MenuKeys.TakeOver or MenuKeys.AlwaysTakeOver);
    }

    [Fact]
    public void Paused_the_status_says_the_screen_is_free_and_the_item_resumes()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        _engine.IsPaused = true;

        var menu = Model.BuildMenu();

        Assert.Equal("Paused: the screen is free for other programs", menu[0].Text);
        Assert.Equal("Resume", Item(menu, MenuKeys.Resume).Text);
        Assert.DoesNotContain(menu, item => item.Key == MenuKeys.Pause);
    }

    [Fact]
    public void With_no_screen_the_status_says_it_is_waiting_for_one()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        _engine.IsRunning = false;

        Assert.Equal("Waiting for the screen", Model.BuildMenu()[0].Text);
    }

    [Fact]
    public void A_theme_that_failed_at_start_says_so_with_the_error_under_it()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        _engine.IsRunning = false;
        _engine.ThemeError = "layout.yaml line 4: unknown blend 'sideways'";
        _engine.Problems = ["Theme error: layout.yaml line 4: unknown blend 'sideways'"];

        var menu = Model.BuildMenu();

        Assert.Equal("Not showing: the theme didn't load", menu[0].Text);
        var problem = menu[1];
        Assert.Equal(MenuKeys.Problem, problem.Key);
        Assert.Equal("Theme error: layout.yaml line 4: unknown blend 'sideways'", problem.Text);
        Assert.False(problem.Enabled);
    }

    [Fact]
    public void With_no_theme_found_there_is_nothing_to_pause_or_preview()
    {
        _engine.Problems = ["No theme named 'neon'"];

        var menu = Model.BuildMenu();

        Assert.Equal("Not showing anything", menu[0].Text);
        Assert.Equal("No theme named 'neon'", menu[1].Text);
        Assert.False(Item(menu, MenuKeys.Preview).Enabled);
        Assert.False(Item(menu, MenuKeys.Pause).Enabled);
        Assert.False(Item(menu, MenuKeys.Orientation).Enabled);
    }

    [Fact]
    public void A_long_problem_is_cut_to_its_first_line_and_a_menus_width()
    {
        _engine.Problems = ["first line " + new string('x', 200) + Environment.NewLine + "second line"];

        var text = Model.BuildMenu()[1].Text;

        Assert.Equal(TrayModel.MenuTextLimit, text.Length);
        Assert.StartsWith("first line xxx", text);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Themes_list_the_users_own_then_the_installed_ones_with_the_current_one_checked()
    {
        Theme("user", "neon");
        Theme("installed", "example-portrait");
        _engine.Showing(Theme("installed", "example-landscape"));

        var themes = Item(MenuKeys.Themes).Children;

        Assert.Equal(["neon", "", "example-landscape", "example-portrait"], themes.Select(item => item.Text));
        Assert.True(themes[1].IsSeparator);
        Assert.Equal([false, false, true, false], themes.Select(item => item.Checked));
    }

    [Fact]
    public void A_users_theme_with_an_installed_themes_name_is_the_one_listed()
    {
        var mine = Theme("user", "example-landscape");
        Theme("installed", "example-landscape");
        Theme("installed", "example-portrait");
        _engine.Showing(mine);

        var themes = Item(MenuKeys.Themes).Children;

        Assert.Equal(["example-landscape", "", "example-portrait"], themes.Select(item => item.Text));
        Assert.True(themes[0].Checked);
    }

    [Fact]
    public void With_no_themes_the_submenu_says_so()
    {
        var themes = Item(MenuKeys.Themes).Children;

        var only = Assert.Single(themes);
        Assert.Equal("No themes found", only.Text);
        Assert.False(only.Enabled);
    }

    [Fact]
    public void Orientation_lists_the_four_with_the_current_one_checked()
    {
        _engine.Showing(Theme("installed", "example-landscape"));
        _engine.Settings = _engine.Settings! with { Orientation = Orientation.PortraitFlipped };

        var orientations = Item(MenuKeys.Orientation).Children;

        Assert.Equal(["Landscape", "Landscape, upside down", "Portrait", "Portrait, upside down"], orientations.Select(item => item.Text));
        Assert.Equal([false, false, false, true], orientations.Select(item => item.Checked));
    }

    [Fact]
    public async Task Picking_a_theme_writes_config_yaml_then_switches_on_the_worker()
    {
        var neon = Theme("user", "neon");
        _engine.Showing(Theme("installed", "example-landscape"));
        File.WriteAllText(_paths.ConfigFile, "# mine\ntheme: example-landscape\nfps: 20\n");

        await Model.SwitchTheme(new ThemeEntry("neon", neon)).WaitAsync(Wait.Timeout);

        Assert.Equal("# mine\ntheme: neon\nfps: 20\n", File.ReadAllText(_paths.ConfigFile));
        var call = Assert.Single(_engine.Calls);
        Assert.Equal($"SwitchTheme {neon}", call.Call);
        Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread);
    }

    [Fact]
    public async Task The_theme_items_command_switches_to_that_theme()
    {
        var neon = Theme("user", "neon");
        _engine.Showing(Theme("installed", "example-landscape"));

        Item(MenuKeys.Themes).Children[0].Command!();

        Assert.True(await Wait.UntilAsync(() => _engine.CallNames.Contains($"SwitchTheme {neon}")));
        Assert.Equal("theme: neon\r\n", File.ReadAllText(_paths.ConfigFile)); // a new file: just the key
    }

    [Fact]
    public async Task A_config_yaml_marquee_cant_change_is_left_alone_and_the_reason_shown()
    {
        var neon = Theme("user", "neon");
        _engine.Showing(Theme("installed", "example-landscape"));
        File.WriteAllText(_paths.ConfigFile, "fps: 20\ntheme: 'unclosed\n");

        await Model.SwitchTheme(new ThemeEntry("neon", neon)).WaitAsync(Wait.Timeout);

        Assert.Equal("fps: 20\ntheme: 'unclosed\n", File.ReadAllText(_paths.ConfigFile));
        Assert.Empty(_engine.Calls);
        var balloon = Assert.Single(_host.Balloons);
        Assert.Equal("Marquee couldn't do that", balloon.Title);
        Assert.Contains("config.yaml", balloon.Text);
        Assert.True(balloon.IsError);
    }

    [Fact]
    public async Task Picking_an_orientation_writes_config_yaml_and_turns_the_picture()
    {
        _engine.Showing(Theme("installed", "example-landscape"));

        await Model.SetOrientation(Orientation.Portrait).WaitAsync(Wait.Timeout);

        Assert.Equal("orientation: portrait\r\n", File.ReadAllText(_paths.ConfigFile));
        Assert.Equal(["SetOrientation Portrait"], _engine.CallNames);
    }

    [Fact]
    public async Task Pause_and_resume_go_to_the_engine_on_the_worker()
    {
        _engine.Showing(Theme("installed", "example-landscape"));

        Item(MenuKeys.Pause).Command!();
        _engine.IsPaused = true;
        Item(MenuKeys.Resume).Command!();

        Assert.True(await Wait.UntilAsync(() => _engine.Calls.Count == 2));
        Assert.Equal(["Pause", "Resume"], _engine.CallNames);
        Assert.All(_engine.Calls, call => Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread));
    }

    [Fact]
    public async Task The_folders_open_through_the_shell_and_are_made_first()
    {
        await Model.OpenThemesFolder().WaitAsync(Wait.Timeout);
        await Model.OpenLogs().WaitAsync(Wait.Timeout);

        Assert.Equal([_paths.UserThemes, _paths.Logs], _shell.Opened);
        Assert.True(Directory.Exists(_paths.UserThemes));
        Assert.True(Directory.Exists(_paths.Logs));
    }

    /// The tray runs elevated and %LOCALAPPDATA%\Marquee is the user's: made through a junction there, the themes
    /// folder would be an administrator's folder made wherever the junction points.
    [Fact]
    public async Task A_missing_themes_folder_isnt_made_through_a_junction()
    {
        using var elsewhere = new TempDir();
        var link = _dir.File("Marquee");
        Links.MakeJunction(link, elsewhere.Path);
        try
        {
            var paths = _paths with { Locator = new ThemeLocator([Path.Combine(link, "themes")], _dir.Path) };
            var model = new TrayModel(_engine, paths, _shell, _host, _worker, _updates.Updates, NullLog.Instance, () => _now);

            await model.OpenThemesFolder().WaitAsync(Wait.Timeout);

            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere.Path));
            Assert.Empty(_shell.Opened);
            var balloon = Assert.Single(_host.Balloons);
            Assert.Contains(link, balloon.Text);
            Assert.True(balloon.IsError);
        }
        finally
        {
            Links.RemoveJunction(link);
        }
    }

    /// A themes folder that is already there is only opened, which Explorer does as the user, so one the user made a
    /// junction to keep their themes on another drive still opens.
    [Fact]
    public async Task A_themes_folder_that_is_already_there_is_opened_as_it_is()
    {
        using var elsewhere = new TempDir();
        Links.MakeJunction(_paths.UserThemes, elsewhere.Path);
        try
        {
            await Model.OpenThemesFolder().WaitAsync(Wait.Timeout);

            Assert.Equal([_paths.UserThemes], _shell.Opened);
            Assert.Empty(_host.Balloons);
        }
        finally
        {
            Links.RemoveJunction(_paths.UserThemes);
        }
    }

    [Fact]
    public async Task Edit_settings_opens_config_yaml_through_the_shell()
    {
        File.WriteAllText(_paths.ConfigFile, "theme: neon\n");

        await Model.EditSettings().WaitAsync(Wait.Timeout);

        Assert.Equal([_paths.ConfigFile], _shell.Opened);
        Assert.Equal("theme: neon\n", File.ReadAllText(_paths.ConfigFile));
    }

    [Fact]
    public async Task Edit_settings_first_writes_a_config_yaml_naming_the_theme_on_show()
    {
        await Model.EditSettings().WaitAsync(Wait.Timeout);

        Assert.Equal("theme: example-landscape\r\n", File.ReadAllText(_paths.ConfigFile));
        Assert.Equal([_paths.ConfigFile], _shell.Opened);
    }

    /// Explorer starts it, as the user: the tray runs elevated, and an elevated editor would save theme files as
    /// administrator (it refuses to run so). Explorer can't pass arguments; the editor opens the theme config.yaml names.
    [Fact]
    public async Task Edit_theme_starts_the_editor_beside_the_tray_through_the_shell()
    {
        var editor = _dir.Write("MarqueeEditor.exe", "");
        var model = new TrayModel(_engine, _paths with { EditorExe = editor }, _shell, _host, _worker, _updates.Updates,
            NullLog.Instance, () => _now);

        var item = Item(model.BuildMenu(), MenuKeys.EditTheme);
        item.Command!();

        Assert.Equal("Edit theme…", item.Text);
        Assert.True(item.Enabled);
        Assert.True(await Wait.UntilAsync(() => !_shell.Opened.IsEmpty));
        Assert.Equal([editor], _shell.Opened);
        Assert.Empty(_host.Balloons);
    }

    /// A development build has the editor in its own project's output folder, not beside the tray.
    [Fact]
    public async Task Edit_theme_without_the_editor_says_where_it_looked_and_opens_nothing()
    {
        var missing = _dir.File("MarqueeEditor.exe");
        var model = new TrayModel(_engine, _paths with { EditorExe = missing }, _shell, _host, _worker, _updates.Updates,
            NullLog.Instance, () => _now);

        await model.EditTheme().WaitAsync(Wait.Timeout);

        Assert.Empty(_shell.Opened);
        var balloon = Assert.Single(_host.Balloons);
        Assert.Contains(missing, balloon.Text);
        Assert.True(balloon.IsError);
    }

    [Fact]
    public async Task New_theme_asks_the_desktop_shell_to_open_the_editor_with_new()
    {
        var editor = _dir.Write("MarqueeEditor.exe", "");
        var model = new TrayModel(_engine, _paths with { EditorExe = editor }, _shell, _host, _worker, _updates.Updates,
            NullLog.Instance, () => _now);
        Item(model.BuildMenu(), MenuKeys.NewTheme).Command!();
        Assert.True(await Wait.UntilAsync(() => !_shell.Commands.IsEmpty));
        Assert.Equal((editor, "--new"), Assert.Single(_shell.Commands));
        Assert.Empty(_host.Balloons);
    }

    [Fact]
    public void The_installed_editor_is_looked_for_beside_the_tray() =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "MarqueeEditor.exe"), _paths.EditorExe);

    [Fact]
    public void Preview_and_about_go_to_the_window_side()
    {
        _engine.Showing(Theme("installed", "example-landscape"));

        Item(MenuKeys.Preview).Command!();
        Item(MenuKeys.About).Command!();

        Assert.Equal(1, _host.PreviewsOpened);
        var about = Assert.Single(_host.Messages);
        Assert.Equal("About Marquee", about.Title);
        Assert.StartsWith("Marquee ", about.Text);
    }

    [Fact]
    public async Task Start_starts_the_daily_update_check_after_the_engine()
    {
        await Model.Start().WaitAsync(Wait.Timeout);

        Assert.True(_updates.Delay.WaitForRequests(1));
        Assert.Equal(["Start"], _engine.CallNames);
    }

    [Fact]
    public async Task Quit_stops_the_engine_on_the_worker_then_exits()
    {
        await Model.Quit().WaitAsync(Wait.Timeout);

        var call = Assert.Single(_engine.Calls);
        Assert.Equal("Dispose", call.Call);
        Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread);
        Assert.Equal(1, _host.Exits);
    }

    [Fact]
    public async Task Start_starts_the_engine_on_the_worker()
    {
        await Model.Start().WaitAsync(Wait.Timeout);

        var call = Assert.Single(_engine.Calls);
        Assert.Equal("Start", call.Call);
        Assert.NotEqual(Environment.CurrentManagedThreadId, call.Thread);
        Assert.Empty(_host.Balloons);
    }

    [Fact]
    public async Task On_the_first_run_a_hint_says_where_to_pick_a_theme_once()
    {
        _engine.UsesDefaultTheme = true;

        await Model.Start().WaitAsync(Wait.Timeout);
        Assert.Empty(_host.Balloons); // not before the theme is on the screen
        _engine.RaiseScreenOpened();
        _engine.RaiseScreenOpened(); // after a pause, say
        var again = new TrayModel(_engine, _paths, _shell, _host, _worker, _updates.Updates, NullLog.Instance, () => _now);
        await again.Start().WaitAsync(Wait.Timeout);
        _engine.RaiseScreenOpened();

        var hint = Assert.Single(_host.Balloons);
        Assert.Equal("Marquee is showing an example theme", hint.Title);
        Assert.Contains("theme and the screen's orientation", hint.Text);
        Assert.False(hint.IsError);
    }

    [Fact]
    public void The_first_run_hint_isnt_shown_over_a_config_yaml_error_or_while_paused()
    {
        _engine.UsesDefaultTheme = false; // what a config.yaml that doesn't read gives
        _engine.RaiseScreenOpened();
        _engine.UsesDefaultTheme = true;
        _engine.IsPaused = true; // paused before the opening's report came through
        _engine.RaiseScreenOpened();

        Assert.Empty(_host.Balloons);
        Assert.False(File.Exists(Path.Combine(_paths.DataFolder, FirstRun.HintMarker)));
    }

    [Fact]
    public void A_theme_error_shows_a_balloon_at_most_once_a_minute()
    {
        _engine.RaiseThemeFailed("layout.yaml line 4: unknown blend 'sideways'");
        _engine.RaiseThemeFailed("layout.yaml line 5: another");
        _now += TrayModel.BalloonEvery;
        _engine.RaiseThemeFailed("layout.yaml line 6: and another");

        Assert.Equal(
            [("The theme didn't load", "layout.yaml line 4: unknown blend 'sideways'", true), ("The theme didn't load", "layout.yaml line 6: and another", true)],
            _host.Balloons);
    }

    [Fact]
    public void A_config_yaml_error_shows_a_balloon_too()
    {
        _engine.RaiseConfigFailed("config.yaml line 2: unknown key 'fsp'");

        Assert.Equal([("config.yaml has an error", "config.yaml line 2: unknown key 'fsp'", true)], _host.Balloons);
    }

    [Fact]
    public void Each_kind_of_error_has_its_own_balloon_a_minute()
    {
        // At logon, a broken config.yaml mustn't keep the theme's error (or any other kind) from showing.
        _engine.RaiseConfigFailed("config.yaml line 2: unknown key 'fsp'");
        _engine.RaiseThemeFailed("layout.yaml line 4: unknown blend 'sideways'");
        _engine.RaiseConfigFailed("config.yaml line 3: another");
        _engine.RaiseThemeFailed("layout.yaml line 5: another");

        Assert.Equal(
            [("config.yaml has an error", "config.yaml line 2: unknown key 'fsp'", true), ("The theme didn't load", "layout.yaml line 4: unknown blend 'sideways'", true)],
            _host.Balloons);
    }

    [Fact]
    public void A_balloon_holds_at_most_what_windows_shows()
    {
        _engine.RaiseThemeFailed(new string('x', 400));

        var balloon = Assert.Single(_host.Balloons);
        Assert.Equal(TrayModel.BalloonTextLimit, balloon.Text.Length);
        Assert.EndsWith("…", balloon.Text);
    }
}
