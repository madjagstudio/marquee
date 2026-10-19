using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Marquee.Providers.Fps;

/// The ETW session this Marquee process's PresentMon records in: `Marquee-<pid>`, one per process, so the tray and a
/// `marquee run` in an admin terminal can both measure FPS. With one name between them, each PresentMon's
/// --stop_existing_session would stop the other's, and the two would take turns failing for as long as both ran.
///
/// A session outlives the process that started it. Killing PresentMon leaves its session running, and so does a
/// Marquee that crashes (its job kills PresentMon). So Stop ends this process's session once PresentMon is gone, and
/// StopOrphans, before each start, ends those of Marquee processes that are no longer running; a leftover lasts until
/// the next start rather than until a reboot (Windows runs only 64 sessions at once unless configured otherwise). A
/// process id can be reused: a leftover whose id now belongs to some other program is stopped after that program
/// exits, and one whose id now belongs to a new Marquee is that Marquee's own name, which its --stop_existing_session
/// takes over.
sealed class PresentMonSession(IEtwSessions sessions, Func<int, bool> isRunning, int ownProcessId)
{
    const string Prefix = "Marquee-";

    public string Name { get; } = Prefix + ownProcessId.ToString(CultureInfo.InvariantCulture);

    /// Stops the sessions of Marquee processes that have gone. Never throws: a leftover costs one of Windows' sessions,
    /// which is no reason to go without FPS, and the next start tries again.
    public void StopOrphans()
    {
        IReadOnlyList<string> names;
        try
        {
            names = sessions.Names();
        }
        catch (Win32Exception)
        {
            return;
        }

        foreach (var name in names)
            if (ProcessId(name) is { } id && id != ownProcessId && !isRunning(id))
                TryStop(name);
    }

    /// Stops this process's session, once its PresentMon has been stopped. Never throws.
    public void Stop() => TryStop(Name);

    /// Whether a process with this id is running. Needs no handle to it, so it works for elevated ones too.
    public static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    void TryStop(string name)
    {
        try
        {
            sessions.Stop(name);
        }
        catch (Win32Exception)
        {
            // Left for the next start, as above.
        }
    }

    /// The process id in a `Marquee-<pid>` name, or null for any other session. Digits only, as Name writes them.
    static int? ProcessId(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal)
        && int.TryParse(name.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
}
