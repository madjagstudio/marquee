using System.Collections.Concurrent;
using System.Globalization;
using Marquee.Core.Devices;
using Marquee.Host.Tests.TestSupport;
using Marquee.Providers.Custom;
using SkiaSharp;

namespace Marquee.Host.Tests;

public class MarqueeEngineTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly TempTheme _theme = new(TempTheme.Fill("#FF0000"));
    readonly ListLog _log = new();
    readonly FakeServices _services;
    readonly EngineSettings _settings;

    public MarqueeEngineTests()
    {
        _services = new FakeServices(_dir.File("claude-limits.json"));
        _settings = new EngineSettings { ThemeFolder = _theme.Folder, Sensors = "hwinfo" };
    }

    public void Dispose()
    {
        _theme.Dispose();
        _dir.Dispose();
    }

    MarqueeEngine Engine(EngineSettings? settings = null) => new(settings ?? _settings, _log, _services);

    static void AssertRed(SKColor color) => Assert.Equal(SKColors.Red, color);

    [Fact]
    public void Start_renders_the_theme_to_the_screen()
    {
        using var engine = Engine();

        Assert.True(engine.Start());

        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed >= 3), "No frames reached the screen.");
        AssertRed(_services.LastDevice.LastPixel);
        var status = engine.Status;
        Assert.Equal((30, _theme.Name, "Recording device"), (status.TargetFps, status.Theme, status.Device));
        Assert.True(status.FramesSent >= 3);
        Assert.Contains($"info: Showing theme '{_theme.Name}'.", _log.Entries);
    }

    [Fact]
    public void Start_without_a_screen_fails_before_starting_anything()
    {
        _services.Device = _ => null;
        using var engine = Engine();

        Assert.False(engine.Start());

        Assert.False(engine.IsRunning);
        Assert.Contains("error: No supported screen found. Run `marquee devices` to check.", _log.Entries);
        Assert.Empty(_services.HardwareRequests);
        Assert.Empty(_services.AgentRequests);
        Assert.Empty(_services.Servers);
    }

    [Fact]
    public void Start_with_a_broken_theme_fails_reports_the_error_and_releases_the_screen()
    {
        _theme.Write(TempTheme.Broken);
        using var engine = Engine();
        var failed = new List<string>();
        engine.ThemeFailed += failed.Add;

        Assert.False(engine.Start());

        Assert.False(engine.IsRunning);
        var error = Assert.Single(failed);
        Assert.StartsWith("layout.yaml line 4: ", error);
        Assert.Contains($"error: {error}", _log.Entries);
        Assert.True(_services.LastDevice.Disposed);
        Assert.Equal(0, _services.LastDevice.FramesPushed);
        Assert.Empty(_services.AgentRequests);
        Assert.Empty(_services.Servers);
    }

    [Theory]
    [InlineData(true, true, true, "ClaudeSessions ClaudeUsage CodexLimits CodexSessions")]
    [InlineData(false, true, true, "ClaudeUsage CodexLimits CodexSessions")]
    [InlineData(true, false, true, "ClaudeSessions CodexLimits CodexSessions")]
    [InlineData(true, true, false, "ClaudeSessions ClaudeUsage")]
    [InlineData(false, false, false, "")]
    public void Start_asks_for_exactly_the_providers_the_switches_allow(bool claudeSessions, bool claudeUsage, bool codex, string expected)
    {
        using var engine = Engine(_settings with { ClaudeSessions = claudeSessions, ClaudeUsage = claudeUsage, Codex = codex });

        Assert.True(engine.Start());

        Assert.Equal(expected, string.Join(" ", _services.AgentRequests));
        Assert.Equal(["hwinfo"], _services.HardwareRequests);
    }

    const string FpsNeedsAdministrator =
        "warn: FPS is off: PresentMon needs Marquee to run as administrator. " +
        "Set `providers: { fps: off }` in config.yaml to stop this message.";

    [Fact]
    public void Start_runs_the_fps_provider_when_it_is_on_and_Marquee_is_elevated()
    {
        _services.Elevated = true;
        using var engine = Engine(_settings with { GameFps = true });

        Assert.True(engine.Start());

        Assert.Equal(1, _services.FpsRequests);
        Assert.Single(_services.Providers, p => p.Name == "FPS");
        Assert.DoesNotContain(FpsNeedsAdministrator, _log.Entries);
    }

    [Fact]
    public void Start_unelevated_leaves_fps_off_and_says_why_once()
    {
        _services.Elevated = false;
        using var engine = Engine(_settings with { GameFps = true });

        Assert.True(engine.Start());

        Assert.Equal(0, _services.FpsRequests);
        Assert.Single(_log.Entries, e => e == FpsNeedsAdministrator);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Start_with_fps_switched_off_neither_runs_it_nor_mentions_it(bool elevated)
    {
        _services.Elevated = elevated;
        using var engine = Engine(_settings with { GameFps = false });

        Assert.True(engine.Start());

        Assert.Equal(0, _services.FpsRequests);
        Assert.DoesNotContain(_log.Entries, e => e.Contains("FPS", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_switching_fps_stops_or_starts_only_the_fps_provider()
    {
        _services.Elevated = true;
        using var engine = Engine(_settings with { GameFps = true });
        engine.Start();
        var fps = Assert.Single(_services.Providers, p => p.Name == "FPS");
        var others = _services.Providers.Where(p => p != fps).ToList();

        Assert.True(engine.Apply(_settings with { GameFps = false }));
        Assert.True(fps.Disposed);

        Assert.True(engine.Apply(_settings with { GameFps = true }));
        Assert.Equal(2, _services.FpsRequests);
        Assert.Equal("FPS", _services.Providers[^1].Name);
        Assert.False(_services.Providers[^1].Disposed);
        Assert.All(others, p => Assert.False(p.Disposed));
    }

    [Fact]
    public void Start_gives_the_http_server_its_port_and_key_limit()
    {
        using var engine = Engine(_settings with { HttpPort = 50123, MaxCustomKeys = 250 });

        Assert.True(engine.Start());

        var http = Assert.Single(_services.HttpServers);
        Assert.Equal((50123, 250), (http.Port, http.MaxKeys));
        Assert.Single(_services.Servers, s => s.Kind == "status line");
    }

    [Fact]
    public void Start_with_no_port_runs_no_http_server()
    {
        using var engine = Engine(_settings with { HttpPort = null });

        Assert.True(engine.Start());

        Assert.Empty(_services.HttpServers);
        Assert.Single(_services.Servers, s => s.Kind == "status line");
    }

    [Fact]
    public void Theme_events_fire_on_start_on_a_broken_save_and_on_the_fix()
    {
        _theme.Write(TempTheme.Fill("#FF0000") + "  - image: missing.png\n");
        using var engine = Engine();
        var loaded = new BlockingCollection<LoadedThemeInfo>();
        var failed = new BlockingCollection<string>();
        engine.ThemeLoaded += loaded.Add;
        engine.ThemeFailed += failed.Add;

        Assert.True(engine.Start());
        Assert.True(loaded.TryTake(out var first), "No ThemeLoaded at start.");
        Assert.Equal(_theme.Name, first!.Name);
        Assert.Equal(["Image 'missing.png' was not found and was skipped."], first.Warnings);
        Assert.Contains("warn: Image 'missing.png' was not found and was skipped.", _log.Entries);

        _theme.Write(TempTheme.Broken);
        Assert.True(failed.TryTake(out var error, Wait.Timeout), "No ThemeFailed after a broken save.");
        Assert.StartsWith("layout.yaml line 4: ", error);
        Assert.True(Wait.Until(() => _log.Entries.Contains($"error: {error} (the last good layout stays on screen until the file is fixed)")));

        _theme.Write(TempTheme.Fill("#0000FF"));
        Assert.True(loaded.TryTake(out var fixedTheme, Wait.Timeout), "No ThemeLoaded after the fix.");
        Assert.Empty(fixedTheme!.Warnings);
        Assert.True(Wait.Until(() => _services.LastDevice.LastPixel == SKColors.Blue), "The fixed theme never reached the screen.");
    }

    [Fact]
    public void FrameRendered_passes_each_frame_on()
    {
        using var engine = Engine();
        var frames = 0;
        engine.FrameRendered += image =>
        {
            Assert.Equal(1920, image.Width);
            Interlocked.Increment(ref frames);
        };

        engine.Start();

        Assert.True(Wait.Until(() => Volatile.Read(ref frames) >= 2));
    }

    [Fact]
    public void RenderFrames_renders_exactly_that_many_without_the_render_thread()
    {
        using var engine = Engine();

        Assert.True(engine.Start(liveRendering: false));
        Assert.Equal(3, engine.RenderFrames(3));

        Assert.Equal(3, _services.LastDevice.FramesPushed);
        AssertRed(_services.LastDevice.LastPixel);
    }

    [Fact]
    public void RenderFrames_needs_a_start_without_live_rendering()
    {
        using var engine = Engine();
        Assert.Throws<InvalidOperationException>(() => engine.RenderFrames(1));

        engine.Start();

        Assert.Throws<InvalidOperationException>(() => engine.RenderFrames(1));
    }

    [Fact]
    public void Apply_with_a_new_frame_rate_keeps_the_screen_open_and_everything_else_running()
    {
        using var engine = Engine();
        engine.Start();
        var screen = _services.LastDevice;
        Assert.True(Wait.Until(() => screen.FramesPushed > 0));
        var providers = _services.Providers.ToList();
        engine.Sensors.Set("custom.kept", 42);

        Assert.True(engine.Apply(_settings with { FpsOverride = 12 }));

        Assert.Equal(12, engine.Status.TargetFps);
        AssertUntouched(screen, providers);
        Assert.True(engine.Sensors.Snapshot().TryGet("custom.kept", out var kept));
        Assert.Equal(42, kept.Number);
        var pushed = screen.FramesPushed;
        Assert.True(Wait.Until(() => screen.FramesPushed > pushed), "The loop stopped sending.");
        Assert.Equal(_settings with { FpsOverride = 12 }, engine.Settings);
        Assert.Contains("info: The settings changed; applying the new ones.", _log.Entries);
    }

    /// The screen stayed open the whole time, and no provider or server was stopped or added.
    void AssertUntouched(RecordingDevice screen, IReadOnlyList<FakeProvider> providers)
    {
        Assert.Same(screen, Assert.Single(_services.Devices));
        Assert.Equal((1, 0, false), (screen.Opens, screen.Closes, screen.Disposed));
        Assert.Equal(providers, _services.Providers);
        Assert.All(providers, p => Assert.False(p.Disposed));
        Assert.All(_services.Servers, s => Assert.False(s.Disposed));
    }

    [Fact]
    public void Apply_with_the_same_settings_restarts_nothing()
    {
        using var engine = Engine();
        engine.Start();

        Assert.True(engine.Apply(_settings with { }));

        Assert.Single(_services.Devices);
        Assert.False(_services.LastDevice.Disposed);
    }

    [Fact]
    public void Apply_with_a_broken_theme_keeps_the_previous_one_on_the_open_screen()
    {
        using var broken = new TempTheme(TempTheme.Broken);
        using var engine = Engine();
        engine.Start();

        var screen = _services.LastDevice;
        Assert.True(Wait.Until(() => screen.FramesPushed > 0));
        var providers = _services.Providers.ToList();

        Assert.False(engine.Apply(_settings with { ThemeFolder = broken.Folder }));

        Assert.True(engine.IsRunning);
        Assert.Equal(_settings, engine.Settings);
        AssertUntouched(screen, providers);
        Assert.Contains("error: The new settings could not be applied, so Marquee kept the previous ones.", _log.Entries);
        var pushed = screen.FramesPushed;
        Assert.True(Wait.Until(() => screen.FramesPushed > pushed), "The previous theme stopped showing.");
        AssertRed(screen.LastPixel);
    }

    [Fact]
    public void A_change_to_a_broken_theme_applies_once_the_theme_is_fixed()
    {
        using var broken = new TempTheme(TempTheme.Broken);
        using var engine = Engine();
        engine.Start();
        var screen = _services.LastDevice;
        var wanted = _settings with { ThemeFolder = broken.Folder };

        Assert.False(engine.Apply(wanted));
        Assert.Contains($"info: Once theme '{broken.Name}' is fixed and saved, Marquee switches to it with the new settings.", _log.Entries);

        // Saving it still broken says why, as a hot reload would; saving it fixed applies the change.
        broken.Write(TempTheme.Broken.Replace("sideways", "upwards"));
        Assert.True(Wait.Until(() => _log.Entries.Any(e => e.Contains("upwards") && e.EndsWith("(Marquee switches to this theme once it loads)"))),
            "Saving the theme still broken logged nothing.");
        broken.Write(TempTheme.Fill("#0000FF"));

        Assert.True(Wait.Until(() => engine.Settings == wanted), "The change was never applied.");
        Assert.True(Wait.Until(() => screen.LastPixel == SKColors.Blue), "The fixed theme never reached the screen.");
        Assert.Same(screen, Assert.Single(_services.Devices));
        Assert.Null(engine.WaitingForTheme);
    }

    [Fact]
    public void Another_change_or_stop_ends_the_wait_for_a_broken_theme()
    {
        using var broken = new TempTheme(TempTheme.Broken);
        using var engine = Engine();
        engine.Start();

        Assert.False(engine.Apply(_settings with { ThemeFolder = broken.Folder }));
        Assert.Equal(broken.Folder, engine.WaitingForTheme);
        Assert.True(engine.Apply(_settings with { FpsOverride = 12 }));
        Assert.Null(engine.WaitingForTheme);

        Assert.False(engine.Apply(_settings with { ThemeFolder = broken.Folder }));
        Assert.True(engine.Apply(_settings with { FpsOverride = 12 })); // back to the running settings: nothing to wait for
        Assert.Null(engine.WaitingForTheme);

        Assert.False(engine.Apply(_settings with { ThemeFolder = broken.Folder }));
        engine.Stop();
        Assert.Null(engine.WaitingForTheme);
    }

    [Fact]
    public void Apply_before_start_only_changes_the_settings()
    {
        using var engine = Engine();

        Assert.True(engine.Apply(_settings with { FpsOverride = 12 }));

        Assert.Empty(_services.Devices);
        Assert.True(engine.Start());
        Assert.Equal(12, engine.Status.TargetFps);
    }

    [Fact]
    public async Task Apply_from_other_threads_while_rendering_is_safe()
    {
        using var engine = Engine();
        engine.Start();

        // The config watcher calls Apply on a thread-pool thread; several at once is the worst case.
        var applied = await Task.WhenAll(Enumerable.Range(1, 4)
            .Select(fps => Task.Run(() => engine.Apply(_settings with { FpsOverride = 10 + fps }))))
            .WaitAsync(Wait.Timeout);

        Assert.All(applied, Assert.True);
        Assert.True(engine.IsRunning);
        Assert.Equal(engine.Settings.FpsOverride, engine.Status.TargetFps);
        Assert.Equal(_services.Devices.Count - 1, _services.Devices.Count(d => ((RecordingDevice)d).Disposed));
    }

    [Fact]
    public void Apply_with_a_new_theme_loads_it_first_then_swaps_it_in_on_the_open_screen()
    {
        using var blue = new TempTheme(TempTheme.Fill("#0000FF", fps: 20));
        using var engine = Engine();
        var loaded = new BlockingCollection<LoadedThemeInfo>();
        engine.ThemeLoaded += loaded.Add;
        engine.Start();
        var screen = _services.LastDevice;
        Assert.True(Wait.Until(() => screen.FramesPushed > 0));
        var providers = _services.Providers.ToList();

        Assert.True(engine.Apply(_settings with { ThemeFolder = blue.Folder }));

        Assert.Equal([_theme.Name, blue.Name], loaded.Select(t => t.Name));
        Assert.True(Wait.Until(() => screen.LastPixel == SKColors.Blue), "The new theme never reached the screen.");
        AssertUntouched(screen, providers);
        Assert.Equal((20, blue.Name), (engine.Status.TargetFps, engine.Status.Theme));

        // The new theme's folder is the one watched now.
        blue.Write(TempTheme.Fill("#00FF00"));
        Assert.True(Wait.Until(() => screen.LastPixel == SKColors.Lime), "Saving the new theme did not reload it.");
    }

    [Fact]
    public void SwitchTheme_changes_only_the_theme_folder()
    {
        using var blue = new TempTheme(TempTheme.Fill("#0000FF"));
        using var engine = Engine(_settings with { FpsOverride = 12 });
        engine.Start();

        Assert.True(engine.SwitchTheme(blue.Folder));

        Assert.Equal(_settings with { FpsOverride = 12, ThemeFolder = blue.Folder }, engine.Settings);
        Assert.True(Wait.Until(() => _services.LastDevice.LastPixel == SKColors.Blue));
        Assert.Single(_services.Devices);
    }

    [Fact]
    public void Apply_with_a_new_orientation_replaces_only_the_screen()
    {
        using var engine = Engine();
        engine.Start();
        var first = _services.LastDevice;
        var providers = _services.Providers.ToList();

        Assert.True(engine.Apply(_settings with { Orientation = Orientation.Portrait }));

        Assert.Equal(2, _services.Devices.Count);
        Assert.Equal(Orientation.Portrait, _services.DeviceRequests[^1].Orientation);
        Assert.True(first.Disposed);
        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed > 0), "The new screen got nothing.");
        Assert.Equal(providers, _services.Providers);
        Assert.All(providers, p => Assert.False(p.Disposed));
        Assert.All(_services.Servers, s => Assert.False(s.Disposed));
    }

    [Fact]
    public void Apply_with_a_new_sensor_source_restarts_only_the_hardware_provider()
    {
        using var engine = Engine();
        engine.Start();
        var hwinfo = Assert.Single(_services.Providers, p => p.Name == "hwinfo");
        var others = _services.Providers.Where(p => p != hwinfo).ToList();

        Assert.True(engine.Apply(_settings with { Sensors = "lhm" }));

        Assert.Equal(["hwinfo", "lhm"], _services.HardwareRequests);
        Assert.True(hwinfo.Disposed);
        Assert.False(Assert.Single(_services.Providers, p => p.Name == "lhm").Disposed);
        Assert.All(others, p => Assert.False(p.Disposed));
        Assert.Single(_services.Devices);
    }

    [Fact]
    public void Apply_switching_an_agent_provider_stops_or_starts_only_that_one()
    {
        using var engine = Engine();
        engine.Start();
        var usage = Assert.Single(_services.Providers, p => p.Name == "ClaudeUsage");
        var others = _services.Providers.Where(p => p != usage).ToList();

        Assert.True(engine.Apply(_settings with { ClaudeUsage = false }));
        Assert.True(usage.Disposed);
        Assert.All(others, p => Assert.False(p.Disposed));

        Assert.True(engine.Apply(_settings));
        Assert.Equal(2, _services.AgentRequests.Count(s => s == AgentSource.ClaudeUsage));
        Assert.False(_services.Providers[^1].Disposed);
        Assert.All(others, p => Assert.False(p.Disposed));
        Assert.Equal(4, _services.AgentRequests.Distinct().Count());
        Assert.Equal(5, _services.AgentRequests.Count);
    }

    [Fact]
    public void Apply_with_a_new_port_or_key_limit_restarts_only_the_http_server()
    {
        using var engine = Engine();
        engine.Start();
        var screen = _services.LastDevice;
        var providers = _services.Providers.ToList();
        var statusLine = Assert.Single(_services.Servers, s => s.Kind == "status line");

        Assert.True(engine.Apply(_settings with { HttpPort = 50124 }));
        Assert.True(engine.Apply(_settings with { HttpPort = 50124, MaxCustomKeys = 10 }));

        Assert.Equal([(CustomValuesServer.DefaultPort, 1000, true), (50124, 1000, true), (50124, 10, false)],
            _services.HttpServers.Select(s => (s.Port, s.MaxKeys, s.Disposed)));
        Assert.False(statusLine.Disposed);
        Assert.Same(screen, Assert.Single(_services.Devices));
        Assert.All(providers, p => Assert.False(p.Disposed));
    }

    [Fact]
    public void Apply_needing_a_screen_that_is_gone_keeps_the_old_one_and_a_later_Apply_succeeds()
    {
        using var engine = Engine();
        engine.Start();
        var first = _services.LastDevice;
        var portrait = _settings with { Orientation = Orientation.Portrait };

        _services.Device = _ => null; // unplugged, or L-Connect has it
        Assert.False(engine.Apply(portrait));

        Assert.True(engine.IsRunning);
        Assert.Equal(_settings, engine.Settings);
        Assert.False(first.Disposed);
        Assert.Contains("error: No supported screen found. Run `marquee devices` to check.", _log.Entries);

        _services.Device = _ => new RecordingDevice(480, 1920); // back
        Assert.True(engine.Apply(portrait));

        Assert.Equal(portrait, engine.Settings);
        Assert.True(first.Disposed);
        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed > 0), "The new screen got nothing.");
    }

    [Fact]
    public void A_change_that_fails_part_way_stops_the_engine_until_the_next_Apply_starts_it_again()
    {
        using var engine = Engine();
        engine.Start();
        var portrait = _settings with { Orientation = Orientation.Portrait };

        _services.Device = _ => throw new IOException("The USB stack is resetting.");
        Assert.False(engine.Apply(portrait));

        Assert.False(engine.IsRunning);
        Assert.Contains("error: Applying the new settings failed (The USB stack is resetting.), so Marquee stopped. " +
            "It starts again at the next settings change.", _log.Entries);
        Assert.All(_services.Providers, p => Assert.True(p.Disposed));

        _services.Device = _ => new RecordingDevice(480, 1920);
        Assert.True(engine.Apply(portrait));

        Assert.True(engine.IsRunning);
        Assert.Equal(portrait, engine.Settings);
        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed > 0), "The engine did not come back.");
    }

    [Fact]
    public void Stop_then_Apply_only_changes_the_settings()
    {
        using var engine = Engine();
        engine.Start();
        engine.Stop();

        Assert.True(engine.Apply(_settings with { FpsOverride = 12 }));

        Assert.False(engine.IsRunning);
        Assert.Single(_services.Devices);
    }

    [Fact]
    public void Pause_closes_the_screen_while_the_providers_run_on_and_Resume_opens_it_again()
    {
        using var engine = Engine();
        engine.Start();
        var screen = _services.LastDevice;
        Assert.True(Wait.Until(() => screen.FramesPushed > 0));

        engine.Pause();

        Assert.True(engine.IsPaused);
        Assert.Equal((1, 1), (screen.Opens, screen.Closes));
        Assert.All(_services.Providers, p => Assert.False(p.Disposed));
        Assert.All(_services.Servers, s => Assert.False(s.Disposed));

        engine.Resume();

        Assert.False(engine.IsPaused);
        var pushed = screen.FramesPushed;
        Assert.True(Wait.Until(() => screen.FramesPushed > pushed), "Nothing reached the screen after Resume.");
        Assert.Equal(2, screen.Opens);
    }

    [Fact]
    public void A_new_screen_while_paused_stays_closed_until_Resume()
    {
        using var engine = Engine();
        engine.Pause(); // before Start: it starts paused
        engine.Start();
        Assert.True(engine.Apply(_settings with { Orientation = Orientation.Portrait }));

        // The sending thread would have opened a screen well within this, had the loop been started.
        Thread.Sleep(300);
        var screen = _services.LastDevice;
        Assert.Equal(2, _services.Devices.Count);
        Assert.Equal((0, 0), (_services.Devices[0] is RecordingDevice first ? first.Opens : -1, screen.Opens));
        engine.Resume();
        Assert.True(Wait.Until(() => screen.FramesPushed > 0), "Nothing reached the new screen after Resume.");
    }

    [Fact]
    public async Task A_ThemeLoaded_handler_may_wait_for_another_thread_that_calls_the_engine()
    {
        // The tray's UI thread calls Apply while a hot reload's handler waits for the UI thread (Control.Invoke, say).
        // That deadlocked while the handler ran under the theme watcher's lock, which Apply waits for.
        using var engine = Engine();
        engine.Start();
        var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.ThemeLoaded += _ =>
        {
            if (applied.Task.IsCompleted) return;
            var done = new ManualResetEventSlim(); // not disposed: the thread may set it after the wait gave up
            var ok = false;
            new Thread(() =>
            {
                ok = engine.Apply(_settings with { FpsOverride = 12 });
                done.Set();
            }).Start();
            applied.TrySetResult(done.Wait(TimeSpan.FromSeconds(5)) && ok);
        };

        _theme.Write(TempTheme.Fill("#0000FF")); // a hot reload, on the watcher's thread

        Assert.True(await applied.Task.WaitAsync(Wait.Timeout), "Apply could not finish while the handler waited for it.");
    }

    [Fact]
    public void A_throwing_event_handler_is_logged_and_the_engine_carries_on()
    {
        using var engine = Engine();
        engine.ThemeLoaded += _ => throw new InvalidOperationException("The banner is gone.");

        Assert.True(engine.Start());

        Assert.Contains("error: A handler of the engine's events failed: The banner is gone.", _log.Entries);
        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed > 0));
    }

    [Fact]
    public void Dispose_stops_providers_servers_and_the_screen_and_can_be_repeated()
    {
        var engine = Engine();
        engine.Start();
        Assert.True(Wait.Until(() => _services.LastDevice.FramesPushed > 0));

        engine.Dispose();
        engine.Dispose();
        engine.Stop();

        Assert.False(engine.IsRunning);
        Assert.NotEmpty(_services.Providers);
        Assert.All(_services.Providers, p => Assert.True(p.Disposed));
        Assert.All(_services.Servers, s => Assert.True(s.Disposed));
        Assert.Equal(1, _services.LastDevice.Closes);
        Assert.True(_services.LastDevice.Disposed);
        Assert.Equal(EngineStatus.Stopped, engine.Status);
    }

    [Fact]
    public void Claude_limits_are_restored_at_start_and_saved_at_stop()
    {
        var resetsAt = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds();
        var updatedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        _dir.Write("claude-limits.json", string.Create(CultureInfo.InvariantCulture,
            $"{{ \"five_hour\": {{ \"pct\": 42.5, \"resets_at\": {resetsAt} }}, \"updated_at\": {updatedAt} }}"));
        using var engine = Engine();

        engine.Start();
        Assert.True(engine.Sensors.Snapshot().TryGet("claude.five_hour.pct", out var restored));
        Assert.Equal(42.5, restored.Number);

        engine.Sensors.Set("claude.five_hour.pct", 61);
        engine.Stop();

        Assert.Contains("\"pct\": 61", File.ReadAllText(_dir.File("claude-limits.json")));
    }

    [Fact]
    public void Claude_limits_are_saved_while_running()
    {
        using var engine = Engine();
        engine.Start();

        engine.Sensors.Set("claude.five_hour.pct", 17);
        engine.Sensors.Set("claude.five_hour.resets_at", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
        engine.Sensors.Set("claude.updated_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.True(Wait.Until(() => SavedLimits().Contains("\"pct\": 17")), "The limits were not saved.");
    }

    /// The saved limits file, or "" while it doesn't exist or is being replaced.
    string SavedLimits()
    {
        try
        {
            return File.ReadAllText(_dir.File("claude-limits.json"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
