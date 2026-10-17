using Marquee.Core.Usb;

namespace Marquee.Core.Tests.TestSupport;

/// Records every bulk write. A reply, if the responder returns one, becomes readable
/// once, right after the write that produced it. That's how the real panel behaves.
sealed class FakeUsbTransport : IUsbTransport
{
    byte[]? _pending;

    public List<byte[]> Writes { get; } = [];
    public Func<byte[], byte[]?> Responder { get; set; } = _ => null;
    public Func<byte[], Exception?> WriteFault { get; set; } = _ => null;
    public bool Disposed { get; private set; }

    public void Write(byte[] data, int timeoutMs)
    {
        if (WriteFault(data) is { } fault) throw fault;
        Writes.Add(data.ToArray());
        _pending = Responder(data);
    }

    public int Read(byte[] buffer, int timeoutMs)
    {
        if (_pending is null) return 0;
        var count = Math.Min(buffer.Length, _pending.Length);
        Array.Copy(_pending, buffer, count);
        _pending = null;
        return count;
    }

    public void Dispose() => Disposed = true;
}
