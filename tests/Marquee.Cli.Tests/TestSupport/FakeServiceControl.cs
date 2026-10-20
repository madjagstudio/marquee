using System.ServiceProcess;
using Marquee.Core.Devices;

namespace Marquee.Cli.Tests.TestSupport;

/// Stands in for the Service Control Manager: L-Connect's service and watcher, both running, and every stop, start and
/// wait in Calls, in order. Stopped runs after each stop, so a test can play the Marquee that then opens the screen.
sealed class FakeServiceControl : IServiceControl
{
    public const string Service = "LConnectService";
    public const string Watcher = "LConnectServiceWatcher";

    readonly Dictionary<string, ServiceControllerStatus> _status = new()
    {
        [Service] = ServiceControllerStatus.Running,
        [Watcher] = ServiceControllerStatus.Running,
    };

    /// "stop X", "start X", "wait X Stopped".
    public List<string> Calls { get; } = [];

    /// Called with each service's name once it has stopped.
    public Action<string> Stopped { get; set; } = _ => { };

    /// Stop throws, as it does without admin rights, and the service keeps running.
    public bool FailToStop { get; set; }

    public ServiceControllerStatus? Status(string name) => _status.TryGetValue(name, out var status) ? status : null;

    public void SetStatus(string name, ServiceControllerStatus status) => _status[name] = status;

    public void Stop(string name)
    {
        Calls.Add($"stop {name}");
        if (FailToStop) throw new InvalidOperationException($"Cannot open {name} service on computer '.'.");
        _status[name] = ServiceControllerStatus.Stopped;
        Stopped(name);
    }

    public void Start(string name)
    {
        Calls.Add($"start {name}");
        _status[name] = ServiceControllerStatus.Running;
    }

    public bool WaitFor(string name, ServiceControllerStatus status, TimeSpan timeout)
    {
        Calls.Add($"wait {name} {status}");
        return Status(name) == status;
    }
}
