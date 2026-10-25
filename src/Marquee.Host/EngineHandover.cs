using System.Diagnostics;
using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Host.Resources;

namespace Marquee.Host;

/// The engine's side of the L-Connect handover: what a busy screen leads to under `lconnect.handover`, and
/// LConnectHandover run on a thread of its own, one at a time, waiting for the engine's render loop to open the screen.
/// It also keeps the screen signal that `marquee claim-screen` waits on.
///
/// Thread-safe. OnScreenBusy and OnScreenOpenChanged come from the render loop's sending thread and never block it; the
/// handover's wait for the screen needs that thread free, so the handover itself runs on its own thread. That thread is
/// a foreground one, so the process can't end between stopping L-Connect and starting it again.
sealed class EngineHandover : IDisposable
{
    /// How long Auto leaves a busy screen alone after a handover failed, before it tries again: 1 minute after the
    /// episode's first failure, 2 after its second, 5 after its third, then every 10. See LConnectHandover for why
    /// retries back off. The render loop reports a busy screen only once per episode, so a timer schedules retries.
    /// Opening the screen or pausing Marquee ends the episode; the next busy screen starts the schedule again.
    public static readonly IReadOnlyList<TimeSpan> RetrySchedule =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)];

    /// The first wait of the schedule, which is also how long a new busy episode is held back after a failure.
    public static TimeSpan RetryAfterFailure => RetrySchedule[0];

    readonly ILog _log;
    readonly IEngineServices _services;
    readonly Func<HandoverMode> _mode;
    readonly Action _retryScreen;
    readonly Action<ScreenBusyInfo> _busy;
    readonly Action<HandoverResult> _finished;
    readonly Func<TimeSpan> _elapsed;
    readonly Func<TimeSpan, Action, IDisposable> _schedule;
    readonly ScreenSignal? _signal;
    readonly ManualResetEventSlim _screenOpen = new(false);
    readonly object _gate = new();
    bool _wanted; // the engine is running and not paused, so its render loop is trying to open the screen
    Thread? _thread; // the handover under way, or the last one
    CancellationTokenSource? _cancel; // the wait for the screen of the handover under way
    TimeSpan? _failedAt;
    int _failures; // the handovers that failed in this busy episode, which set Auto's wait (RetrySchedule)
    IDisposable? _retry; // Auto's next try after a failure, while one is due
    int _retryGeneration; // which retry is due: each one scheduled or cancelled moves it on
    bool _toldUnelevated;
    bool _disposed;

    /// `mode` reads `lconnect.handover` as it is at that moment (the engine's settings), since Auto's retry decides
    /// again minutes after the busy screen was reported. `retryScreen` asks the render loop and the ring to try their
    /// devices at once (EngineRun.RetryScreen). `busy` and `finished` raise the engine's events; they are called on the
    /// render loop's thread and the handover's, and must not block. `elapsed` is a monotonic clock and `schedule` a
    /// one-shot timer (it runs the action once after the delay, unless what it returns is disposed first), for tests.
    public EngineHandover(ILog log, IEngineServices services, Func<HandoverMode> mode, Action retryScreen,
        Action<ScreenBusyInfo> busy, Action<HandoverResult> finished, Func<TimeSpan>? elapsed = null,
        Func<TimeSpan, Action, IDisposable>? schedule = null)
    {
        _log = log;
        _services = services;
        _mode = mode;
        _retryScreen = retryScreen;
        _busy = busy;
        _finished = finished;
        var clock = Stopwatch.StartNew();
        _elapsed = elapsed ?? (() => clock.Elapsed);
        _schedule = schedule ?? ((delay, action) => new Timer(_ => action(), null, delay, Timeout.InfiniteTimeSpan));
        _signal = ScreenSignal.Create(services.ScreenSignalName);
    }

    /// Whether a handover is under way.
    public bool IsRunning
    {
        get
        {
            lock (_gate) return _cancel is not null;
        }
    }

    /// Whether the engine wants the screen: from just before a run starts until it pauses or stops. While it doesn't,
    /// no handover starts; one under way stops waiting for the screen, which won't open now, and starts L-Connect again
    /// at once; and Auto's retry is called off, since a run that starts again reports a busy screen afresh.
    public void SetScreenWanted(bool wanted)
    {
        lock (_gate)
        {
            _wanted = wanted;
            if (wanted) return;
            _cancel?.Cancel();
            CancelRetry();
        }
    }

    /// The render loop found the screen busy, once per busy episode. Tells the host what the mode says and whether the
    /// screen could be taken; with Auto, takes it, unless a handover is under way or the last one failed less than
    /// RetryAfterFailure ago (Auto then tries again once that time is up). A new episode starts Auto's retry schedule
    /// again.
    public void OnScreenBusy(LConnectState state)
    {
        lock (_gate)
        {
            if (!_wanted) return;
            _failures = 0;
        }

        var elevated = _services.IsElevated;
        var action = HandoverPolicy.Decide(_mode(), state, userAsked: false);
        if (action != HandoverAction.None && !elevated)
        {
            action = HandoverAction.None;
            TellUnelevatedOnce();
        }

        _busy(new ScreenBusyInfo(state, action, CanTakeOver: state == LConnectState.Service && elevated));
        if (action == HandoverAction.TakeOver) Start(automatic: true);
    }

    /// The engine's screen opened or closed. Called by the thread that opened or closed it.
    public void OnScreenOpenChanged(bool open)
    {
        if (open)
        {
            _screenOpen.Set();
            _signal?.Set();
        }
        else
        {
            _screenOpen.Reset();
            _signal?.Reset();
        }
    }

    /// The tray's "Take over": starts a handover whatever the mode says. False when it doesn't start: one is under way
    /// already, the engine doesn't want the screen (stopped or paused), Marquee has the screen already (a balloon
    /// clicked long after it showed), or Marquee isn't elevated (those two are logged).
    public bool TakeOver()
    {
        if (!_services.IsElevated)
        {
            _log.Warn(Strings.TakeOverNeedsAdministrator);
            return false;
        }

        return Start(automatic: false);
    }

    /// Waits until a handover under way has ended, and with it started L-Connect again.
    public void WaitForEnd()
    {
        Thread? thread;
        lock (_gate) thread = _thread;
        thread?.Join();
    }

    /// After WaitForEnd, once nothing opens or closes the screen any more.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            CancelRetry();
        }

        _signal?.Dispose();
        _screenOpen.Dispose();
    }

    /// `retry` is Auto's own retry, which comes once its wait (RetryDelay) is up, so it isn't held back.
    bool Start(bool automatic, bool retry = false)
    {
        lock (_gate)
        {
            if (_disposed || !_wanted || _cancel is not null) return false;
            if (_screenOpen.IsSet)
            {
                // A busy report or a "Take over" that came after the screen opened: nothing to take.
                _log.Info(Strings.HandoverScreenOpen);
                return false;
            }

            if (automatic && !retry && _failedAt is { } failedAt && _elapsed() - failedAt is var since && since < RetryDelay)
            {
                _log.Info(Strings.HandoverHeldBack((int)RetryDelay.TotalSeconds));
                // The retry after that failure went with a pause, or is still due: either way, this busy screen is
                // tried again once the time is up.
                ScheduleRetry(RetryDelay - since);
                return false;
            }

            if (retry) _log.Info(Strings.HandoverRetry);
            else if (automatic) _log.Info(Strings.HandoverAuto);

            CancelRetry();
            var cancel = new CancellationTokenSource();
            _cancel = cancel;
            _thread = new Thread(() => Run(cancel)) { IsBackground = false, Name = "Marquee L-Connect handover" };
            _thread.Start();
            return true;
        }
    }

    void Run(CancellationTokenSource cancel)
    {
        HandoverResult result;
        try
        {
            result = new LConnectHandover(_services.CreateServiceControl(), _log)
                .TakeScreen(timeout => WaitForScreen(timeout, cancel.Token), cancel.Token);
        }
        catch (Exception ex)
        {
            // TakeScreen reports its own errors, so this is getting the service control; nothing was stopped. An
            // exception must not end the process from this thread.
            var message = Strings.HandoverFailed(ex.Message);
            _log.Error(message);
            result = new HandoverResult(HandoverOutcome.Failed, message, [], []);
        }

        lock (_gate)
        {
            // A cancelled handover didn't fail: Marquee paused or is quitting, and holding Auto back after a Resume
            // would leave the screen to L-Connect for nothing. A failure is tried again later, whatever the mode is
            // now: Retry checks it then.
            if (result.Outcome == HandoverOutcome.Failed)
            {
                _failedAt = _elapsed();
                _failures++;
                if (_wanted && !_disposed) ScheduleRetry(RetryDelay);
            }
            else if (result.Outcome == HandoverOutcome.Taken)
            {
                _failedAt = null; // the last one didn't fail, so the next busy screen needn't wait
                _failures = 0;
            }

            _cancel = null;
            cancel.Dispose();
        }

        // After the handover counts as ended, so a handler may start another.
        _finished(result);
    }

    /// On the timer's thread, RetryDelay after a handover failed (or the rest of it, after a busy screen was
    /// held back): takes the screen if it is still L-Connect's service's, and `lconnect.handover` still says auto. An
    /// exception would end the process from here. `generation` is the retry's own: disposing a timer doesn't stop a
    /// callback that has already started, so one that waited for the lock while a pause cancelled it (and perhaps a
    /// held-back busy screen scheduled the next) finds it is no longer the retry due, and leaves that one alone.
    void Retry(int generation)
    {
        try
        {
            lock (_gate)
            {
                if (generation != _retryGeneration) return;
                CancelRetry();
                if (_disposed || !_wanted || _cancel is not null || _screenOpen.IsSet) return;
            }

            if (_mode() != HandoverMode.Auto) return;
            if (_services.LConnect.Detect() != LConnectState.Service) return;
            Start(automatic: true, retry: true);
        }
        catch (Exception ex)
        {
            _log.Error(Strings.HandoverFailed(ex.Message));
        }
    }

    /// Under _gate. How long Auto waits after the last failure: RetrySchedule's step for the failures in this episode
    /// (its first, RetryAfterFailure, before the episode has one of its own).
    TimeSpan RetryDelay => RetrySchedule[Math.Clamp(_failures - 1, 0, RetrySchedule.Count - 1)];

    /// Under _gate. Schedules Auto's retry `delay` from now, unless one is due already.
    void ScheduleRetry(TimeSpan delay)
    {
        if (_retry is not null) return;
        var generation = ++_retryGeneration;
        _retry = _schedule(delay, () => Retry(generation));
    }

    /// Under _gate.
    void CancelRetry()
    {
        _retryGeneration++;
        _retry?.Dispose();
        _retry = null;
    }

    /// L-Connect's service has stopped: has the render loop try the screen at once, then waits for it to open.
    bool WaitForScreen(TimeSpan timeout, CancellationToken cancel)
    {
        _retryScreen();
        try
        {
            return _screenOpen.Wait(timeout, cancel);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// Unelevated, the handover can't run; said once per engine, not at every busy screen, as the FPS warning is.
    void TellUnelevatedOnce()
    {
        lock (_gate)
        {
            if (_toldUnelevated) return;
            _toldUnelevated = true;
        }

        _log.Warn(Strings.HandoverNeedsAdministrator);
    }
}
