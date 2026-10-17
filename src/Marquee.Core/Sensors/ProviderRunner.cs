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

    readonly TimeSpan _firstRetry = firstRetry ?? TimeSpan.FromSeconds(1);
    readonly CancellationTokenSource _stop = new();
    readonly List<(ISensorProvider Provider, Thread Thread)> _running = [];
    bool _disposed;

    public void Start(ISensorProvider provider)
    {
        lock (_running)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var token = _stop.Token;
            var thread = new Thread(() => Run(provider, token)) { IsBackground = true, Name = $"Marquee provider: {provider.Name}" };
            thread.Start();
            _running.Add((provider, thread));
        }
    }

    /// 1 s, 2 s, 4 s, … capped at 60 s (with the default first retry).
    internal static TimeSpan Backoff(TimeSpan firstRetry, int failures) =>
        TimeSpan.FromTicks(Math.Min(MaxRetry.Ticks, firstRetry.Ticks << Math.Clamp(failures - 1, 0, 20)));

    public void Dispose()
    {
        List<(ISensorProvider Provider, Thread Thread)> running;
        lock (_running)
        {
            if (_disposed) return;
            _disposed = true;
            running = [.. _running];
        }

        _stop.Cancel();
        foreach (var (_, thread) in running) thread.Join();
        foreach (var (provider, _) in running)
        {
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                log.Error($"{provider.Name} failed to shut down: {ex.Message}");
            }
        }

        _stop.Dispose();
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
