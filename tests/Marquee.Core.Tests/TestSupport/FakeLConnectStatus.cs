using Marquee.Core.Devices;

namespace Marquee.Core.Tests.TestSupport;

/// Stands in for the Service Control Manager and process list. Counts queries so tests can see when L-Connect is asked.
sealed class FakeLConnectStatus(bool service = false, bool app = false) : ILConnectStatusSource
{
    public int Queries { get; private set; }

    public bool IsServiceRunning()
    {
        Queries++;
        return service;
    }

    public bool IsAppRunning()
    {
        Queries++;
        return app;
    }

    public static LConnectDetector Detector(bool service = false, bool app = false) => new(new FakeLConnectStatus(service, app));
}
