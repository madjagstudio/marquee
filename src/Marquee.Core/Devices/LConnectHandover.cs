using System.Globalization;
using System.ServiceProcess;
using Marquee.Core.Logging;

namespace Marquee.Core.Devices;

/// How a handover ended.
public enum HandoverOutcome
{
    /// Marquee has the screen.
    Taken,

    /// L-Connect's service isn't installed or isn't running, so there was nothing to take the screen from.
    NotNeeded,

    /// Marquee didn't get the screen; the message says why.
    Failed,

    /// Marquee stopped wanting the screen (it paused or is quitting) before it had it. Not a failure: nothing went
    /// wrong, and L-Connect was left running or started again.
    Cancelled,
}

/// What a handover did. Message is a sentence or two for the log and the tray. Restarted names the services the
/// handover started again, and RestartFailed the ones it stopped but couldn't start again.
public sealed record HandoverResult(HandoverOutcome Outcome, string Message, IReadOnlyList<string> Restarted,
    IReadOnlyList<string> RestartFailed);

/// Takes the screen from L-Connect's background service and keeps L-Connect running the fans and RGB. Whoever opens
/// the screen first keeps it, and when L-Connect's service starts and finds the screen busy, it tries three times, then
/// skips the screen and its LED ring and runs everything else normally (the coexistence spec's experiment 1). So the
/// handover stops the watcher, which would otherwise restart the service, then the service; lets Marquee open the
/// screen and the ring; and then always starts the service and the watcher again. Stopping and starting them needs
/// admin rights, which the elevated engine has.
public sealed class LConnectHandover(IServiceControl services, ILog log, TimeSpan? stopTimeout = null)
{
    public const string ServiceName = SystemLConnectStatus.ServiceName;
    public const string WatcherName = "LConnectServiceWatcher";

    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(15);

    /// How long the engine gets to open the screen once the service has stopped. L-Connect's fans and RGB stand still
    /// until the service starts again, so this is kept short.
    public static readonly TimeSpan ScreenTimeout = TimeSpan.FromSeconds(15);

    const string RestartHint = "L-Connect didn't start again (see the log); start it from its tray icon's start-service item.";

    readonly TimeSpan _stopTimeout = stopTimeout ?? DefaultStopTimeout;

    /// `waitForScreen` is the engine opening the screen and the ring: it gets the time it may take and returns true once
    /// Marquee has the screen. Never throws: errors, including the ones from stopping the services without admin rights,
    /// come back as a Failed result, and whatever happened, every service the handover stopped is started again.
    /// `cancel` is Marquee no longer wanting the screen: set before anything was stopped, nothing is; set while the
    /// watcher stops, the service is left running and the watcher started again; set while `waitForScreen` waits (which
    /// then returns false), the result is Cancelled rather than Failed.
    public HandoverResult TakeScreen(Func<TimeSpan, bool> waitForScreen, CancellationToken cancel = default)
    {
        var stopped = new List<string>();
        var restarted = new List<string>();
        var restartFailed = new List<string>();
        var outcome = HandoverOutcome.Failed;
        string message;
        try
        {
            (outcome, message) = StopAndOpen(stopped, waitForScreen, cancel);
        }
        catch (Exception ex)
        {
            message = $"Couldn't take the screen from L-Connect: {ex.Message}";
            log.Error(message);
        }
        finally
        {
            // The service first, so the watcher finds it running, as L-Connect's own start-service item does.
            var taken = outcome == HandoverOutcome.Taken;
            StartAgain(ServiceName, taken, stopped, restarted, restartFailed);
            StartAgain(WatcherName, taken, stopped, restarted, restartFailed);
        }

        if (outcome == HandoverOutcome.Taken)
        {
            message = restartFailed.Count == 0
                ? "Marquee has the screen, and L-Connect is running your fans and RGB again."
                : $"Marquee has the screen, but {RestartHint}";
        }
        else if (restartFailed.Count > 0)
        {
            message = $"{message} {RestartHint}";
        }
        return new(outcome, message, restarted, restartFailed);
    }

