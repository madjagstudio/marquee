using Marquee.Core.Usb;
using SkiaSharp;

namespace Marquee.Core.Devices.LedRing;

/// The 8.8" Universal Screen's ARGB LED ring (USB 0416:8050), a WinUSB device of its own behind the screen's hub.
/// It has no effects of its own: the host streams every frame, and the ring never replies. Like the screen, it is
/// single-threaded: only the ring's sender touches it. A failed write throws, and the sender reopens the ring.
/// Protocol facts from sgtaziz/lian-li-linux (MIT, crates/lianli-devices/src/winusb/led.rs); no code was copied.
public sealed class LedRing88Device(string devicePath, Func<string, IUsbTransport> openTransport) : IDisposable
{
    public const ushort VendorId = 0x0416, ProductId = 0x8050;

    // A 64-byte packet takes a millisecond; this only bounds a write to a ring that has stopped listening.
    const int WriteTimeoutMs = 1000;

    IUsbTransport? _usb;

    public void Open()
    {
        Close();
        try
        {
            _usb = openTransport(devicePath);
        }
        catch (DeviceBusyException)
        {
            // The transport's message names the screen, which is the wrong device here.
            throw new DeviceBusyException("The LED ring is in use by another program.");
        }
    }

    /// Sets every LED: up to 60 colors, in ring order; missing ones are black. Alpha is ignored.
    public void Send(ReadOnlySpan<SKColor> colors)
    {
        var usb = _usb ?? throw new InvalidOperationException("LED ring is not open.");
        foreach (var packet in LedRingPackets.Encode(colors))
            usb.Write(packet, WriteTimeoutMs);
    }

    public void Close()
    {
        _usb?.Dispose();
        _usb = null;
    }

    public void Dispose() => Close();
}
