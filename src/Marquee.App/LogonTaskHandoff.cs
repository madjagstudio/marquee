using System.ComponentModel;
using System.Runtime.InteropServices;
using Marquee.Core.Processes;
using Marquee.Host;

namespace Marquee.App;

/// Starts an administrator's tray through the logon task, with the same rights it gets at logon.
static class LogonTaskHandoff
{
    /// The task must run with highest privileges, or the tray it starts would hand off again. It must start this exe,
    /// so a development build never starts the installed tray instead.
    public static bool ShouldHandOff(int elevationType, LogonTaskStatus? task, string thisExePath)
    {
        if (elevationType != ProcessElevation.TokenElevationTypeLimited
            || task is not { Installed: true, Enabled: true, StartsOnDemand: true, RunsHighest: true }
            || string.IsNullOrWhiteSpace(task.TrayExePath) || string.IsNullOrWhiteSpace(thisExePath)) return false;

        try
        {
            return string.Equals(Path.GetFullPath(task.TrayExePath), Path.GetFullPath(thisExePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// The task's status, or null when it can't be queried; the tray runs as it is.
    public static LogonTaskStatus? Task(ISchTasks schtasks)
    {
        try
        {
            return LogonTask.Status(schtasks);
        }
        catch (Exception ex) when (ex is LogonTaskException or TimeoutException or Win32Exception)
        {
            return null;
        }
    }

    /// Starts the task in this tray's session, or returns Task Scheduler's reason so the tray can log it and carry on.
    public static bool TryStart(ITaskRunner runner, int sessionId, out string? failure)
    {
        try
        {
            runner.RunInSession(LogonTask.TaskName, sessionId);
            failure = null;
            return true;
        }
        catch (COMException ex)
        {
            failure = ex.Message;
            return false;
        }
    }
}
