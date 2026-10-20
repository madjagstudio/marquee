using Marquee.Core.Sensors;

namespace Marquee.Host.Tests.TestSupport;

/// A provider that writes nothing and remembers whether it was disposed. While Fault is set, each poll throws it, as a
/// provider whose source is missing does.
sealed class FakeProvider(string name) : ISensorProvider
{
    volatile bool _disposed;

    public string Name => name;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public bool Disposed => _disposed;

    public volatile Exception? Fault;

    public void Poll(SensorStore store)
    {
        if (Fault is { } fault) throw fault;
    }

    public void Dispose() => _disposed = true;
}
