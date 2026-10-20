using System.Collections.Concurrent;
using Marquee.App.Tests.TestSupport;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Host;

namespace Marquee.App.Tests;

/// The tray's engine, really running, on a fake screen with temp folders for config.yaml, the themes and the data.
/// Each test has a screen marker of its own, so a real tray on this PC is never involved.
public sealed class TrayRuntimeTests : IDisposable
{
    const string Fill = "canvas: { orientation: landscape, fps: 10 }\nbackground:\n  - fill: \"#336699\"\n";

    readonly TempDir _dir = new();
    readonly FakeServices _services;
    readonly TrayPaths _paths;
    readonly string _marker = $@"Local\Marquee-Test-Screen-{Guid.NewGuid():N}";
    readonly ConcurrentQueue<string> _configErrors = new();
    readonly ConcurrentQueue<ScreenBusyInfo> _busy = new();
    readonly ConcurrentQueue<HandoverResult> _handovers = new();
    int _configsApplied;
    TrayRuntime? _runtime;

    public TrayRuntimeTests()
    {
        _services = new FakeServices(_dir.File("claude-limits.json"));
        _paths = new TrayPaths(_dir.File("config.yaml"), new ThemeLocator([_dir.File("user"), _dir.File("installed")], _dir.Path),
            _dir.File("logs"), _dir.File("data"));
        Theme("installed", "example-landscape");
    }

    public void Dispose()
    {
        _runtime?.Dispose();
        _dir.Dispose();
    }

    string Theme(string folder, string name, string layout = Fill) =>
        Path.GetDirectoryName(_dir.Write(Path.Combine(folder, name, "layout.yaml"), layout))!;

    TrayRuntime Start(TimeSpan? retryEvery = null)
    {
        _runtime = new TrayRuntime(_paths, NullLog.Instance, _services, _marker, retryEvery);
        _runtime.ConfigFailed += _configErrors.Enqueue;
        _runtime.ConfigApplied += () => Interlocked.Increment(ref _configsApplied);
        _runtime.ScreenBusy += _busy.Enqueue;
        _runtime.HandoverFinished += _handovers.Enqueue;
        _runtime.Start();
        return _runtime;
    }

    /// config.yaml as a user's editor saves it: written, then picked up by the watcher.
    void WriteConfig(string yaml) => File.WriteAllText(_paths.ConfigFile, yaml);

    [Fact]
    public void With_no_config_yaml_it_shows_example_landscape_from_the_installed_themes()
    {
        var runtime = Start();

        Assert.Equal(_dir.File(Path.Combine("installed", "example-landscape")), runtime.Settings!.ThemeFolder);
        Assert.True(runtime.UsesDefaultTheme);
        Assert.True(runtime.IsRunning);
        Assert.True(Wait.Until(() => _services.LastScreen.Frames > 0));
        Assert.Equal("example-landscape", runtime.Status.Theme);
    }

    [Fact]
    public void A_provider_that_fails_is_among_the_problems_until_it_works_again()
    {
        WriteConfig("providers: { claude: off, codex: off, fps: off }\n"); // only the Claude limits
        var runtime = Start();
        Assert.Empty(runtime.Problems);

        _services.AgentFault = "claude.exe wasn't found on PATH.";
        Assert.True(Wait.Until(() => runtime.Problems.Count > 0), "The provider's failure never reached the problems.");
        Assert.Equal(["ClaudeUsage failed: claude.exe wasn't found on PATH."], runtime.Problems);

        _services.AgentFault = null;
        Assert.True(Wait.Until(() => runtime.Problems.Count == 0), "The problem stayed after the provider recovered.");
    }

    [Fact]
    public void The_theme_config_yaml_names_is_shown_users_first()
    {
        var neon = Theme("user", "neon");
        Theme("installed", "neon");
        WriteConfig("theme: neon\n");

        var runtime = Start();

        Assert.Equal(neon, runtime.Settings!.ThemeFolder);
        Assert.False(runtime.UsesDefaultTheme);
        Assert.True(runtime.IsRunning);
    }

    [Fact]
    public void The_screen_marker_is_held_while_the_tray_wants_the_screen()
    {
        var runtime = Start();
        Assert.True(TrayScreen.IsHeld(_marker));

        runtime.Pause();
        Assert.False(TrayScreen.IsHeld(_marker));
        Assert.True(Wait.Until(() => !_services.LastScreen.IsOpen));

        runtime.Resume();
        Assert.True(TrayScreen.IsHeld(_marker));
        Assert.True(Wait.Until(() => _services.LastScreen.IsOpen));

        runtime.Dispose();
        Assert.False(TrayScreen.IsHeld(_marker));
    }

