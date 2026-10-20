using System.IO.Pipes;
using Marquee.Core.Logging;
using Marquee.Core.Pipes;
using Marquee.Core.Sensors;
using Marquee.Host.Resources;

namespace Marquee.Host.SensorFeed;

/// Serves the sensor store to the theme editor over two pipes, values and history (SensorFeedPipes), so the editor can
/// show readings it can't take itself, such as an elevated Marquee's CPU temperatures.
///
/// Write-only: each pipe is outbound, so the server can't read from it and a client can't write to it; nothing a client
/// sends can reach Marquee. A connection gets one message (SensorFeedFormat) and is closed.
///
/// Locked to the user's SID, as the status-line pipe is (UserPipe), so the user's editor reads an elevated tray's pipe.
/// Each pipe has a single instance, made when the server starts and kept until it is disposed: one client at a time,
/// and a client that comes while another is served waits for the instance to come free. Keeping it between
/// connections holds the name, so no other process can take it (another Marquee of this user, which then serves
/// nothing rather than every other connection, or a process pretending to be Marquee), and a client can tell a pipe
/// that is busy from one that doesn't exist (SensorFeedClient). At most 5 connections a second per pipe; past that the
/// instance stays busy until the oldest of the last 5 is a second old. A client that stops reading is dropped after
/// 2 s, one that leaves mid-message as soon as it does. The history is built at most once a second, and a client
/// within the same second gets the same message. The server runs on its own tasks and only takes a snapshot of
/// the store, so no client can hold up the store or the render loop.
public sealed class SensorFeedServer : IDisposable
{
    readonly SensorStore _store;
    readonly ILog _log;
    readonly TimeProvider _time;
    readonly SensorFeedPipes _pipes;
    readonly SensorFeedLimits _limits;
    readonly CancellationTokenSource _stop = new();
    Task[] _loops = [];
    bool _disposed;

    public SensorFeedServer(SensorStore store, ILog log, TimeProvider time, SensorFeedPipes? pipes = null)
        : this(store, log, time, pipes, SensorFeedLimits.Default)
    {
    }

    internal SensorFeedServer(SensorStore store, ILog log, TimeProvider time, SensorFeedPipes? pipes, SensorFeedLimits limits)
    {
        _store = store;
        _log = log;
        _time = time;
        _pipes = pipes ?? SensorFeedPipes.Default;
        _limits = limits;
    }

    /// Starts serving both pipes. Each pipe exists once this returns, so a client never takes a Marquee that is just
    /// starting for none. A pipe that can't be made (another Marquee of this user serves it, say) is logged once and
    /// tried again each second; the rest of Marquee runs on either way.
    public void Start()
    {
        if (_disposed || _loops.Length > 0) return;
        var stop = _stop.Token;
        var values = new FeedPipe(this, _pipes.Values, () => SensorFeedContent.Values(_store.Snapshot(), _limits.MaxValuesBytes),
            reuseFor: TimeSpan.Zero);
        // Cutting a large history down to its cap encodes it several times over (SensorFeedContent), hundreds of
        // milliseconds for a store of a thousand keys; built at most once a second, it costs that once a second however
        // many clients ask. A graph point is added once a second at most anyway.
        var history = new FeedPipe(this, _pipes.History, () => SensorFeedContent.History(_store.Snapshot(), _limits.MaxHistoryBytes),
            reuseFor: TimeSpan.FromSeconds(1));
        values.TryCreate();
        history.TryCreate();
        _loops = [Task.Run(() => values.Serve(stop)), Task.Run(() => history.Serve(stop))];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stop.Cancel();
        var stopped = true;
        try
        {
            stopped = Task.WaitAll(_loops, TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loops report their own failures; shutting down never throws.
        }

        // A loop stuck past the wait still holds the token; leave the source to the finalizer in that case.
        if (stopped) _stop.Dispose();
    }

    /// False when the client gave up before the connection completed.
    static async Task<bool> Connected(NamedPipeServerStream pipe, CancellationToken stop)
    {
        try
        {
            await pipe.WaitForConnectionAsync(stop);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// Writes the message, giving up on a client that doesn't take it within the write timeout or that leaves; true if
    /// the client took it all. The pipe has no buffer, so the write completes only as the client reads, and nothing is
    /// left in the pipe for the disconnect that follows to discard.
    async Task<bool> Send(NamedPipeServerStream pipe, byte[] json, CancellationToken stop)
    {
        using var timeout = new CancellationTokenSource(_limits.WriteTimeout, _time);
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
        try
        {
            await pipe.WriteAsync(SensorFeedFormat.Frame(json), giveUp.Token);
            return true;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            // The client stopped reading; disconnecting drops it.
            return false;
        }
        catch (IOException)
        {
            // The client left mid-message.
            return false;
        }
    }

    /// One pipe: its instance, its rate, the problem it last reported, and its last message, which is sent again until
    /// it is `reuseFor` old.
    sealed class FeedPipe(SensorFeedServer server, string name, Func<byte[]> message, TimeSpan reuseFor)
    {
        readonly ConnectionRate _rate = new(server._time, server._limits.ConnectionsPerSecond, TimeSpan.FromSeconds(1));
        NamedPipeServerStream? _instance;
        string? _lastProblem;
        byte[]? _built;
        long _builtAt;

        /// Makes the pipe's instance, the first of its name, or says why it can't be made.
        public bool TryCreate()
        {
            try
            {
                _instance = UserPipe.CreateServer(name, PipeDirection.Out, maxInstances: 1, firstInstance: true);
                return true;
            }
            catch (Exception ex)
            {
                // Another of this user's Marquee processes has the name, or another user took it.
                Report(ex);
                return false;
            }
        }

        public async Task Serve(CancellationToken stop)
        {
            try
            {
                while (true)
                {
                    if (_instance is null && !TryCreate())
                    {
                        await Task.Delay(server._limits.RetryDelay, server._time, stop);
                        continue;
                    }

                    try
                    {
                        // The instance stays while the rate holds the next connection back, busy to a client, which
                        // waits for it.
                        await _rate.WaitTurn(stop);
                        var connected = await Connected(_instance!, stop);
                        _rate.Record(); // a client that left before it was served counts too
                        if (connected && Build() is { } json && await server.Send(_instance!, json, stop)) _lastProblem = null;
                        _instance!.Disconnect();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // The instance failed: said once until a client is served again, then a new one is made.
                        Report(ex);
                        _instance?.Dispose();
                        _instance = null;
                        await Task.Delay(server._limits.RetryDelay, server._time, stop);
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Disposed.
            }
            finally
            {
                _instance?.Dispose();
            }
        }

        /// The message, or null when it can't be built, said once until a client is served again. The client is
        /// disconnected without one, and the instance kept.
        byte[]? Build()
        {
            if (_built is { } recent && server._time.GetElapsedTime(_builtAt) < reuseFor) return recent;
            try
            {
                _built = message();
                _builtAt = server._time.GetTimestamp();
                return _built;
            }
            catch (Exception ex)
            {
                Report(ex);
                return null;
            }
        }

        void Report(Exception ex)
        {
            var problem = ex is IOException or UnauthorizedAccessException
                ? Strings.SensorFeedUnavailable(name, ex.Message)
                : Strings.SensorFeedFailed(name, ex.Message);
            if (problem != _lastProblem) server._log.Warn(problem);
            _lastProblem = problem;
        }
    }
}
