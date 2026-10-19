namespace Marquee.Host.Tests.TestSupport;

/// Stands in for the status-line pipe or the HTTP server: remembers what it was started with and whether it stopped.
sealed class FakeServer(string kind, int port = 0, int maxKeys = 0) : IDisposable
{
    volatile bool _disposed;

    public string Kind => kind;
    public int Port => port;
    public int MaxKeys => maxKeys;
    public bool Disposed => _disposed;

    public void Dispose() => _disposed = true;
}