    [Fact]
    public void With_no_screen_it_waits_and_takes_the_screen_when_a_device_arrives()
    {
        _services.ScreenPluggedIn = false;
        var runtime = Start();

        Assert.False(runtime.IsRunning);
        Assert.NotNull(runtime.Settings);
        // It still wants the screen, and takes it as soon as it is there, so `marquee run` should hear about the tray.
        Assert.True(TrayScreen.IsHeld(_marker));

        _services.ScreenPluggedIn = true;
        runtime.RetrySoon();

        Assert.True(Wait.Until(() => runtime.IsRunning));
        Assert.True(Wait.Until(() => _services.LastScreen.Frames > 0));
    }

    [Fact]
    public void With_no_screen_it_also_tries_again_on_a_timer()
    {
        _services.ScreenPluggedIn = false;
        var runtime = Start(retryEvery: TimeSpan.FromMilliseconds(100));
        Assert.False(runtime.IsRunning);

        _services.ScreenPluggedIn = true;

        Assert.True(Wait.Until(() => runtime.IsRunning));
    }

    [Fact]
    public void A_broken_config_yaml_at_start_means_the_defaults_and_says_why()
    {
        WriteConfig("fsp: 20\n");

        var runtime = Start();

        Assert.True(runtime.IsRunning);
        Assert.Equal(_dir.File(Path.Combine("installed", "example-landscape")), runtime.Settings!.ThemeFolder);
        var problem = Assert.Single(runtime.Problems, p => p.StartsWith("config.yaml has an error", StringComparison.Ordinal));
        Assert.Contains("line 1", problem);
    }

    /// The first-run hint ("showing an example theme") would replace the config error's balloon and use up the one-time
    /// hint, though the user has a config.yaml (one from `marquee run`, say) that just doesn't read.
    [Fact]
    public void A_broken_config_yaml_at_start_isnt_a_first_run()
    {
        WriteConfig("fsp: 20\n");

        var runtime = Start();

        Assert.False(runtime.UsesDefaultTheme);
        Assert.Equal(_dir.File(Path.Combine("installed", "example-landscape")), runtime.Settings!.ThemeFolder);
    }

    /// The tray decides the first-run hint when the screen opens, which can be long after Start (while L-Connect held
    /// the screen, say), so UsesDefaultTheme follows config.yaml as last read, not as it was at Start.
    [Fact]
    public void UsesDefaultTheme_follows_the_config_yaml_last_read()
    {
        var neon = Theme("user", "neon");
        var landscape = _dir.File(Path.Combine("installed", "example-landscape"));
        WriteConfig("fsp: 20\n");
        var runtime = Start();
        Assert.False(runtime.UsesDefaultTheme);

        WriteConfig("theme: neon\n");
        Assert.True(Wait.Until(() => runtime.Settings?.ThemeFolder == neon));
        Assert.False(runtime.UsesDefaultTheme);

        WriteConfig("orientation: portrait\n");
        Assert.True(Wait.Until(() => runtime.Settings?.ThemeFolder == landscape));
        Assert.True(runtime.UsesDefaultTheme);
    }

    [Fact]
    public void A_theme_that_cant_be_found_waits_for_a_config_yaml_that_names_one_that_can()
    {
        WriteConfig("theme: nowhere\n");

        var runtime = Start();

        Assert.Null(runtime.Settings);
        Assert.False(runtime.IsRunning);
        Assert.Contains(runtime.Problems, problem => problem.Contains("nowhere", StringComparison.Ordinal));
        Assert.False(TrayScreen.IsHeld(_marker)); // nothing to show, so nothing to hold

        WriteConfig("theme: example-landscape\n");

        Assert.True(Wait.Until(() => runtime.IsRunning));
        Assert.True(TrayScreen.IsHeld(_marker));
        Assert.DoesNotContain(runtime.Problems, problem => problem.Contains("nowhere", StringComparison.Ordinal));
    }

    [Fact]
    public void An_edit_to_config_yaml_is_applied_while_running()
    {
        var neon = Theme("user", "neon");
        var runtime = Start();

        WriteConfig("theme: neon\n");

        Assert.True(Wait.Until(() => runtime.Settings?.ThemeFolder == neon));
    }

    [Fact]
    public void A_broken_edit_keeps_the_settings_and_says_why()
    {
        var runtime = Start();
        var before = runtime.Settings;

        WriteConfig("fsp: 20\n");

        Assert.True(Wait.Until(() => !_configErrors.IsEmpty));
        Assert.Contains("line 1", _configErrors.Single());
        Assert.Contains(runtime.Problems, problem => problem.EndsWith("(the current settings stay until config.yaml is fixed)", StringComparison.Ordinal));
        Assert.Equal(before, runtime.Settings);
    }

