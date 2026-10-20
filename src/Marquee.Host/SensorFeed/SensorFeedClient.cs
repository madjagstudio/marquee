using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Marquee.Core.Pipes;

namespace Marquee.Host.SensorFeed;

/// Reads the sensor feed of the Marquee running as this user (SensorFeedServer), for the theme editor. Each read
/// connects, takes the one message, and closes. The result says why there is no message (SensorFeedStatus): no Marquee
/// running, a server that doesn't answer within the timeout, a pipe that isn't this user's, or a message over its cap,
/// malformed or of another version. The editor then falls back to reading sensors itself, without an error.
///
/// Safe to call from a UI thread: no continuation comes back to the caller's SynchronizationContext, so the
/// synchronous reads, which block on the asynchronous ones, can't deadlock on a Dispatcher. The editor's 1 Hz poll
/// uses the asynchronous ones, which don't block it at all.
public sealed class SensorFeedClient
{
    readonly SensorFeedPipes _pipes;
    readonly SecurityIdentifier? _owner;

    public SensorFeedClient(SensorFeedPipes? pipes = null)
        : this(pipes, UserPipe.CurrentUser)
    {
    }

    /// A client that trusts only a pipe owned by `owner`.
    internal SensorFeedClient(SensorFeedPipes? pipes, SecurityIdentifier? owner)
    {
        _pipes = pipes ?? SensorFeedPipes.Default;
        _owner = owner;
    }

    /// The current values, or why there are none. The whole read, connecting plus reading, is bounded by `timeout`.
    /// Cancelling `cancel` throws OperationCanceledException.
    public Task<SensorFeedResult<SensorFeedValues>> ReadValuesAsync(TimeSpan timeout, CancellationToken cancel = default) =>
        Read(_pipes.Values, SensorFeedFormat.MaxValuesBytes, SensorFeedJson.ReadValues, timeout, cancel);

    /// The graph history, or why there is none. The whole read, connecting plus reading, is bounded by `timeout`.
    /// Cancelling `cancel` throws OperationCanceledException.
    public Task<SensorFeedResult<SensorFeedHistory>> ReadHistoryAsync(TimeSpan timeout, CancellationToken cancel = default) =>
        Read(_pipes.History, SensorFeedFormat.MaxHistoryBytes, SensorFeedJson.ReadHistory, timeout, cancel);

    /// The current values, or null. The whole call, connecting plus reading, is bounded by `timeout`.
    public SensorFeedValues? TryReadValues(TimeSpan timeout) => ReadValuesAsync(timeout).GetAwaiter().GetResult().Message;

    /// The graph history, or null. The whole call, connecting plus reading, is bounded by `timeout`.
    public SensorFeedHistory? TryReadHistory(TimeSpan timeout) => ReadHistoryAsync(timeout).GetAwaiter().GetResult().Message;

    // Every await here and below is ConfigureAwait(false): see the class comment.
    async Task<SensorFeedResult<T>> Read<T>(string name, int maxBytes, Func<ReadOnlyMemory<byte>, T> parse, TimeSpan timeout,
        CancellationToken cancel) where T : class
    {
        if (_owner is null) return new(SensorFeedStatus.NotTrusted, null);

        var clock = Stopwatch.StartNew();
        var limit = (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
        var connected = await Connect(pipe, name, limit, cancel).ConfigureAwait(false);
        if (connected != SensorFeedStatus.Ok) return new(connected, null);

        byte[]? json;
        try
        {
            // Another user, or a Low-integrity process of this one, could create a pipe with this name before Marquee
            // starts; never show what it sends.
            if (!UserPipe.IsTrusted(pipe, _owner)) return new(SensorFeedStatus.NotTrusted, null);

            using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            giveUp.CancelAfter(Math.Max(1, limit - (int)clock.ElapsedMilliseconds));
            json = await ReadMessage(pipe, maxBytes, giveUp.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new(SensorFeedStatus.TimedOut, null);
        }
        catch (IOException)
        {
            // The server closed the pipe before the whole message came.
            return new(SensorFeedStatus.BadData, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            // The pipe's owner couldn't be read.
            return new(SensorFeedStatus.NotTrusted, null);
        }

        if (json is null) return new(SensorFeedStatus.BadData, null);
        try
        {
            return new(SensorFeedStatus.Ok, parse(json));
        }
        catch (FormatException)
        {
            return new(SensorFeedStatus.BadData, null);
        }
    }

    /// Ok once connected; otherwise why not.
    static async Task<SensorFeedStatus> Connect(NamedPipeClientStream pipe, string name, int milliseconds, CancellationToken cancel)
    {
        // Connect keeps trying a pipe that doesn't exist until its timeout, which with no tray would stall a 1 Hz poll
        // for its whole timeout every second. The server keeps its pipe for as long as it runs, between connections
        // too, so a pipe that doesn't exist means no Marquee serves it, and is said at once.
        if (!PipeNative.Exists(name)) return SensorFeedStatus.NotRunning;

        try
        {
            await pipe.ConnectAsync(milliseconds, cancel).ConfigureAwait(false);
            return SensorFeedStatus.Ok;
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
            // The pipe went away, or was made again, while connecting.
        }
        catch (UnauthorizedAccessException)
        {
            // The pipe's rules shut this user out, so it isn't this user's Marquee.
            return SensorFeedStatus.NotTrusted;
        }

        return PipeNative.Exists(name) ? SensorFeedStatus.Busy : SensorFeedStatus.NotRunning;
    }

    /// The length, checked against the cap before anything that large is allocated, then the JSON; null for a length
    /// of 0 or over the cap.
    static async Task<byte[]?> ReadMessage(Stream pipe, int maxBytes, CancellationToken giveUp)
    {
        var prefix = new byte[SensorFeedFormat.PrefixBytes];
        await pipe.ReadExactlyAsync(prefix, giveUp).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (length == 0 || length > maxBytes) return null;

        var json = new byte[length];
        await pipe.ReadExactlyAsync(json, giveUp).ConfigureAwait(false);
        return json;
    }
}
