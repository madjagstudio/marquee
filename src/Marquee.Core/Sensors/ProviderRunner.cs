using Marquee.Core.Logging;

namespace Marquee.Core.Sensors;

/// A data source that writes sensor keys into the store. Providers never see layouts.
public interface ISensorProvider : IDisposable
{
    string Name { get; }
    TimeSpan Interval { get; }

    /// Reads the source once and writes its keys. Throwing is fine: the runner logs it and retries with backoff,
    /// and the provider's keys go stale on their own through their ttl.
    void Poll(SensorStore store);
}

/// Runs each provider on its own thread and schedule, so one slow or failing provider never affects the others
/// or the render loop.
public sealed class ProviderRunner(SensorStore store, ILog log, TimeSpan? firstRetry = null) : IDisposable
{
    public static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(60);

    /// A provider that reports a zero or negative interval is polled at this rate instead of spinning.
    static readonly TimeSpan MinWait = TimeSpan.FromMilliseconds(50);

    /// A running provider, its thread, and what stops that thread alone.
    sealed class Running(ISensorProvider provider, CancellationTokenSource stop)
    {
        public ISensorProvider Provider { get; } = provider;
        public CancellationTokenSource Stop { get; } = stop;
        public Thread? Thread { get; set; }

        // Under the entry's own lock, so the thread and Finish agree on who disposes the provider.
        public bool Ended { get; set; } // the thread is past its last poll
        public bool LeftToThread { get; set; } // Finish stopped waiting, so the thread disposes the provider when it ends

        // Why the last poll failed, until one works again; written by the thread, read by Problems.
        public volatile string? Problem;
    }

    readonly TimeSpan _firstRetry = firstRetry ?? TimeSpan.FromSeconds(1);
    readonly List<Running> _running = [];
    bool _disposed;

    /// How long Stop and Dispose wait for each provider's thread to finish its current poll. A setting only for tests.
    internal TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// What is wrong right now, one line per provider whose last poll failed ("FPS failed: PresentMon is missing…"),
    /// in the order they started, for a status line such as the tray's. A provider drops out once a poll works again
    /// or it is stopped. The log has each failure once, as it happened. Thread-safe.
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_running) return _running.Select(r => r.Problem).OfType<string>().ToList();
        }
    }

    public void Start(ISensorProvider provider)
    {
        lock (_running)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var running = new Running(provider, new CancellationTokenSource());
            running.Thread = new Thread(() => Run(running)) { IsBackground = true, Name = $"Marquee provider: {provider.Name}" };
            running.Thread.Start();
            _running.Add(running);
        }
    }

    /// Stops one provider and disposes it, as Dispose does for all of them, while the others run on: a settings change
    /// that switches one source leaves the rest alone. A provider this runner isn't running is ignored.
    public void Stop(ISensorProvider provider)
    {
        Running? running;
        lock (_running)
        {
            running = _running.Find(r => ReferenceEquals(r.Provider, provider));
            if (running is null) return;
            _running.Remove(running);
        }

        Finish([running]);
    }

    /// 1 s, 2 s, 4 s, … capped at 60 s (with the default first retry).
    internal static TimeSpan Backoff(TimeSpan firstRetry, int failures) =>
        TimeSpan.FromTicks(Math.Min(MaxRetry.Ticks, firstRetry.Ticks << Math.Clamp(failures - 1, 0, 20)));

    public void Dispose()
    {
        List<Running> running;
        lock (_running)
        {
            if (_disposed) return;
            _disposed = true;
            running = [.. _running];
            _running.Clear();
        }

        Finish(running);
    }

    /// Stops the threads, waits for each to finish its current poll, then disposes the providers.
    void Finish(IReadOnlyList<Running> running)
    {
        foreach (var r in running) r.Stop.Cancel();
        var deadline = DateTime.UtcNow + StopTimeout; // one deadline for all threads, so N stuck providers cost 3 s, not 3·N s
        foreach (var r in running)
        {
            var ended = r.Thread!.Join(TimeSpan.FromTicks(Math.Max(0, (deadline - DateTime.UtcNow).Ticks)));
            lock (r)
            {
                if (!ended && !r.Ended)
                {
                    // A poll stuck in a driver call must not hang a settings change or shutdown, and disposing the
                    // provider under a running poll would be worse. Its token is cancelled, so its thread ends once the
                    // poll returns, and disposes the provider (and the token's source, whose wait handle it reads after
                    // the poll) then: Stop can run in a process that lives for days, so a driver handle or a child
                    // process mustn't wait for the process to exit. If the poll never returns, both stay until then;
                    // it is a background thread, so it doesn't keep the process alive.
                    r.LeftToThread = true;
                    log.Warn($"{r.Provider.Name} did not stop within {StopTimeout.TotalSeconds:0} s; it will be shut down when its current poll returns.");
                    continue;
                }
            }

            Shut(r);
        }
    }

    /// Disposes a provider whose thread has ended, and what stopped the thread.
    void Shut(Running running)
    {
        running.Stop.Dispose();
        try
        {
            running.Provider.Dispose();
        }
        catch (Exception ex)
        {
            log.Error($"{running.Provider.Name} failed to shut down: {ex.Message}");
        }
    }

    void Run(Running running)
    {
        var (provider, token) = (running.Provider, running.Stop.Token);
        var failures = 0;
        string? lastError = null;

        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan wait;
                try
                {
                    provider.Poll(store);
                    if (failures > 0) log.Info($"{provider.Name} is working again.");
                    failures = 0;
                    lastError = null;
                    running.Problem = null;
                    wait = provider.Interval;
                }
                catch (Exception ex)
                {
                    failures++;
                    running.Problem = $"{provider.Name} failed: {ex.Message}";
                    if (ex.Message != lastError)
                    {
                        lastError = ex.Message;
                        log.Error($"{running.Problem} Its values will go stale until it recovers.");
                    }

                    wait = Backoff(_firstRetry, failures);
                }

                token.WaitHandle.WaitOne(wait < MinWait ? MinWait : wait);
            }
        }
        finally
        {
            bool shut;
            lock (running)
            {
                running.Ended = true;
                shut = running.LeftToThread;
            }

            if (shut) Shut(running);
        }
    }
}
