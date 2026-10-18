namespace Marquee.Providers.Fps;

/// One process's frame rate now. Fps and FrameTimeMs cover the last second, OnePercentLowFps the last 10 seconds.
/// LastArrival is when the newest of those frames was read, on the clock passed to FrameStats.Add.
public sealed record FpsReading(double Fps, double FrameTimeMs, double OnePercentLowFps, TimeSpan LastArrival);

/// Keeps the last 10 seconds of PresentMon frames for every presenting process and works out each one's frame rate.
/// A frame is placed in time by its start time when the CSV has one, else by when it was read. Start times count from
/// when PresentMon started recording, so one FrameStats holds the frames of one PresentMon run: make a new one when
/// PresentMon is restarted.
///
/// Thread-safety: one lock guards everything. The reader thread adds a row at a time (constant work apart from
/// trimming) while the provider reads a few times a second (at most a sort of MaxFramesPerSwapChain numbers), so the
/// lock is held only briefly and there is nothing to gain from anything cleverer.
public sealed class FrameStats
{
    /// Caps the frames kept per swap chain, so a menu presenting at 1,000 fps can't grow without bound. At that rate
    /// the 1% low covers the last 2 seconds instead of 10.
    public const int MaxFramesPerSwapChain = 2000;

    const double WindowMs = 1000;
    const double KeepMs = 10_000;
    static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(KeepMs);
    static readonly TimeSpan PruneEvery = TimeSpan.FromSeconds(1);

    readonly object _gate = new();
    readonly Dictionary<int, Dictionary<string, SwapChain>> _processes = [];
    TimeSpan? _lastPrune;

    /// Adds a frame read at arrivedAt, the provider's monotonic clock. Processes that have gone quiet are pruned here
    /// too, at most once a second, so memory stays bounded even if Prune is never called.
    public void Add(PresentMonFrame frame, TimeSpan arrivedAt)
    {
        lock (_gate)
        {
            if (!_processes.TryGetValue(frame.ProcessId, out var chains))
                _processes[frame.ProcessId] = chains = new(StringComparer.Ordinal);
            if (!chains.TryGetValue(frame.SwapChain, out var chain))
                chains[frame.SwapChain] = chain = new SwapChain();
            chain.Add(frame.StartMs ?? arrivedAt.TotalMilliseconds, frame.IntervalMs, arrivedAt);

            if (_lastPrune is not { } last || arrivedAt - last >= PruneEvery)
                PruneLocked(arrivedAt);
        }
    }

    /// The process's frame rate, from its busiest swap chain over the last second: a game can also present an overlay
    /// or launcher swap chain at another rate, and mixing the two would make the game look faster than it is. False
    /// when the process has no frames.
    public bool TryRead(int processId, out FpsReading reading)
    {
        reading = null!;
        lock (_gate)
        {
            if (!_processes.TryGetValue(processId, out var chains) || chains.Count == 0) return false;

            var newest = chains.Values.Max(c => c.NewestAt);
            var busiest = chains.Values
                .OrderByDescending(c => c.CountAfter(newest - WindowMs))
                .ThenByDescending(c => c.NewestAt)
                .First();

            // Each frame's interval reaches back to the frame before it, so n frames cover the sum of their n
            // intervals. Dividing by that (not by the n - 1 gaps between the first and last start times) is what
            // makes 60 evenly spaced frames read 60 fps, and it makes Fps exactly 1000 / FrameTimeMs.
            var recent = busiest.IntervalsAfter(busiest.NewestAt - WindowMs).ToList();
            if (recent.Count == 0) return false;
            var frameTimeMs = recent.Average();

            // The "1% low" as frame-time tools usually define it: the mean of the slowest 1% of frame intervals (at
            // least one frame) over the last 10 seconds, shown as a frame rate.
            var slowestFirst = busiest.IntervalsAfter(busiest.NewestAt - KeepMs).OrderByDescending(ms => ms).ToList();
            var slowMs = slowestFirst.Take(Math.Max(1, slowestFirst.Count / 100)).Average();

            reading = new FpsReading(1000 / frameTimeMs, frameTimeMs, 1000 / slowMs, busiest.LastArrival);
            return true;
        }
    }

    /// Drops swap chains, and processes, that have had no frame arrive in the last 10 seconds.
    public void Prune(TimeSpan now)
    {
        lock (_gate) PruneLocked(now);
    }

    internal int FramesKept(int processId)
    {
        lock (_gate)
            return _processes.TryGetValue(processId, out var chains) ? chains.Values.Sum(c => c.Count) : 0;
    }

    void PruneLocked(TimeSpan now)
    {
        if (_lastPrune is not { } last || now > last) _lastPrune = now;
        foreach (var (processId, chains) in _processes.ToList())
        {
            foreach (var (address, chain) in chains.ToList())
                if (now - chain.LastArrival > Quiet) chains.Remove(address);
            if (chains.Count == 0) _processes.Remove(processId);
        }
    }

    /// One swap chain's frames, oldest first, as (time in ms, interval in ms), trimmed to the 10 seconds before the
    /// newest frame and to MaxFramesPerSwapChain.
    sealed class SwapChain
    {
        readonly Queue<(double AtMs, double IntervalMs)> _frames = new();

        public double NewestAt { get; private set; } = double.NegativeInfinity;
        public TimeSpan LastArrival { get; private set; }
        public int Count => _frames.Count;

        public void Add(double atMs, double intervalMs, TimeSpan arrivedAt)
        {
            _frames.Enqueue((atMs, intervalMs));
            NewestAt = Math.Max(NewestAt, atMs);
            if (arrivedAt > LastArrival) LastArrival = arrivedAt;
            while (_frames.Count > 0 && (_frames.Count > MaxFramesPerSwapChain || _frames.Peek().AtMs <= NewestAt - KeepMs))
                _frames.Dequeue();
        }

        public int CountAfter(double fromMs) => _frames.Count(f => f.AtMs > fromMs);

        public IEnumerable<double> IntervalsAfter(double fromMs) => _frames.Where(f => f.AtMs > fromMs).Select(f => f.IntervalMs);
    }
}
