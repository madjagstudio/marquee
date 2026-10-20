using System.ServiceProcess;

namespace Marquee.Core.Devices;

/// Stops and starts Windows services, for the L-Connect handover. Tests pass a fake.
public interface IServiceControl
{
    /// The service's state, or null when it isn't installed.
    ServiceControllerStatus? Status(string name);

    /// Asks the service to stop and returns without waiting for it to.
    void Stop(string name);

    /// Asks the service to start and returns without waiting for it to.
    void Start(string name);

    /// Waits up to `timeout` for the service to reach `status`; false when it didn't.
    bool WaitFor(string name, ServiceControllerStatus status, TimeSpan timeout);
}
