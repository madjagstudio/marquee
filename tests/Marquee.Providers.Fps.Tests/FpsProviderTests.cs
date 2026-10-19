using System.Globalization;
using System.Security.Cryptography;
using Marquee.Core.Sensors;
using Marquee.Providers.Fps;
using Marquee.Providers.Fps.Tests.TestSupport;

namespace Marquee.Providers.Fps.Tests;

/// FpsProvider with fakes all round: a PresentMon that is CSV text the test writes, a foreground window the test picks,
/// process names from a table, a clock the test moves, and ETW sessions and running processes from lists. The exe is a
/// small temp file whose hash the test works out.
public class FpsProviderTests : IDisposable
{
    const int Marquee = 100;
    const int Game = 4242;
    const int OtherGame = 5151;
    const int Dwm = 1200;
    const int Editor = 3000;
    const string Header = "Application,ProcessID,SwapChainAddress,CPUStartTime,MsBetweenPresents";

    static readonly Dictionary<int, string> Names = new()
    {
        [Marquee] = "marquee", [Game] = "game", [OtherGame] = "other", [Dwm] = "dwm", [Editor] = "notepad",
    };

    readonly TempDir _dir = new();
    readonly string _exe;
    readonly string _sha256;
    readonly List<FakePresentMon> _runs = [];
    readonly List<(string Exe, IReadOnlyList<string> Arguments)> _launches = [];
    readonly SensorStore _store = new();
    readonly FakeEtwSessions _sessions = new();
    readonly HashSet<int> _runningProcesses = [Marquee, Game, OtherGame, Dwm, Editor];
    int? _foreground = Game;
    TimeSpan _now = TimeSpan.FromMinutes(5);

