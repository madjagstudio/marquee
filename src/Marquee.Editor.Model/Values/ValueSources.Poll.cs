using Marquee.Core.Sensors;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Values;

/// The poll: the tray's values about once a second while the window is shown, its history when wanted, and the
/// editor's own readings while the tray has nothing to give.
public sealed partial class ValueSources
{
    /// How often the tray is asked for values.
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// How long the tray's last values stay on the canvas while it is busy or answers badly, before the editor's own
    /// readings take over. A tray that isn't running loses them at once.
    public static readonly TimeSpan TrayGrace = TimeSpan.FromSeconds(5);

    /// How long the tray must have answered before the editor stops reading sensors itself: a tray that answers now
    /// and then shouldn't open and close LibreHardwareMonitor over and over.
    public static readonly TimeSpan LocalLinger = TimeSpan.FromSeconds(30);

    // Under _gate: written by the poll, read by the snapshot. Tray is replaced whole, never changed.
    Tray? _tray;
    SensorFeedStatus? _trayStatus;
    DateTimeOffset? _okSince;
    // History is wanted while a theme or graph asked for it after the last history read began: a count each, so a graph
    // added during a read is still asked for after it.
    int _historyAsked = 1;
    int _historyRead;
    HashSet<string> _graphKeys = new(StringComparer.Ordinal);
    (SensorValue Value, bool Stale)?[] _fed = [];

    // The UI thread's.
    CancellationTokenSource? _stop;
    Task _loop = Task.CompletedTask;

    // The poll's last catalog, so a new unused sensor also refreshes an open picker.
    IReadOnlyList<string> _knownKeys = [];
    (SensorValue Value, bool Stale)?[] _knownReadings = [];
    long? _knownSecond;

    /// The bound on one values read. A read that finds no tray returns at once (SensorFeedClient), so this only bounds
    /// a busy or stuck one. A setting only for tests.
    internal TimeSpan ValuesTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// The bound on one history read, up to 8 MB.
    internal TimeSpan HistoryTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// What the last read of the tray's feed said; null before the first.
    public SensorFeedStatus? TrayStatus
    {
        get
        {
            lock (_gate) return _trayStatus;
        }
    }

    /// For the panel's "tray not running": no Marquee of this user serves the feed.
    public bool TrayNotRunning => TrayStatus == SensorFeedStatus.NotRunning;

    /// True while the poll runs.
    public bool IsActive => _stop is not null;

    /// Starts the poll, reading at once and then each PollInterval, or stops it, and the editor's own readings with it,
    /// while the window is minimised.
    public void SetActive(bool active)
    {
        if (_disposed || active == IsActive) return;
        if (active)
        {
            _stop = new CancellationTokenSource();
            var stop = _stop.Token;
            _loop = Task.Run(() => Loop(stop));
            return;
        }

        lock (_gate)
        {
            // Under the lock, so a poll finishing now can't start the local readings again after this stops them.
            _stop!.Cancel();
            _stop = null;
            _local.Stop();
        }
    }

