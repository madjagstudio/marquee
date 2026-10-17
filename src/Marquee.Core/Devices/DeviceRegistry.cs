using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Usb;

namespace Marquee.Core.Devices;

/// Maps USB vendor/product IDs to device implementations. New panels are added here.
public static class DeviceRegistry
{
    public static bool IsSupported(ushort vendorId, ushort productId) =>
        (vendorId, productId) is (UniversalScreen88Device.VendorId, UniversalScreen88Device.ProductId);

    public static IDisplayDevice Create(UsbDeviceEntry entry, Func<string, IUsbTransport> openTransport, Rotation rotation) =>
        IsSupported(entry.VendorId, entry.ProductId)
            ? new UniversalScreen88Device(() => openTransport(entry.Path), rotation)
            : throw new NotSupportedException($"No driver for USB {entry.VendorId:X4}:{entry.ProductId:X4}.");
}
