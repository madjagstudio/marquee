using System.ServiceProcess;
using Marquee.Core.Devices;

namespace Marquee.Core.Tests.TestSupport;

/// Stands in for the Service Control Manager. Holds the given services in the given states (a name not given isn't
/// installed) and records every stop, start and wait in Calls, in order, so tests can check the handover's sequence.
/// Status queries aren't recorded: they change nothing.
sealed class FakeServiceControl(params (string Name, ServiceControllerStatus Status)[] services) : IServiceControl
{
    readonly Dictionary<string, ServiceControllerStatus> _status = services.ToDictionary(s => s.Name, s => s.Status);

    /// "stop X", "start X", "wait X Stopped", and whatever the test's screen callback adds.
    public List<string> Calls { get; } = [];

    /// The timeout of every wait, in order.
    public List<TimeSpan> WaitTimeouts { get; } = [];

    /// Stop throws, as it does without admin rights, and the service keeps running.
    public HashSet<string> FailToStop { get; } = [];

    /// Stop leaves the service stopping; it finishes only after the first wait has timed out.
    public HashSet<string> StopLate { get; } = [];

    /// Stop leaves the service stopping for good.
    public HashSet<string> NeverStop { get; } = [];

    /// Start throws and the service stays stopped.
    public HashSet<string> FailToStart { get; } = [];

    public ServiceControllerStatus? Status(string name) => _status.TryGetValue(name, out var status) ? status : null;

    public void SetStatus(string name, ServiceControllerStatus status) => _status[name] = status;

    public void Stop(string name)
    {
        Calls.Add($"stop {name}");
        if (FailToStop.Contains(name)) throw new InvalidOperationException($"Cannot open {name} service on computer '.'.");
        if (Status(name) is not ServiceControllerStatus.Running)
            throw new InvalidOperationException($"The {name} service isn't running.");
        _status[name] = StopLate.Contains(name) || NeverStop.Contains(name)
            ? ServiceControllerStatus.StopPending
            : ServiceControllerStatus.Stopped;
    }

    public void Start(string name)
    {
        Calls.Add($"start {name}");
        if (FailToStart.Contains(name)) throw new InvalidOperationException($"Cannot start service {name} on computer '.'.");
        // Like the real SCM: only a fully stopped service can start.
        if (Status(name) is not ServiceControllerStatus.Stopped)
            throw new InvalidOperationException($"An instance of the {name} service is already running.");
        _status[name] = ServiceControllerStatus.Running;
    }

    public bool WaitFor(string name, ServiceControllerStatus status, TimeSpan timeout)
    {
        Calls.Add($"wait {name} {status}");
        WaitTimeouts.Add(timeout);
        if (Status(name) == status) return true;
        if (status == ServiceControllerStatus.Stopped && StopLate.Remove(name))
        {
            // Too late for this wait, done by the next look.
            _status[name] = ServiceControllerStatus.Stopped;
        }
        return false;
    }
}
