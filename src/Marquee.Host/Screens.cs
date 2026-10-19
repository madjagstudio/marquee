using Marquee.Core.Devices;
using Marquee.Core.Usb;

namespace Marquee.Host;

/// Finding the screen, for show-image, run and the tray.
public static class Screens
{
    public static Orientation? ParseOrientation(string? value) =>
        OrientationNames.TryParse(value, out var orientation) ? orientation : null;

    /// The first supported screen that is plugged in. Nothing is opened yet.
    public static IDisplayDevice? Find(Orientation orientation) =>
        WinUsbTransport.Enumerate()
            .Where(e => DeviceRegistry.IsSupported(e.VendorId, e.ProductId))
            .Select(e => DeviceRegistry.Create(e, WinUsbTransport.Open, orientation))
            .FirstOrDefault();
}
