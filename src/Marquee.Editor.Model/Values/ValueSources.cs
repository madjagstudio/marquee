using Marquee.Core;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Host;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Values;

/// Where the canvas's values come from. Each key the theme reads takes, highest first: its pin in the test values
/// panel, the tray's feed, what the editor reads itself, or a sample. The snapshot the session draws with (Attach) is
/// made from them, frame by frame, with the theme's keys only; `now` is the snapshot's clock, pinned or the PC's.
///
/// While active (SetActive), a poll asks the tray for values about once a second, off the UI thread
/// (ValueSources.Poll.cs), and its history when a theme opens or gains a graph; after that it keeps each key's history
/// from the values it receives, as the tray's store does. With no tray the editor reads sensors itself, and the panel
/// says "tray not running". Updated says when a poll changed a value the theme reads, the known keys, or its clock,
/// so the canvas can redraw; Animates says when it should draw at the theme's fps instead.
///
/// Snapshot, Rows and the test values are safe from any thread. Attach, SetTheme, SetActive and Dispose belong to the
/// UI thread, as the session does.
public sealed partial class ValueSources : IDisposable
{
    readonly ITrayFeed _feed;
    readonly ILocalReadings _local;
    readonly TimeProvider _time;
    readonly SynchronizationContext? _context;
    readonly object _gate = new();
    ThemeMotion _motion = ThemeMotion.None;
    EditorSession? _session;
    DateTimeOffset _changedAt;
    bool _disposed;

    /// Owns `local`, and disposes it. Inactive until SetActive(true).
    public ValueSources(ITrayFeed feed, ILocalReadings local, TimeProvider? time = null)
    {
        _feed = feed;
        _local = local;
        _time = time ?? TimeProvider.System;
        // Updated is raised on the thread that made the sources, the UI thread, when it has a SynchronizationContext.
        _context = SynchronizationContext.Current;
        _changedAt = _time.GetUtcNow();
        TestValues.Changed += OnTestValuesChanged;
    }

    /// The most Dispose waits for the poll and the local readings to stop.
    public static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(1);

    /// How long Dispose waits: CloseWait. Tests set it, so the bound, not how quickly the machine runs, decides.
    internal TimeSpan DisposeWait { get; set; } = CloseWait;

    /// The real sources: this user's tray, and the sensors of this PC that config.yaml (MarqueePaths.ConfigFile) lets
    /// the tray read.
    public static ValueSources ForThisUser(ILog? log = null) => ForThisUser(MarqueePaths.ConfigFile, log);

    /// The real sources, with config.yaml read from `configFile` when they are made: one that is missing or broken is
    /// the defaults (ConfigFile.Load, which logs a broken one), as for the tray.
    public static ValueSources ForThisUser(string configFile, ILog? log = null)
    {
        log ??= NullLog.Instance;
        return new(new TrayFeed(), LocalReadings.ForThisPc(ConfigFile.Load(configFile, log), log));
    }

    /// The editor's own readings, for tests.
    internal ILocalReadings Local => _local;

    /// The panel's list, pins and slider ranges.
    public TestValues TestValues { get; } = new();

    /// What moves with time in the theme set last.
    public ThemeMotion Motion => _motion;

    /// When a value the theme reads last changed: a reading, a pin, or the theme itself. Smoothing moves for a while
    /// after.
    public DateTimeOffset ChangedAt
    {
        get
        {
            lock (_gate) return _changedAt;
        }
    }

    /// True while the canvas should be drawn at the theme's fps: a video or a shader plays, a widget pulses with the
    /// values now, or smoothing is still easing toward a value that changed. Otherwise a frame per change, and one a
    /// second for new values, is enough.
    public bool Animates
    {
        get
        {
            var motion = _motion;
            if (motion.Video || motion.Shader) return true;
            if (motion.Smoothing > TimeSpan.Zero && _time.GetUtcNow() - ChangedAt < motion.Smoothing) return true;
            return motion.Pulses(Snapshot());
        }
    }

    /// Raised when a poll changed a value the theme reads, the known keys, or what the tray said, at every poll while
    /// the theme reads an unpinned `now` (a clock moves on each second), and when the test values change.
    public event EventHandler? Updated;

    /// Makes `session` draw with these sources and keeps the list of keys following its theme, until another session is
    /// attached or these are disposed.
    public void Attach(EditorSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Detach();
        _session = session;
        session.Values = Snapshot;
        session.Changed += OnSessionChanged;
        SetTheme(session.Theme?.Spec);
    }