    [Fact]
    public void A_theme_that_doesnt_load_is_a_problem_until_one_does()
    {
        Theme("user", "broken", "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#00FF00\"\n    blend: sideways\n");
        WriteConfig("theme: broken\n");

        var runtime = Start();

        Assert.False(runtime.IsRunning);
        // The engine raises ThemeFailed as Start returns, but on another thread if one is raising events already.
        Assert.True(Wait.Until(() => runtime.ThemeError is not null));
        Assert.Contains(runtime.Problems, problem => problem.StartsWith("Theme error: ", StringComparison.Ordinal));

        WriteConfig("theme: example-landscape\n");

        Assert.True(Wait.Until(() => runtime.IsRunning && runtime.ThemeError is null));
        Assert.DoesNotContain(runtime.Problems, problem => problem.StartsWith("Theme error: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Game_fps_needs_an_elevated_tray_and_says_so()
    {
        var runtime = Start();

        Assert.Contains("Game FPS is off: Marquee isn't running as administrator.", runtime.Problems);
    }

    [Fact]
    public void Game_fps_says_nothing_when_turned_off_or_elevated()
    {
        WriteConfig("providers: { fps: off }\n");
        Assert.Empty(Start().Problems);

        _runtime!.Dispose();
        WriteConfig("theme: example-landscape\n");
        _services.IsElevated = true;
        Assert.Empty(Start().Problems);
    }

    [Fact]
    public void Switching_theme_and_orientation_reach_the_engine()
    {
        var neon = Theme("user", "neon");
        var runtime = Start();

        Assert.True(runtime.SwitchTheme(neon));
        Assert.Equal(neon, runtime.Settings!.ThemeFolder);

        Assert.True(runtime.SetOrientation(Orientation.Portrait));
        Assert.Equal(Orientation.Portrait, runtime.Settings!.Orientation);
        Assert.Equal(480, _services.LastScreen.Info.Width); // a portrait screen
    }

    [Fact]
    public void Updates_follow_config_yaml_and_each_edit_that_applies_is_announced()
    {
        WriteConfig("updates: off\n");
        var runtime = Start();
        Assert.False(runtime.CheckForUpdates);

        WriteConfig("updates: check\n");

        Assert.True(Wait.Until(() => runtime.CheckForUpdates && Volatile.Read(ref _configsApplied) > 0));
    }

    [Fact]
    public void A_broken_edit_keeps_the_update_setting()
    {
        WriteConfig("updates: off\n");
        var runtime = Start();

        WriteConfig("updates: sometimes\n");

        Assert.True(Wait.Until(() => !_configErrors.IsEmpty));
        Assert.False(runtime.CheckForUpdates);
        Assert.Equal(0, Volatile.Read(ref _configsApplied));
    }

    [Fact]
    public void A_busy_screen_is_passed_on_to_the_tray()
    {
        _services.ScreenInUse = true;

        Start();

        Assert.True(Wait.Until(() => !_busy.IsEmpty));
        // The fake PC has no L-Connect, so there is nothing to take the screen from.
        Assert.Equal(new ScreenBusyInfo(LConnectState.NotRunning, HandoverAction.None, CanTakeOver: false), _busy.Single());
    }

    [Fact]
    public void Unelevated_the_tray_cant_take_the_screen_over()
    {
        var runtime = Start();

        Assert.False(runtime.IsElevated);
        Assert.False(runtime.TakeOverScreen());
        Assert.Empty(_handovers);
    }

    [Fact]
    public void The_screen_opening_is_passed_on_to_the_tray()
    {
        var opened = 0;
        _runtime = new TrayRuntime(_paths, NullLog.Instance, _services, _marker);
        _runtime.ScreenOpened += () => Interlocked.Increment(ref opened);

        _runtime.Start();

        Assert.True(Wait.Until(() => Volatile.Read(ref opened) > 0), "ScreenOpened never came.");
    }

    [Fact]
    public void HandoverChosen_says_whether_config_yaml_sets_lconnect_handover()
    {
        var runtime = Start();
        Assert.False(runtime.HandoverChosen);

        WriteConfig("lconnect: { handover: off }\n");

        Assert.True(Wait.Until(() => runtime.HandoverChosen), "The edit never reached HandoverChosen.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LConnectInstalled_comes_from_L_Connect_s_service(bool installed)
    {
        _services.LConnectInstalled = installed;

        Assert.Equal(installed, Start().LConnectInstalled);
    }

    [Fact]
    public void Elevated_a_take_over_runs_and_its_end_is_passed_on_to_the_tray()
    {
        _services.IsElevated = true;
        _services.ScreenInUse = true; // Marquee having the screen would leave nothing to take
        var runtime = Start();

        Assert.True(runtime.IsElevated);
        Assert.True(runtime.TakeOverScreen());

        Assert.True(Wait.Until(() => !_handovers.IsEmpty));
        // The fake services refuse to touch L-Connect's, which a handover reports as failed.
        Assert.Equal(HandoverOutcome.Failed, _handovers.Single().Outcome);
    }
}
