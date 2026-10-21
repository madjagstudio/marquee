using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Marquee.Core.Pipes;

namespace Marquee.Providers.Agents.Claude;

/// The named pipe that carries status-line JSON from `marquee statusline` (run by Claude Code) to the running app.
/// Only the user who started Marquee can connect, and the bridge only talks to a pipe owned by that same user.
///
/// Both checks use the user's own SID, not PipeOptions.CurrentUserOnly: that option uses the token's default owner,
/// which for an elevated administrator is BUILTIN\Administrators. An elevated `marquee run` (needed for CPU
/// temperatures) would then make a pipe that the user's non-elevated bridge can neither open nor recognize. With the
/// user's SID, any of this user's processes, elevated or not, can post to the pipe; other users can't. Letting a
/// non-elevated process of the same user write to an elevated Marquee is accepted on purpose: a message is at most
/// 64 KB of JSON, parsed defensively, and only ever shown on the screen. The lock itself is UserPipe's, which the
/// theme editor's sensor feed shares.
public static class StatusLinePipe
{
    /// One pipe per Windows user, since pipe names are shared by every session on the PC.
    public static string DefaultName { get; } = $"marquee-statusline-{CurrentUser?.Value ?? Environment.UserName}";

    /// The user this process runs as. An administrator's elevated and non-elevated processes share it.
    static SecurityIdentifier? CurrentUser => UserPipe.CurrentUser;

    /// Hands one status-line JSON to the app. False if Marquee isn't running (nothing to connect to within `timeout`),
    /// doesn't take the message within `timeout` (the whole call is bounded by it, connecting plus writing), the
    /// message is too large, the pipe isn't this user's, or the pipe failed; the status line works the same either way.
    public static bool TrySend(byte[] json, TimeSpan timeout, string? pipeName = null) =>
        TrySend(json, timeout, pipeName, CurrentUser);

    /// TrySend, talking only to a pipe owned by `owner`.
    internal static bool TrySend(byte[] json, TimeSpan timeout, string? pipeName, SecurityIdentifier? owner)
    {
        if (owner is null || json.Length == 0 || json.Length > ClaudeStatusLine.MaxBytes) return false;

        try
        {
            var clock = Stopwatch.StartNew();
            var limit = (int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue);
            using var pipe = new NamedPipeClientStream(".", pipeName ?? DefaultName, PipeDirection.Out, PipeOptions.Asynchronous);
            pipe.Connect(limit);

            // Another user could create a pipe with this name before Marquee starts; never hand the status line to it.
            if (!IsOwnedBy(pipe, owner)) return false;

            // The server's pipe has no buffer, so a write completes only when the server reads it. Bound the write by
            // whatever is left of the timeout, so a stalled app can never hang Claude Code's status line.
            using var giveUp = new CancellationTokenSource(Math.Max(1, limit - (int)clock.ElapsedMilliseconds));
            WriteAndFlush(pipe, json, giveUp.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException
                                       or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// One more instance of the app's end of the pipe. Throws UnauthorizedAccessException if the pipe already exists
    /// and isn't this user's (another user created the name first and let anyone add instances): Marquee would then
    /// be serving that user's pipe, whose access rules that user chose.
    internal static NamedPipeServerStream CreateServer(string name) => CreateServer(name, CurrentUser);

    /// CreateServer, accepting the pipe only if `owner` owns it.
    internal static NamedPipeServerStream CreateServer(string name, SecurityIdentifier? owner) =>
        UserPipe.CreateServer(name, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, owner);

    /// Owned by `user`, with full control for `user` only (full control includes adding instances, which the app
    /// needs for the next client).
    internal static PipeSecurity ServerSecurity(SecurityIdentifier user) => UserPipe.ServerSecurity(user);

    static bool IsOwnedBy(PipeStream pipe, SecurityIdentifier owner) => UserPipe.IsOwnedBy(pipe, owner);

    static async Task WriteAndFlush(Stream pipe, byte[] json, CancellationToken token)
    {
        await pipe.WriteAsync(json, token);
        await pipe.FlushAsync(token);
    }
}
