using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace Marquee.Core.Devices;

/// Which part of L-Connect 3, if any, is running. Its background service holds the screen until the service stops;
/// closing the app's window doesn't stop the service.
public enum LConnectState
{
    NotRunning,
    AppOnly,
    Service,
}

/// Where LConnectDetector reads L-Connect's state from. Tests pass a fake.
public interface ILConnectStatusSource
{
    bool IsServiceRunning();
    bool IsAppRunning();
}

/// Reads the service state from the Service Control Manager (no admin rights needed) and the app from its process.
public sealed class SystemLConnectStatus : ILConnectStatusSource
{
    const string ServiceName = "LConnectService";
    const string ServiceProcessName = "L-Connect-Service";
    const string AppProcessName = "L-Connect 3";

    public bool IsServiceRunning()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            // Stopping means the screen is about to be released, so only a fully stopped service counts as gone.
            return service.Status != ServiceControllerStatus.Stopped;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Not installed, or the SCM couldn't be asked: fall back to the service's process.
            return IsProcessRunning(ServiceProcessName);
        }
    }

    public bool IsAppRunning() => IsProcessRunning(AppProcessName);

    static bool IsProcessRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }
}

/// Explains a DeviceBusyException: says who holds the screen and how to free it.
public sealed class LConnectDetector(ILConnectStatusSource source)
{
    public static LConnectDetector Default { get; } = new(new SystemLConnectStatus());

    public LConnectState Detect() =>
        source.IsServiceRunning() ? LConnectState.Service
        : source.IsAppRunning() ? LConnectState.AppOnly
        : LConnectState.NotRunning;

    /// One sentence or more for the render loop's single log line, or "" when L-Connect isn't running.
    public static string LogHint(LConnectState state) => state switch
    {
        LConnectState.Service =>
            "L-Connect's background service is using it, and closing the L-Connect 3 window doesn't free it. " +
            "To free it, right-click L-Connect's tray icon and choose its stop-service option, then start the service " +
            "again from the same menu once Marquee has the screen; L-Connect then skips the screen and keeps running " +
            "your fans and RGB.",
        LConnectState.AppOnly => "L-Connect 3 is running; quit it from its tray icon.",
        _ => "",
    };

    /// Lines for show-image's stderr, or none when L-Connect isn't running.
    public static IReadOnlyList<string> ConsoleHint(LConnectState state) => state switch
    {
        LConnectState.Service =>
        [
            "L-Connect's background service (LConnectService) is using the screen. Closing the L-Connect 3 window doesn't free it.",
            "To free it, right-click L-Connect's tray icon and choose its stop-service option, then try again.",
            "Start the service again from the same menu afterwards. If Marquee has the screen then (`marquee run`),",
            "L-Connect skips the screen and keeps running your fans and RGB.",
        ],
        LConnectState.AppOnly => ["L-Connect 3 is running. Quit it from its tray icon, then try again."],
        _ => [],
    };
}
