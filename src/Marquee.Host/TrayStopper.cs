using System.ComponentModel;
using System.Diagnostics;

namespace Marquee.Host;

/// A running MarqueeTray.exe, as TrayStopper handles it. Dispose lets go of the process.
public interface ITray : IDisposable
{
    int ProcessId { get; }

    /// Sets the tray's quit event (TrayNames.Quit). False when it has none to set: a tray that has just exited, or one
    /// built before the event existed.
    bool AskToQuit();

    /// Whether the tray has exited within `timeout`.
    bool WaitForExit(TimeSpan timeout);

    /// Ends the process at once, and waits briefly for it to let go of its files. Throws Win32Exception when it can't.
    void Kill();
}

/// The trays running from one exe. Tests put a fake in its place, so no test signals or ends a real process.
public interface ITrayProcesses
{
    IReadOnlyList<ITray> Running(string trayExePath);
}

/// How many trays quit when asked, and how many had to be ended.
public sealed record TrayStopResult(int Quit, int Killed);

/// Closes every tray running from the installed MarqueeTray.exe, in every user's session, before the installer replaces
/// or removes its files: otherwise they are in use, and Windows Installer can only finish at the next restart. Each tray
/// is asked to quit first, so it lets go of the screen, turns the ring off and saves its state as Quit does; a tray that
/// hasn't quit when the grace time is up, or has no quit event to hear, is ended. Only SYSTEM (the installer) and
/// administrators can do either to an elevated tray.
public static class TrayStopper
{
    /// Quitting stops the engine, which lets go of the screen and the ring in well under a second; this is for a busy PC.
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    public static TrayStopResult Stop(string trayExePath, ITrayProcesses processes, TimeSpan grace)
    {
        var trays = processes.Running(trayExePath);
        try
        {
            // All asked before any is waited for, so they quit side by side and the whole wait is at most `grace`.
            var asked = trays.Select(tray => tray.AskToQuit()).ToList();
            var clock = Stopwatch.StartNew();
            var quit = 0;
            var killed = 0;
            Win32Exception? failed = null;
            for (var i = 0; i < trays.Count; i++)
            {
                if (asked[i] && trays[i].WaitForExit(Remaining(grace, clock.Elapsed)))
                {
                    quit++;
                    continue;
                }

                // A tray that can't be ended keeps its files in use, but the others are still closed, so that as few
                // files as possible wait for a restart; the first error is reported once every tray has had its turn.
                try
                {
                    trays[i].Kill();
                    killed++;
                }
                catch (Win32Exception ex)
                {
                    failed ??= ex;
                }
            }

            if (failed is not null) throw failed;
            return new TrayStopResult(quit, killed);
        }
        finally
        {
            foreach (var tray in trays) tray.Dispose();
        }
    }

    static TimeSpan Remaining(TimeSpan grace, TimeSpan elapsed) => elapsed < grace ? grace - elapsed : TimeSpan.Zero;
}