    public FpsProviderTests()
    {
        _exe = _dir.Write(@"PresentMon\PresentMon-test.exe", "not really PresentMon");
        _sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_exe))).ToLowerInvariant();
    }

    public void Dispose()
    {
        foreach (var run in _runs) run.Dispose();
        _dir.Dispose();
    }

    FakePresentMon Run => _runs[^1];

    FpsProvider Provider(string? exe = null, string? sha256 = null, int ownProcessId = Marquee) => new(
        exe ?? _exe, sha256 ?? _sha256, Launch, () => _foreground,
        id => Names.TryGetValue(id, out var name) ? name : null, () => _now, ownProcessId,
        _sessions, _runningProcesses.Contains);

    IPresentMonRun Launch(string exe, IReadOnlyList<string> arguments)
    {
        _launches.Add((exe, arguments));
        var run = new FakePresentMon();
        _runs.Add(run);
        return run;
    }

    /// One CSV row per frame at a steady rate, starting one interval after fromMs on PresentMon's own clock.
    static IEnumerable<string> Frames(int pid, double fps, double seconds, double fromMs = 0, string application = "game.exe")
    {
        var interval = 1000 / fps;
        var count = (int)Math.Round(seconds * fps);
        for (var i = 1; i <= count; i++)
            yield return string.Create(CultureInfo.InvariantCulture, $"{application},{pid},0x10,{fromMs + i * interval:0.####},{interval:0.####}");
    }

    /// Writes rows into the running PresentMon, all read at the current time, and waits until they are in.
    void Feed(IEnumerable<string> lines)
    {
        Run.Write(lines);
        Assert.True(Run.WaitUntilRead(), "The provider did not read PresentMon's output.");
    }

    /// Starts PresentMon with a first poll and feeds it the header.
    FpsProvider Started()
    {
        var provider = Provider();
        provider.Poll(_store);
        Feed([Header]);
        return provider;
    }

    object? Read(string key) =>
        _store.Snapshot().TryGet(key, out var value) ? value.Number is { } n ? n : value.Text : null;

    void AssertNothingWritten()
    {
        foreach (var key in new[] { "fps", "frametime", "fps.1pct_low", "fps.app" })
            Assert.Null(Read(key));
    }

    // ---- What it writes ----

    [Fact]
    public void Writes_the_foreground_games_fps_frame_time_1pct_low_and_name()
    {
        using var provider = Started();
        Feed(Frames(Game, fps: 60, seconds: 3));

        provider.Poll(_store);

        Assert.Equal(60, (double)Read("fps")!, 1e-2);
        Assert.Equal(1000.0 / 60, (double)Read("frametime")!, 1e-2);
        Assert.Equal(60, (double)Read("fps.1pct_low")!, 1e-2);
        Assert.Equal("game", Read("fps.app"));
    }

    [Fact]
    public void Its_name_and_interval_follow_the_design()
    {
        using var provider = Provider();

        Assert.Equal(("FPS", TimeSpan.FromMilliseconds(250)), (provider.Name, provider.Interval));
    }

    [Fact]
    public void Starts_PresentMon_on_the_first_poll_in_a_session_named_for_this_process()
    {
        using var provider = Provider();
        Assert.Empty(_launches);

        provider.Poll(_store);

        var (exe, arguments) = Assert.Single(_launches);
        Assert.Equal(_exe, exe);
        Assert.Equal(["--output_stdout", "--no_console_stats", "--session_name", "Marquee-100", "--stop_existing_session"], arguments);
        provider.Poll(_store);
        Assert.Single(_launches);
    }

    // ---- ETW sessions ----

    /// The tray and a `marquee run` in an admin terminal both measure FPS. With one session name between them, each
    /// PresentMon's --stop_existing_session would stop the other's, over and over.
    [Fact]
    public void Each_Marquee_process_records_in_an_ETW_session_of_its_own()
    {
        using var tray = Provider(ownProcessId: 100);
        using var cli = Provider(ownProcessId: 200);

        tray.Poll(_store);
        cli.Poll(_store);

        Assert.Equal(["Marquee-100", "Marquee-200"], _launches.Select(l => l.Arguments[3]));
    }

    /// A Marquee that crashed, or was ended from Task Manager, leaves its session behind (an ETW session outlives the
    /// process that started it), and nothing else would ever stop it before a reboot.
    [Fact]
    public void Starting_stops_the_sessions_of_Marquee_processes_that_have_gone()
    {
        _runningProcesses.Add(300); // another Marquee, still running; 301 and 302 have gone
        _sessions.Running.AddRange(
        [
            "NT Kernel Logger", "Marquee-301", "Marquee-300", "Marquee-100", "Marquee", "Marquee-", "Marquee-x1",
            "Marquee-+1", "Marquee- 1", "Marquee-1.5", "Other-302", "Marquee-302",
        ]);
        var launchesAtEachStop = new List<int>();
        _sessions.OnStop = _ => launchesAtEachStop.Add(_launches.Count);

        using var provider = Provider();
        provider.Poll(_store);

        Assert.Equal(["Marquee-301", "Marquee-302"], _sessions.Stopped);
        Assert.Equal([0, 0], launchesAtEachStop); // before PresentMon started
        Assert.Single(_launches);
    }

    [Fact]
    public void Sessions_that_cant_be_listed_dont_keep_PresentMon_from_starting()
    {
        _sessions.ListFails = true;
        using var provider = Provider();

        provider.Poll(_store);

        Assert.Single(_launches);
    }

    [Fact]
    public void A_session_that_cant_be_stopped_doesnt_keep_the_others_or_PresentMon_from_stopping_or_starting()
    {
        _sessions.Running.AddRange(["Marquee-301", "Marquee-302"]);
        _sessions.StopFails.Add("Marquee-301");
        using var provider = Provider();

        provider.Poll(_store);

        Assert.Equal(["Marquee-302"], _sessions.Stopped);
        Assert.Single(_launches);
    }

    /// Killing PresentMon leaves its session running, so the provider stops it once PresentMon has gone.
    [Fact]
    public void Dispose_stops_PresentMon_then_its_session()
    {
        var provider = Started();
        _sessions.OnStop = _ => Assert.True(Run.Disposed);

        provider.Dispose();

        Assert.Equal(["Marquee-100"], _sessions.Stopped);
    }

    [Fact]
    public void Dispose_before_PresentMon_started_stops_no_session()
    {
        var provider = Provider();

        provider.Dispose();

        Assert.Empty(_sessions.Stopped);
    }

    [Fact]
    public void A_session_that_cant_be_stopped_on_dispose_is_left()
    {
        var provider = Started();
        _sessions.StopFails.Add("Marquee-100");

        provider.Dispose();

        Assert.True(Run.Disposed);
    }

    [Theory]
    [InlineData(Marquee)]
    [InlineData(Dwm)]
    public void Ignores_Marquee_itself_and_dwm(int foreground)
    {
        using var provider = Started();
        Feed(Frames(foreground, fps: 60, seconds: 2, application: Names[foreground] + ".exe"));
        _foreground = foreground;

        provider.Poll(_store);

        AssertNothingWritten();
    }

    [Fact]
    public void Writes_nothing_when_the_foreground_process_has_no_frames()
    {
        using var provider = Started();
        Feed(Frames(Game, fps: 60, seconds: 2));
        _foreground = Editor;

        provider.Poll(_store);

        AssertNothingWritten();
    }

    [Fact]
    public void Writes_nothing_without_a_foreground_window()
    {
        using var provider = Started();
        Feed(Frames(Game, fps: 60, seconds: 2));
        _foreground = null;

        provider.Poll(_store);

        AssertNothingWritten();
    }

    [Fact]
    public void Writes_nothing_once_the_last_frame_arrived_more_than_two_seconds_ago()
    {
        using var provider = Started();
        Feed(Frames(Game, fps: 60, seconds: 2));
        _now += TimeSpan.FromSeconds(2.1);

        provider.Poll(_store);

        AssertNothingWritten();
    }

    [Fact]
    public void The_keys_go_stale_two_seconds_after_the_last_frame_arrived()
    {
        var storeNow = DateTimeOffset.UnixEpoch;
        var store = new SensorStore(() => storeNow);
        using var provider = Provider();
        provider.Poll(store);
        Feed([Header, .. Frames(Game, fps: 60, seconds: 2)]);
        _now += TimeSpan.FromSeconds(0.5); // the poll comes half a second after the frames

        provider.Poll(store);

        storeNow += TimeSpan.FromSeconds(1.4);
        Assert.True(store.Snapshot().TryGet("fps", out _));
        Assert.True(store.Snapshot().TryGet("fps.app", out _));
        storeNow += TimeSpan.FromSeconds(0.2);
        Assert.False(store.Snapshot().TryGet("fps", out _));
        Assert.False(store.Snapshot().TryGet("fps.app", out _));
    }

    [Fact]
    public void Switching_the_foreground_switches_the_app()
    {
        using var provider = Started();
        Feed([.. Frames(Game, fps: 60, seconds: 2), .. Frames(OtherGame, fps: 144, seconds: 2, application: "other.exe")]);
        provider.Poll(_store);
        Assert.Equal("game", Read("fps.app"));

        _foreground = OtherGame;
        provider.Poll(_store);

        Assert.Equal("other", Read("fps.app"));
        Assert.Equal(144, (double)Read("fps")!, 1e-2);
    }

    // ---- PresentMon stopping and starting ----

    [Fact]
    public void PresentMon_exiting_fails_the_poll_with_its_exit_code_and_last_error()
    {
        using var provider = Started();
        Run.Exit(5, "Error: failed to start the trace session");

        var error = Assert.Throws<InvalidOperationException>(() => provider.Poll(_store));

        Assert.Contains("PresentMon exited with code 5", error.Message);
        Assert.Contains("Error: failed to start the trace session", error.Message);
        Assert.True(_runs[0].Disposed);
    }

    [Fact]
    public void The_poll_after_an_exit_restarts_PresentMon_with_fresh_frame_stats()
    {
        using var provider = Started();
        // A long first run: its start times are 100 seconds in.
        Feed(Frames(Game, fps: 60, seconds: 2, fromMs: 100_000));
        Run.Exit(1);
        Assert.Throws<InvalidOperationException>(() => provider.Poll(_store));

        _now += TimeSpan.FromSeconds(1);
        provider.Poll(_store);
        Assert.Equal(2, _runs.Count);
        // The new run's clock starts at 0 again. Mixed with the old run's frames, these would be the oldest frames by
        // start time and the reading would still be the old 60 fps.
        Feed([Header, .. Frames(Game, fps: 30, seconds: 2)]);
        provider.Poll(_store);

        Assert.Equal(30, (double)Read("fps")!, 1e-2);
    }

    [Fact]
    public void An_unreadable_header_fails_the_poll()
    {
        using var provider = Provider();
        provider.Poll(_store);
        Run.Write("Application,SwapChainAddress,MsBetweenPresents");

        // The reader stops at the header, so there is no "all read" to wait for: poll until the poll fails.
        InvalidOperationException? error = null;
        Assert.True(Wait.Until(() =>
        {
            try
            {
                provider.Poll(_store);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                error = ex;
                return true;
            }
        }), "The poll never failed.");

        Assert.Contains("ProcessID", error!.Message);
        Assert.True(_runs[0].Disposed);
        Assert.Single(_runs);
    }

    [Fact]
    public void Dispose_stops_PresentMon()
    {
        var provider = Started();

        provider.Dispose();

        Assert.True(Run.Disposed);
    }

    // ---- The exe ----

    [Fact]
    public void A_missing_exe_is_a_clear_error_and_nothing_starts()
    {
        var missing = _dir.File(@"PresentMon\missing.exe");
        using var provider = Provider(exe: missing);

        var error = Assert.Throws<FileNotFoundException>(() => provider.Poll(_store));

        Assert.Contains(missing, error.Message);
        Assert.Empty(_launches);
    }

    [Fact]
    public void An_exe_with_the_wrong_hash_is_a_clear_error_and_never_starts()
    {
        using var provider = Provider(sha256: new string('0', 64));

        var error = Assert.Throws<InvalidOperationException>(() => provider.Poll(_store));
        Assert.Throws<InvalidOperationException>(() => provider.Poll(_store));

        Assert.Contains(_exe, error.Message);
        Assert.Contains(_sha256, error.Message);
        Assert.Contains("not the PresentMon Marquee was built with", error.Message);
        Assert.Empty(_launches);
    }

    [Fact]
    public void The_hash_is_checked_again_before_a_restart()
    {
        using var provider = Started();
        Run.Exit(1);
        Assert.Throws<InvalidOperationException>(() => provider.Poll(_store));
        File.WriteAllText(_exe, "something else");

        var error = Assert.Throws<InvalidOperationException>(() => provider.Poll(_store));

        Assert.Contains("not the PresentMon Marquee was built with", error.Message);
        Assert.Single(_launches);
    }
}
