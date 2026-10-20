using System.ComponentModel;
using System.ServiceProcess;

namespace Marquee.Core.Devices;

/// IServiceControl over the Service Control Manager. Any user can read a service's state, but L-Connect's services run
/// as LocalSystem, so stopping and starting them needs admin rights: without them Stop and Start throw, and the
/// handover reports what they threw.
public sealed class WindowsServiceControl : IServiceControl
{
    const int ErrorServiceDoesNotExist = 1060;

    public ServiceControllerStatus? Status(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            return service.Status;
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: ErrorServiceDoesNotExist })
        {
            // Any other failure (no access to the SCM, say) isn't "not installed", so it stays an exception.
            return null;
        }
    }

    /// Only the service itself. Stop() would stop every service that depends on it too, waiting up to 30 s for each
    /// and past any cancel, and the handover starts only what it stopped. With a dependent running, the stop fails
    /// instead, and the handover reports it.
    public void Stop(string name)
    {
        using var service = new ServiceController(name);
        service.Stop(stopDependentServices: false);
    }

    public void Start(string name)
    {
        using var service = new ServiceController(name);
        service.Start();
    }

    public bool WaitFor(string name, ServiceControllerStatus status, TimeSpan timeout)
    {
        using var service = new ServiceController(name);
        try
        {
            service.WaitForStatus(status, timeout);
            return true;
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return false;
        }
    }
}
