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
    /// the message is too large, or the pipe failed; the status line works the same either way.
    public static bool TrySend(byte[] json, TimeSpan timeout, string? pipeName = null)
    {
        if (json.Length == 0 || json.Length > ClaudeStatusLine.MaxBytes) return false;

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? DefaultName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect((int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue));
            pipe.Write(json);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
