using System.Diagnostics;

namespace Marquee.Core.Devices;

/// L-Connect 3 holds the screen while it runs. Used to explain DeviceBusyException.
public static class LConnectDetector
{
    static readonly string[] ProcessNames = ["L-Connect 3", "L-Connect-Service"];

    public static bool IsRunning() => ProcessNames.Any(name =>
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    });
}
