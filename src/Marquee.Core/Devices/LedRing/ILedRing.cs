using SkiaSharp;

namespace Marquee.Core.Devices.LedRing;

/// An LED ring as the engine's ring sender drives it, so tests can stand in a fake for LedRing88Device. Like a
/// screen, it is single-threaded: one thread at a time opens it, sends to it and closes it.
public interface ILedRing
{
    void Open();

    /// Sets every LED: up to 60 colors, in ring order; missing ones are black. Alpha is ignored. Throws when the ring
    /// has gone away.
    void Send(ReadOnlySpan<SKColor> colors);

    /// Lets the ring go. Open may follow.
    void Close();
}
