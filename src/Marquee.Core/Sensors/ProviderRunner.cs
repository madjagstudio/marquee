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

    /// How long Stop and Dispose wait for each provider's thread to finish its current poll.
    static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    /// A running provider, its thread, and what stops that thread alone.
    sealed record Running(ISensorProvider Provider, Thread Thread, CancellationTokenSource Stop);

    readonly TimeSpan _firstRetry = firstRetry ?? TimeSpan.FromSeconds(1);
    readonly List<Running> _running = [];
    bool _disposed;

    public void Start(ISensorProvider provider)
    {
        lock (_running)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var stop = new CancellationTokenSource();
            var token = stop.Token;
            var thread = new Thread(() => Run(provider, token)) { IsBackground = true, Name = $"Marquee provider: {provider.Name}" };
            thread.Start();
            _running.Add(new Running(provider, thread, stop));
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
        foreach (var (provider, thread, stop) in running)
        {
            if (!thread.Join(TimeSpan.FromTicks(Math.Max(0, (deadline - DateTime.UtcNow).Ticks))))
            {
                // A poll stuck in a driver call must not hang shutdown. Provider threads are background threads, so
                // this one ends with the process. Disposing the provider under a running poll would be worse, and the
                // thread reads its token's wait handle after the poll, so that stays undisposed too.
                log.Warn($"{provider.Name} did not stop within {StopTimeout.TotalSeconds:0} s; leaving it to exit with the process.");
                continue;
            }

            stop.Dispose();
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                log.Error($"{provider.Name} failed to shut down: {ex.Message}");
            }
        }
    }

    void Run(ISensorProvider provider, CancellationToken token)
    {
        var failures = 0;
        string? lastError = null;

        while (!token.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                provider.Poll(store);
                if (failures > 0) log.Info($"{provider.Name} is working again.");
                failures = 0;
                lastError = null;
                wait = provider.Interval;
            }
            catch (Exception ex)
            {
                failures++;
                if (ex.Message != lastError)
                {
                    lastError = ex.Message;
                    log.Error($"{provider.Name} failed: {ex.Message} Its values will go stale until it recovers.");
                }

                wait = Backoff(_firstRetry, failures);
            }

            token.WaitHandle.WaitOne(wait < MinWait ? MinWait : wait);
        }
    }
}