    /// Reads the keys, ranges, graphs and motion of the theme now shown; null for none. A graph on a key that had none
    /// asks the tray for its history at the next poll.
    public void SetTheme(ThemeSpec? spec)
    {
        _motion = ThemeMotion.Of(spec);
        TestValues.SetTheme(spec);
        var graphs = TestValues.Keys.Where(TestValues.IsGraphKey).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            if (!graphs.IsSubsetOf(_graphKeys)) _historyAsked++;
            _graphKeys = graphs;
        }
    }

    /// The values the canvas draws now: every key the theme reads, from its highest source.
    public SensorSnapshot Snapshot()
    {
        var view = View();
        var entries = new Dictionary<string, SensorEntry>(StringComparer.Ordinal);
        foreach (var key in view.Keys)
            if (key != TestValues.Now && view.Resolve(key).Entry is { } entry)
                entries[key] = entry;
        return SensorSnapshot.Of(view.Now, entries);
    }

    /// Where the key's value comes from now.
    public ValueSourceKind SourceOf(string key) => View().Resolve(key).Source;

    /// All known keys, including readings that the current theme does not use.
    public IReadOnlyList<string> KnownKeys()
    {
        Tray? tray;
        lock (_gate) tray = _tray;
        return TestValues.Keys.Concat(tray?.Values.Keys ?? []).Concat(_local.Snapshot().Keys)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    /// The panel's lines, one per key in TestValues.Keys.
    public IReadOnlyList<ValueRow> Rows()
    {
        var view = View();
        return view.Keys.Select(key =>
        {
            var (entry, source) = view.Resolve(key);
            var pin = key == TestValues.Now ? null : TestValues.PinOf(key);
            return new ValueRow(key, source, entry?.Value, entry?.ExpiresAt <= view.Now, pin, TestValues.RangeOf(key),
                TestValues.IsGraphKey(key));
        }).ToList();
    }

    /// Stops the poll and the local readings, and lets go of the session. It waits for them at most CloseWait in all:
    /// a poll under way ends within its read's timeout (up to HistoryTimeout), and the local readings within their
    /// providers' stop (up to 3 s each, for one stuck in a driver call), which could hold a closing window for
    /// seconds. The readings stop on a thread of their own, started at once, so a busy thread pool can't hold them up.
    /// Past CloseWait both finish stopping by themselves, where nothing waits on them; the poll is cancelled and the
    /// readings can't start again.
    public void Dispose()
    {
        if (_disposed) return;
        SetActive(false);
        _disposed = true;
        Detach();
        TestValues.Changed -= OnTestValuesChanged;
        var local = new Thread(StopLocal) { IsBackground = true, Name = "Marquee editor readings stop" };
        local.Start();
        var waited = System.Diagnostics.Stopwatch.StartNew();
        // WaitAny, not Wait: it doesn't throw for a poll that faulted, which a closing window has no use for.
        Task.WaitAny([_loop], DisposeWait);
        local.Join(Remaining(DisposeWait, waited.Elapsed));
    }

    /// What is left of `wait` after `spent`; infinite stays infinite.
    static TimeSpan Remaining(TimeSpan wait, TimeSpan spent) =>
        wait == Timeout.InfiniteTimeSpan ? wait : spent >= wait ? TimeSpan.Zero : wait - spent;

    /// On a thread of its own, where an exception would end the process: a stop that fails is let go.
    void StopLocal()
    {
        try
        {
            _local.Dispose();
        }
        catch (Exception)
        {
            // The readings are stopping because the window closes; one that fails to stop goes with the process.
        }
    }

    ValueView View()
    {
        Tray? tray;
        lock (_gate) tray = _tray;
        var pinnedNow = TestValues.PinnedNow;
        return new ValueView(TestValues, tray, _local.Snapshot(), TestValues.Pins, pinnedNow ?? _time.GetUtcNow(),
            pinnedNow is not null);
    }

    void Detach()
    {
        if (_session is null) return;
        _session.Changed -= OnSessionChanged;
        _session = null;
    }

    void OnSessionChanged(object? sender, SessionChanges changes)
    {
        if (changes.HasFlag(SessionChanges.Theme)) SetTheme(_session?.Theme?.Spec);
    }

    void OnTestValuesChanged(object? sender, EventArgs e)
    {
        lock (_gate) _changedAt = _time.GetUtcNow();
        RaiseUpdated();
    }

    void RaiseUpdated()
    {
        if (_context is null) Updated?.Invoke(this, EventArgs.Empty);
        else _context.Post(_ => Updated?.Invoke(this, EventArgs.Empty), null);
    }
}
