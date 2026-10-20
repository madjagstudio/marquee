using System.ComponentModel;

namespace Marquee.Host.Tests.TestSupport;

/// A running tray as TrayStopper sees it: whether it has a quit event, whether it quits when asked, and what was done to
/// it. It "waits" by recording the time it was given, so no test sleeps.
sealed class FakeTray(int processId, bool hasQuitEvent, bool quitsWhenAsked, bool killFails = false) : ITray
{
    public int ProcessId => processId;

    public bool Asked { get; private set; }

    public TimeSpan Waited { get; private set; }

    public bool Killed { get; private set; }

    public bool Disposed { get; private set; }

    public bool AskToQuit()
    {
        Asked = hasQuitEvent;
        return hasQuitEvent;
    }

    public bool WaitForExit(TimeSpan timeout)
    {
        Waited = timeout;
        return Asked && quitsWhenAsked;
    }

    public void Kill()
    {
        if (killFails) throw new Win32Exception(5);
        Killed = true;
    }

    public void Dispose() => Disposed = true;
}

/// The trays FakeTray stands in for, whatever path is asked about; it remembers the path.
sealed class FakeTrayProcesses(params FakeTray[] trays) : ITrayProcesses
{
    public string? AskedFor { get; private set; }

    public IReadOnlyList<ITray> Running(string trayExePath)
    {
        AskedFor = trayExePath;
        return trays;
    }
}