    async Task Loop(CancellationToken stop)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval, _time);
            do
            {
                try
                {
                    await PollAsync(stop).ConfigureAwait(false);
                }
                catch (Exception) when (!stop.IsCancellationRequested)
                {
                    // A failed read or a source that wouldn't start; the next tick tries again.
                }
            }
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    /// One poll: the values, then the history if it is wanted and the tray answered; then which sources run.
    internal async Task PollAsync(CancellationToken cancel = default)
    {
        var values = await _feed.ReadValuesAsync(ValuesTimeout, cancel).ConfigureAwait(false);
        SensorFeedHistory? history = null;
        var asked = HistoryAsked;
        if (values.Message is not null && asked is not null)
            history = (await _feed.ReadHistoryAsync(HistoryTimeout, cancel).ConfigureAwait(false)).Message;

        var now = _time.GetUtcNow();
        var keys = TestValues.Keys;
        bool statusChanged;
        lock (_gate)
        {
            if (cancel.IsCancellationRequested) return;
            statusChanged = _trayStatus != values.Status;
            _trayStatus = values.Status;
            if (values.Message is { } message)
            {
                _okSince ??= now;
                _tray = Received(_tray, message, history, keys, now);
                if (history is not null) _historyRead = Math.Max(_historyRead, asked!.Value);
            }
            else
            {
                _okSince = null;
                if (_tray is not null && (values.Status == SensorFeedStatus.NotRunning || now - _tray.At >= TrayGrace))
                {
                    // Its history goes with it, so a tray that comes back is asked for its own.
                    _tray = null;
                    _historyAsked++;
                }
            }

            if (_tray is null) _local.Start();
            else if (now - _okSince >= LocalLinger) _local.Stop();
        }

        // A clock the theme shows moves on each second though no reading does; so does a graph fed by the tray, whose
        // history takes a point each poll, steady readings included.
        var ticks = _motion.Clock && TestValues.PinnedNow is null;
        var graphMoves = values.Message is not null && keys.Any(TestValues.IsGraphKey);
        var knownKeys = KnownKeys();
        var keysChanged = !knownKeys.SequenceEqual(_knownKeys);
        var view = View();
        var readings = knownKeys.Select(view.Fed).ToArray();
        var readingsChanged = !readings.SequenceEqual(_knownReadings);
        var second = view.Now.ToUnixTimeSeconds();
        var clockChanged = second != _knownSecond;
        _knownKeys = knownKeys;
        _knownReadings = readings;
        _knownSecond = second;
        if (NoteChanges(keys, now) || statusChanged || ticks || graphMoves || keysChanged || readingsChanged || clockChanged) RaiseUpdated();
    }

    /// The ask a history read would answer; null when none is waiting.
    int? HistoryAsked
    {
        get
        {
            lock (_gate) return _historyRead == _historyAsked ? null : _historyAsked;
        }
    }

    /// The tray's new values, with each theme key's history: the tray's own when it came with them, otherwise the last
    /// one with this value added. A stale or text value adds nothing.
    ///
    /// That is one point per poll, about a second, where the tray's store adds one per write: the same for the
    /// hardware keys, written each second, but a key written every 2 to 5 seconds (an agent's, `marquee push`) draws
    /// that many times faster here than on the screen until the next history read. The feed can't say whether a value
    /// was written again since the last poll, as FeedValue has no write time; carrying one (a version 2 of the feed's
    /// format, which is the tray's to change) would let a point be added only when it moves on.
    static Tray Received(Tray? last, SensorFeedValues message, SensorFeedHistory? history, IReadOnlyList<string> keys,
        DateTimeOffset now)
    {
        var kept = new Dictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (history is not null && history.History.TryGetValue(key, out var points))
                kept[key] = [.. points.Skip(Math.Max(0, points.Count - SensorStore.DefaultHistoryLength))];
            else if (message.Values.TryGetValue(key, out var value) && value is { Stale: false, Number: { } number })
                kept[key] = Append(last?.History.GetValueOrDefault(key) ?? [], number);
            else if (last?.History.GetValueOrDefault(key) is { } previous)
                kept[key] = previous;
        }

        return new Tray(message.Values, kept, now);
    }

    /// Copy-on-write, as the store's history is, so a snapshot can hold an array while the next poll makes another.
    static double[] Append(double[] history, double value)
    {
        var keep = Math.Min(history.Length, SensorStore.DefaultHistoryLength - 1);
        var next = new double[keep + 1];
        Array.Copy(history, history.Length - keep, next, 0, keep);
        next[keep] = value;
        return next;
    }

    /// True, and ChangedAt moved on, when a reading of a key the theme reads differs from the last poll's.
    bool NoteChanges(IReadOnlyList<string> keys, DateTimeOffset now)
    {
        var view = View();
        var fed = keys.Select(view.Fed).ToArray();
        lock (_gate)
        {
            if (fed.SequenceEqual(_fed)) return false;
            _fed = fed;
            _changedAt = now;
            return true;
        }
    }
}
