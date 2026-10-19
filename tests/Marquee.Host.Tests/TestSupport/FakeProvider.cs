using Marquee.Core.Sensors;

namespace Marquee.Host.Tests.TestSupport;

/// A provider that writes nothing and remembers whether it was disposed.
sealed class FakeProvider(string name) : ISensorProvider
{
    volatile bool _disposed;

    public string Name => name;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public bool Disposed => _disposed;

    public void Poll(SensorStore store) { }

    public void Dispose() => _disposed = true;
}
