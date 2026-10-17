using System.IO.Pipes;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Agents.Claude;

/// Receives status-line JSON over the pipe and writes Claude Code's usage limits into the store. One message per
/// connection, handled in arrival order. A malformed message is logged (once per distinct problem) and ignored; the
/// next good one works again.
public sealed class StatusLineServer(SensorStore store, ILog log, string? pipeName = null, Func<DateTimeOffset>? now = null) : IDisposable
{
    /// How long a connected client may take to send its message.
    static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    readonly string _pipeName = pipeName ?? StatusLinePipe.DefaultName;
    readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    readonly CancellationTokenSource _stop = new();
    string? _lastProblem;
    Task? _loop;
    bool _disposed;

    /// Starts listening. False (with an error logged) if the pipe can't be created; the rest of Marquee runs on.
    public bool Start()
    {
        NamedPipeServerStream first;
        try
        {
            first = CreatePipe();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Claude Code limits are unavailable: the status-line pipe could not be created ({ex.Message}).");
            return false;
        }

        _loop = Task.Run(() => Listen(first, _stop.Token));
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stop.Cancel();
        var stopped = true;
        try
        {
            stopped = _loop is null || _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // The loop reports its own failures; shutting down never throws.
        }

        // A loop stuck past the wait still holds the token; leave the source to the finalizer in that case.
        if (stopped) _stop.Dispose();
    }

    NamedPipeServerStream CreatePipe() => new(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    async Task Listen(NamedPipeServerStream first, CancellationToken stop)
    {
        try
        {
            await AcceptLoop(first, stop);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error($"Claude Code limits stopped updating: the status-line pipe failed ({ex.Message}).");
        }
    }

    async Task AcceptLoop(NamedPipeServerStream first, CancellationToken stop)
    {
        // The instance listening for the next client. Whatever way the loop ends, it must not stay open unread.
        NamedPipeServerStream? pending = first;
        try
        {
            while (true)
            {
                try
                {
                    await pending.WaitForConnectionAsync(stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // The client gave up before the connection completed; wait for the next one.
                    var abandoned = pending;
                    pending = null;
                    await abandoned.DisposeAsync();
                    pending = CreatePipe();
                    continue;
                }

                // Open the next instance before reading, so the next client can connect meanwhile. Messages are still
                // handled one at a time, in the order they arrived. Receive owns (and disposes) the connected one.
                var connected = pending;
                pending = null;
                try
                {
                    pending = CreatePipe();
                }
                catch
                {
                    await connected.DisposeAsync();
                    throw;
                }

                await Receive(connected, stop);
            }
        }
        finally
        {
            if (pending is not null) await pending.DisposeAsync();
        }
    }

    async Task Receive(NamedPipeServerStream pipe, CancellationToken stop)
    {
        await using (pipe)
        {
            byte[] message;
            try
            {
                message = await ReadAll(pipe, stop);
            }
            catch (OperationCanceledException)
            {
                // Shutting down is not a problem; otherwise the client was too slow.
                if (!stop.IsCancellationRequested) Problem("a status-line message took too long to arrive");
                return;
            }
            catch (IOException)
            {
                return; // the client went away mid-message
            }

            if (message.Length > ClaudeStatusLine.MaxBytes)
            {
                Problem($"a status-line message was larger than {ClaudeStatusLine.MaxBytes / 1024} KB");
                return;
            }

            try
            {
                ClaudeStatusLine.Apply(ClaudeStatusLine.Parse(message), store, _now());
                _lastProblem = null;
            }
            catch (FormatException ex)
            {
                Problem(ex.Message);
            }
        }
    }

    /// Reads until the client closes its end, stopping one chunk past the limit so an oversized message is detectable.
    static async Task<byte[]> ReadAll(Stream pipe, CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(ReadTimeout);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while (buffer.Length <= ClaudeStatusLine.MaxBytes && (read = await pipe.ReadAsync(chunk, timeout.Token)) > 0)
            buffer.Write(chunk, 0, read);
        return buffer.ToArray();
    }

    /// Messages are handled one at a time, so this needs no lock.
    void Problem(string problem)
    {
        if (problem == _lastProblem) return;
        _lastProblem = problem;
        log.Warn($"Ignored a Claude Code status-line update: {problem}. The limits keep their last values.");
    }
}
