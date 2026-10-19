using Marquee.Core.Usb;

namespace Marquee.Core.Devices.LedRing;

/// Finding the Universal Screen's LED ring. It isn't a display, so DeviceRegistry doesn't list it.
public static class LedRings
{
    /// The first LED ring that is plugged in. Nothing is opened yet.
    public static LedRing88Device? Find() => Find(WinUsbTransport.Enumerate(), WinUsbTransport.Open);

    internal static LedRing88Device? Find(IEnumerable<UsbDeviceEntry> entries, Func<string, IUsbTransport> openTransport) =>
        entries.FirstOrDefault(e => (e.VendorId, e.ProductId) is (LedRing88Device.VendorId, LedRing88Device.ProductId)) is { } ring
            ? new LedRing88Device(ring.Path, openTransport)
            : null;
}