    /// Everything up to Marquee having the screen. Adds each service it stopped to `stopped` as it goes, so the caller
    /// can start them again whatever this throws.
    (HandoverOutcome, string) StopAndOpen(List<string> stopped, Func<TimeSpan, bool> waitForScreen, CancellationToken cancel)
    {
        var service = services.Status(ServiceName);
        if (!IsRunning(service))
        {
            var message = service is null
                ? "L-Connect's service isn't installed, so there's no screen to take from it."
                : "L-Connect's service isn't running, so there's no screen to take from it.";
            log.Info(message);
            return (HandoverOutcome.NotNeeded, message);
        }

        if (cancel.IsCancellationRequested) return LeftRunning();

        if (IsRunning(services.Status(WatcherName)))
        {
            log.Info($"Stopping L-Connect's service watcher ({WatcherName}) so it doesn't restart the service while Marquee takes the screen.");
            services.Stop(WatcherName);
            stopped.Add(WatcherName);

            // A watcher still stopping may look once more, find the service stopped and restart it, which would take
            // the screen back during the wait below.
            if (!services.WaitFor(WatcherName, ServiceControllerStatus.Stopped, _stopTimeout))
            {
                var message = $"L-Connect's service watcher didn't stop within {Seconds(_stopTimeout)} s.";
                log.Warn(message);
                return (HandoverOutcome.Failed, message);
            }

            // A Pause or Quit during that wait (up to _stopTimeout) leaves the service alone; the caller starts the
            // watcher again.
            if (cancel.IsCancellationRequested) return LeftRunning();
        }

        log.Info($"Stopping L-Connect's service ({ServiceName}) so Marquee can open the screen.");
        services.Stop(ServiceName);
        stopped.Add(ServiceName);

        if (!services.WaitFor(ServiceName, ServiceControllerStatus.Stopped, _stopTimeout))
        {
            var message = $"L-Connect's service didn't stop within {Seconds(_stopTimeout)} s.";
            log.Warn(message);
            return (HandoverOutcome.Failed, message);
        }

        log.Info("L-Connect's service has stopped; opening the screen.");
        if (!waitForScreen(ScreenTimeout))
        {
            if (cancel.IsCancellationRequested)
            {
                const string stoppedWaiting = "Stopped waiting for the screen (Marquee paused or is quitting); starting L-Connect again.";
                log.Info(stoppedWaiting);
                return (HandoverOutcome.Cancelled, stoppedWaiting);
            }

            var message = $"Marquee couldn't open the screen within {Seconds(ScreenTimeout)} s of L-Connect's service stopping.";
            log.Warn(message);
            return (HandoverOutcome.Failed, message);
        }

        log.Info("Marquee has the screen.");
        return (HandoverOutcome.Taken, "");
    }

    /// Marquee no longer wants the screen before the service was stopped: the screen won't open now, so stopping
    /// L-Connect's service would only pause its fans and RGB for nothing.
    (HandoverOutcome, string) LeftRunning()
    {
        const string message = "Marquee paused or is quitting, so L-Connect's service was left running.";
        log.Info(message);
        return (HandoverOutcome.Cancelled, message);
    }

    /// Starts a service the handover stopped. Each start is tried on its own, so a service that won't start doesn't
    /// keep its watcher from starting, and the watcher then restarts the service itself.
    void StartAgain(string name, bool taken, List<string> stopped, List<string> restarted, List<string> restartFailed)
    {
        if (!stopped.Contains(name)) return;

        var what = name == ServiceName ? "L-Connect's service" : "L-Connect's service watcher";
        try
        {
            var status = services.Status(name);
            if (status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                // The L-Connect 3 app starts the service itself when it can't reach it.
                log.Info($"{what} is already running again.");
                return;
            }

            // A service can't start while it's still stopping, so a slow stop gets one more wait.
            if (status == ServiceControllerStatus.StopPending)
                services.WaitFor(name, ServiceControllerStatus.Stopped, _stopTimeout);

            log.Info(name == ServiceName && taken
                ? "Starting L-Connect's service again. It skips the screen and keeps running your fans and RGB."
                : $"Starting {what} again.");
            services.Start(name);
            restarted.Add(name);
        }
        catch (Exception ex)
        {
            log.Error($"Couldn't start {what} again: {ex.Message}");
            restartFailed.Add(name);
        }
    }

    /// Stopping counts as not running: the service is already letting go of the screen, and a second stop would fail.
    static bool IsRunning(ServiceControllerStatus? status) =>
        status is not (null or ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending);

    static string Seconds(TimeSpan timeout) => timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
}
