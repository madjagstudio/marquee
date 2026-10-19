using System.ServiceProcess;
using Marquee.Core.Devices;

namespace Marquee.Host.Tests.TestSupport;

/// Stands in for the Service Control Manager: L-Connect's service and watcher, running unless a test says otherwise,
/// and every stop, start and wait in Calls, in order. The handover's thread changes them while the render loop's
/// sending thread reads them (through LConnect), so everything is under one lock.
sealed class FakeServiceControl : IServiceControl
{
    public const string Service = "LConnectService";
    public const string Watcher = "LConnectServiceWatcher";

    readonly object _gate = new();
    readonly Dictionary<string, ServiceControllerStatus> _status = new()
    {
        [Service] = ServiceControllerStatus.Running,
        [Watcher] = ServiceControllerStatus.Running,
    };
    readonly List<string> _calls = [];

    /// Stop throws, as it does without admin rights, and the service keeps running.
    public bool FailToStop { get; set; }

    /// "stop X", "start X", "wait X Stopped".
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate) return _calls.ToList();
        }
    }

    /// Says who holds the screen from these services, as the real detector does from the real ones: L-Connect's
    /// service while it isn't stopped, else nothing.
    public LConnectDetector Detector => new(new StatusSource(this));

    public bool IsServiceRunning => Status(Service) is not (null or ServiceControllerStatus.Stopped);

    public ServiceControllerStatus? Status(string name)
    {
        lock (_gate) return _status.TryGetValue(name, out var status) ? status : null;
    }

    public void SetStatus(string name, ServiceControllerStatus status)
    {
        lock (_gate) _status[name] = status;
    }

    public void Stop(string name)
    {
        lock (_gate)
        {
            _calls.Add($"stop {name}");
            if (FailToStop) throw new InvalidOperationException($"Cannot open {name} service on computer '.'.");
            _status[name] = ServiceControllerStatus.Stopped;
        }
    }

    public void Start(string name)
    {
        lock (_gate)
        {
            _calls.Add($"start {name}");
            _status[name] = ServiceControllerStatus.Running;
        }
    }

    public bool WaitFor(string name, ServiceControllerStatus status, TimeSpan timeout)
    {
        lock (_gate)
        {
            _calls.Add($"wait {name} {status}");
            return _status.GetValueOrDefault(name) == status;
        }
    }

    sealed class StatusSource(FakeServiceControl services) : ILConnectStatusSource
    {
        public bool IsServiceRunning() => services.IsServiceRunning;
        public bool IsAppRunning() => false;
    }
}
