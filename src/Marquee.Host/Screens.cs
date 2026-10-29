using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Usb;

namespace Marquee.Host;

/// Finding the screen, for show-image, run and the tray.
public static class Screens
{
    public static Orientation? ParseOrientation(string? value) =>
        OrientationNames.TryParse(value, out var orientation) ? orientation : null;

    /// The first supported screen that is plugged in. Nothing is opened yet. `log` gets what the screen reports while
    /// it runs.
    public static IDisplayDevice? Find(Orientation orientation, ILog? log = null) =>
        WinUsbTransport.Enumerate()
            .Where(e => DeviceRegistry.IsSupported(e.VendorId, e.ProductId))
            .Select(e => DeviceRegistry.Create(e, WinUsbTransport.Open, orientation, log))
            .FirstOrDefault();
}
