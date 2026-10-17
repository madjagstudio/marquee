using Marquee.Core.Devices;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Usb;

namespace Marquee.Core.Tests;

public class DeviceRegistryTests
{
    const string ScreenPath = @"\\?\usb#vid_1cbe&pid_a088#3d37947a848e0703#{a5dcbf10-6530-11d2-901f-00c04fb951ed}";

    [Fact]
    public void FromPath_parses_vid_and_pid()
    {
        var entry = UsbDeviceEntry.FromPath(ScreenPath);

        Assert.NotNull(entry);
        Assert.Equal(0x1CBE, entry.VendorId);
        Assert.Equal(0xA088, entry.ProductId);
        Assert.Equal(ScreenPath, entry.Path);
    }

    [Fact]
    public void FromPath_returns_null_without_ids() => Assert.Null(UsbDeviceEntry.FromPath(@"\\?\hid#something"));

    [Fact]
    public void Universal_screen_is_supported_other_lian_li_panels_are_not_yet()
    {
        Assert.True(DeviceRegistry.IsSupported(0x1CBE, 0xA088));
        Assert.False(DeviceRegistry.IsSupported(0x0416, 0x8050));
    }

    [Fact]
    public void Create_builds_device_without_opening_usb()
    {
        var opened = false;

        var device = DeviceRegistry.Create(
            UsbDeviceEntry.FromPath(ScreenPath)!,
            _ => { opened = true; throw new InvalidOperationException(); },
            Rotation.Clockwise);

        Assert.IsType<UniversalScreen88Device>(device);
        Assert.False(opened);
    }

    [Fact]
    public void Create_rejects_unsupported_device() =>
        Assert.Throws<NotSupportedException>(() => DeviceRegistry.Create(
            new UsbDeviceEntry(0x0416, 0x8050, "x"), _ => throw new InvalidOperationException(), Rotation.Clockwise));
}
