using Marquee.Core.Sensors;

namespace Marquee.Host.Tests.TestSupport;

/// Stands in for the status-line pipe, the HTTP server or the sensor feed: remembers what it was started with and
/// whether it stopped.
sealed class FakeServer(string kind, int port = 0, int maxKeys = 0, SensorStore? store = null) : IDisposable
{
    volatile bool _disposed;

    public string Kind => kind;
    public int Port => port;
    public int MaxKeys => maxKeys;
    public SensorStore? Store => store;
    public bool Disposed => _disposed;

    public void Dispose() => _disposed = true;
}
