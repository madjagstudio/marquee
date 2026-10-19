using Marquee.Core.Devices.LedRing;
using SkiaSharp;

namespace Marquee.Host.Tests.TestSupport;

/// An LED ring that keeps every frame sent to it and can be told to fail. The ring sender's thread drives it while the
/// test thread reads it, so everything is under one lock.
sealed class FakeRing : ILedRing
{
    readonly object _gate = new();
    readonly List<SKColor[]> _frames = [];
    int _opens;
    int _closes;
    bool _open;

    /// Thrown by Open while set, as a ring another program holds would.
    public Exception? OpenFault { get; set; }

    /// Thrown by Send while set, as an unplugged ring would.
    public Exception? SendFault { get; set; }

    public int Opens
    {
        get
        {
            lock (_gate) return _opens;
        }
    }

    public int Closes
    {
        get
        {
            lock (_gate) return _closes;
        }
    }

    public bool IsOpen
    {
        get
        {
            lock (_gate) return _open;
        }
    }

    public IReadOnlyList<SKColor[]> Frames
    {
        get
        {
            lock (_gate) return _frames.ToList();
        }
    }

    public int FrameCount
    {
        get
        {
            lock (_gate) return _frames.Count;
        }
    }

    /// The last frame sent, or null before any.
    public SKColor[]? LastFrame
    {
        get
        {
            lock (_gate) return _frames.Count > 0 ? _frames[^1] : null;
        }
    }

    public void Open()
    {
        lock (_gate)
        {
            _opens++;
            if (OpenFault is { } fault) throw fault;
            _open = true;
        }
    }

    public void Send(ReadOnlySpan<SKColor> colors)
    {
        lock (_gate)
        {
            if (!_open) throw new InvalidOperationException("LED ring is not open.");
            if (SendFault is { } fault) throw fault;
            _frames.Add(colors.ToArray());
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closes++;
            _open = false;
        }
    }

    /// Whether `frame` is all off. The ring ignores alpha, so only red, green and blue count.
    public static bool IsBlack(SKColor[]? frame) =>
        frame is not null && frame.All(c => c.Red == 0 && c.Green == 0 && c.Blue == 0);
}
