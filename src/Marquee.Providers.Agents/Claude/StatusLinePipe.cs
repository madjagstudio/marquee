using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace Marquee.Providers.Agents.Claude;

/// The named pipe that carries status-line JSON from `marquee statusline` (run by Claude Code) to the running app.
/// Both ends use PipeOptions.CurrentUserOnly: only the user who started Marquee can connect, and the bridge only
/// talks to a pipe owned by that same user.
public static class StatusLinePipe
{
    /// One pipe per Windows user, since pipe names are shared by every session on the PC.
    public static string DefaultName { get; } = $"marquee-statusline-{WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName}";

    /// Hands one status-line JSON to the app. False if Marquee isn't running (nothing to connect to within `timeout`),
    /// doesn't take the message within `timeout` (the whole call is bounded by it, connecting plus writing), the
    /// message is too large, or the pipe failed; the status line works the same either way.
    public static bool TrySend(byte[] json, TimeSpan timeout, string? pipeName = null)
    {
        if (json.Length == 0 || json.Length > ClaudeStatusLine.MaxBytes) return false;

        try
        {
            var clock = Stopwatch.StartNew();
            var limit = (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);
            using var pipe = new NamedPipeClientStream(".", pipeName ?? DefaultName, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(limit);

            // The server's pipe has no buffer, so a write completes only when the server reads it. Bound the write by
            // whatever is left of the timeout, so a stalled app can never hang Claude Code's status line.
            using var giveUp = new CancellationTokenSource(Math.Max(1, limit - (int)clock.ElapsedMilliseconds));
            WriteAndFlush(pipe, json, giveUp.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    static async Task WriteAndFlush(Stream pipe, byte[] json, CancellationToken token)
    {
        await pipe.WriteAsync(json, token);
        await pipe.FlushAsync(token);
    }
}
