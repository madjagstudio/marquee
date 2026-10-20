using System.Diagnostics;
using Marquee.Core.Devices.LedRing;
using Marquee.Core.Logging;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Host.Resources;
using SkiaSharp;

namespace Marquee.Host;

/// Lights the LED ring from a thread of its own, so the ring never delays a screen frame. Each tick renders Spec with
/// the latest sensor values and sends it, at most 30 times a second: the ring's effects look smooth at that rate, and
/// a frame is only three 64-byte packets. The ring keeps its last colors, so a frame identical to the last one sent
/// goes again only once a second: a static ring costs one frame a second rather than thirty, and a ring that lost its
/// colors without an error (another program wrote to it, say) is put right within a second.
///
/// `find` is asked for the ring at the first tick and again after every failure (a ring plugged back in may have a
/// new device path); it returns null while no ring is plugged in. A ring that fails is let go and tried again after
/// 2 s, as the screen is; a missing one is looked for every 5 s. Each problem is logged once, and so is the recovery.
/// Like the screen, the ring is single-threaded: only the sending thread touches it while that runs, and Pause and
/// Dispose only once it has ended.
///
/// Spec may be set from any thread. Start, Pause and Dispose are for one thread at a time (the engine calls them under
/// its lock).
public sealed class RingSender(Func<ILedRing?> find, SensorStore sensors, ILog log) : IDisposable
{
    /// 30 frames a second.
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1.0 / 30);

    /// After the ring failed, as for the screen.
    public static readonly TimeSpan ReconnectDelay = RenderLoop.ReconnectDelay;

    /// While no ring is plugged in. Many screens may never have one, so this is slower than ReconnectDelay.
    public static readonly TimeSpan LookAgainDelay = TimeSpan.FromSeconds(5);

    /// How often a frame identical to the last one is sent again.
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(1);

    static readonly SKColor[] Black = new SKColor[RingRenderer.LedCount];

    readonly Stopwatch _clock = Stopwatch.StartNew();
    volatile RingSpec? _spec;

    // The sending thread's, or the caller's of Tick, Pause and Dispose while that thread isn't running.
    readonly SKColor[] _frame = new SKColor[RingRenderer.LedCount];
    readonly SKColor[] _sent = new SKColor[RingRenderer.LedCount];
    ILedRing? _ring; // open, or null
    TimeSpan _nextAttempt;
    TimeSpan? _sentAt; // when _sent went out, or null when nothing has been sent since the ring opened
    bool _dark; // the ring was sent black and let go, so Off has nothing left to do
    string? _problem; // the device problem last logged, until the ring works again
    string? _frameError;
    int _retryNow; // 1 when RetryNow asked for the next attempt to open the ring to come at once

    Thread? _thread;
    CancellationTokenSource? _stop;
    bool _disposed;

    /// What the ring shows from the next tick, or null to keep it dark (`ring: off`): the ring is sent black once, so
    /// a ring an earlier run left lit goes dark too, and then let go.
    public RingSpec? Spec
    {
        get => _spec;
        set => _spec = value;
    }

    /// Starts the sending thread; the ring is found and opened at its first tick. After Pause, lights the ring again.
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null) return;
        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "Marquee LED ring" };
        _thread.Start();
    }

    /// Ends the sending thread, then sends black and lets the ring go: the ring is dark while Marquee is paused, and
    /// free for another program. Start lights it again. Repeating it changes nothing, though while no ring is open and
    /// none is known to be dark (none plugged in), each Pause looks for one once more to send it black.
    public void Pause()
    {
        if (_thread is not null)
        {
            _stop!.Cancel();
            _thread.Join();
            _thread = null;
            _stop.Dispose();
            _stop = null;
        }

        Darken();
    }

    /// Makes the next attempt to find and open the ring come with the next tick instead of after the rest of
    /// ReconnectDelay or LookAgainDelay, as RenderLoop.RetryNow does for the screen: the L-Connect handover asks for both
    /// once L-Connect's service has let go of them. Thread-safe.
    public void RetryNow() => Volatile.Write(ref _retryNow, 1);

    /// Pause, for good: the ring is left dark when Marquee quits.
    public void Dispose()
    {
        if (_disposed) return;
        Pause();
        _disposed = true;
    }

    /// One tick at `now` on the sender's clock, which also times the effects. The thread calls it at Interval; tests
    /// call it directly with made-up times.
    internal void Tick(TimeSpan now)
    {
        if (_spec is not { } spec)
        {
            if (!_dark && Send(Black, now)) Release(dark: true);
            return;
        }

        _dark = false;
        RingRenderer.Render(spec, sensors.Snapshot(), now, _frame);
        if (_sentAt is { } sentAt && now - sentAt < RefreshEvery && _frame.AsSpan().SequenceEqual(_sent)) return;
        if (!Send(_frame, now)) return;
        _frame.CopyTo(_sent, 0);
        _sentAt = now;
    }

    void Run(CancellationToken token)
    {
        var next = _clock.Elapsed;
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick(_clock.Elapsed);
            }
            catch (Exception ex)
            {
                // Send handles the ring's own errors, so this is rendering. An exception left to end the thread would
                // end the process; logged once, not 30 times a second.
                var message = Strings.RingFrameFailed(ex.Message);
                if (message != _frameError)
                {
                    _frameError = message;
                    log.Error(message);
                }
            }

            next += Interval;
            var now = _clock.Elapsed;
            if (next < now) next = now; // running behind: carry on from here rather than bursting to catch up
            else token.WaitHandle.WaitOne(next - now);
        }
    }

    /// Sends a frame, opening the ring first if need be. False when the ring isn't there or failed.
    bool Send(SKColor[] colors, TimeSpan now)
    {
        if (_ring is null && !Open(now)) return false;
        try
        {
            _ring!.Send(colors);
        }
        catch (Exception ex)
        {
            Fail(ex, now);
            return false;
        }

        if (_problem is not null)
        {
            _problem = null;
            log.Info(Strings.RingConnected);
        }

        return true;
    }

    bool Open(TimeSpan now)
    {
        var retryNow = Interlocked.Exchange(ref _retryNow, 0) == 1;
        if (now < _nextAttempt && !retryNow) return false;
        ILedRing? ring = null;
        try
        {
            ring = find();
            if (ring is null)
            {
                _nextAttempt = now + LookAgainDelay;
                Report(Strings.RingNotFound, log.Info); // an Info, once: a screen without a ring is normal
                return false;
            }

            ring.Open();
        }
        catch (Exception ex)
        {
            CloseQuietly(ring);
            Fail(ex, now);
            return false;
        }

        _ring = ring;
        _sentAt = null;
        return true;
    }

    /// Lets the ring go; the next attempt, after ReconnectDelay, finds it afresh.
    void Fail(Exception ex, TimeSpan now)
    {
        Release(dark: false);
        _nextAttempt = now + ReconnectDelay;
        Report(Strings.RingUnavailable(ex.Message, (int)ReconnectDelay.TotalSeconds), log.Warn);
    }

    void Report(string message, Action<string> write)
    {
        if (message == _problem) return;
        _problem = message;
        write(message);
    }

    /// Sends black and lets the ring go. A ring that fails now has gone, and its colors with it. With no ring open and
    /// none known to be dark (one that failed was let go still lit; one an earlier run or a crash left lit was never
    /// opened), it makes one try, now, to find and open the ring; one that isn't there or won't open is left as it is,
    /// quietly, since its problem was logged when it last mattered.
    void Darken()
    {
        if (_ring is null)
        {
            if (_dark) return;
            ILedRing? ring = null;
            try
            {
                ring = find();
                ring?.Open();
            }
            catch (Exception)
            {
                CloseQuietly(ring);
                return;
            }

            if (ring is null) return;
            _ring = ring;
        }

        try
        {
            _ring.Send(Black);
        }
        catch (Exception)
        {
            Release(dark: false);
            return;
        }

        Release(dark: true);
    }

    void Release(bool dark)
    {
        CloseQuietly(_ring);
        _ring = null;
        _sentAt = null;
        _dark = dark;
    }

    /// Closing a ring that failed may fail too; there is nothing left to let go of then.
    static void CloseQuietly(ILedRing? ring)
    {
        try
        {
            ring?.Close();
        }
        catch (Exception)
        {
            // See above.
        }
    }
}
