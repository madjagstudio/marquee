using Marquee.Core.Devices;
using Marquee.Core.Usb;

/// Command-line helpers shared by show-image and run.
static class Screens
{
    public static Orientation? ParseOrientation(string? value) => value switch
    {
        "portrait" => Orientation.Portrait,
        "portrait-flipped" => Orientation.PortraitFlipped,
        "landscape" => Orientation.Landscape,
        "landscape-flipped" => Orientation.LandscapeFlipped,
        _ => null,
    };

    /// The first supported screen that is plugged in. Nothing is opened yet.
    public static IDisplayDevice? Find(Orientation orientation) =>
        WinUsbTransport.Enumerate()
            .Where(e => DeviceRegistry.IsSupported(e.VendorId, e.ProductId))
            .Select(e => DeviceRegistry.Create(e, WinUsbTransport.Open, orientation))
            .FirstOrDefault();
}
