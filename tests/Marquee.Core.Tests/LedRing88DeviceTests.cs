using Marquee.Core.Devices;
using Marquee.Core.Devices.LedRing;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Usb;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class LedRing88DeviceTests
{
    const string RingPath = @"\\?\usb#vid_0416&pid_8050#9&1234abcd&0&3#{a5dcbf10-6530-11d2-901f-00c04fb951ed}";
    const string ScreenPath = @"\\?\usb#vid_1cbe&pid_a088#0123456789abcdef#{a5dcbf10-6530-11d2-901f-00c04fb951ed}";

    readonly FakeUsbTransport _usb = new();
    readonly List<string> _openedPaths = [];

    LedRing88Device CreateDevice() => new(RingPath, path =>
    {
        _openedPaths.Add(path);
        return _usb;
    });

    static SKColor[] Gradient() =>
        Enumerable.Range(0, LedRingPackets.LedCount).Select(i => new SKColor((byte)i, (byte)(2 * i), (byte)(3 * i))).ToArray();

    [Fact]
    public void Ids_are_the_rings_usb_vendor_and_product()
    {
        Assert.Equal(0x0416, LedRing88Device.VendorId);
        Assert.Equal(0x8050, LedRing88Device.ProductId);
    }

    [Fact]
    public void Open_opens_the_transport_for_the_device_path_and_writes_nothing()
    {
        using var device = CreateDevice();

        device.Open();

        Assert.Equal([RingPath], _openedPaths);
        Assert.Empty(_usb.Writes);
    }

    [Fact]
    public void Send_writes_the_three_packets_in_order()
    {
        using var device = CreateDevice();
        device.Open();
        var colors = Gradient();

        device.Send(colors);

        Assert.Equal(LedRingPackets.Encode(colors), _usb.Writes);
        Assert.Equal(new byte[] { 0, 20, 40 }, _usb.Writes.Select(w => w[1]).ToArray());
    }

    [Fact]
    public void Send_before_Open_throws()
    {
        using var device = CreateDevice();

        Assert.Throws<InvalidOperationException>(() => device.Send(Gradient()));
    }

    [Fact]
    public void Send_rejects_too_many_colors_before_writing()
    {
        using var device = CreateDevice();
        device.Open();

        Assert.Throws<ArgumentException>(() => device.Send(new SKColor[LedRingPackets.LedCount + 1]));

        Assert.Empty(_usb.Writes);
    }

    [Fact]
    public void A_failing_write_throws()
    {
        _usb.WriteFault = w => w[1] == 20 ? new IOException("unplugged") : null;
        using var device = CreateDevice();
        device.Open();

        Assert.Throws<IOException>(() => device.Send(Gradient()));
        Assert.Single(_usb.Writes);
    }

    [Fact]
    public void Open_while_another_program_holds_the_ring_says_so()
    {
        using var device = new LedRing88Device(RingPath, _ => throw new DeviceBusyException("The screen is in use by another program."));

        var ex = Assert.Throws<DeviceBusyException>(device.Open);

        Assert.Equal("The LED ring is in use by another program.", ex.Message);
    }

    [Fact]
    public void Second_Open_disposes_the_first_transport()
    {
        var transports = new List<FakeUsbTransport>();
        using var device = new LedRing88Device(RingPath, _ =>
        {
            var transport = new FakeUsbTransport();
            transports.Add(transport);
            return transport;
        });

        device.Open();
        device.Open();

        Assert.Equal(2, transports.Count);
        Assert.True(transports[0].Disposed);
        Assert.False(transports[1].Disposed);
    }

    [Fact]
    public void Close_disposes_the_transport_and_Send_then_throws()
    {
        using var device = CreateDevice();
        device.Open();

        device.Close();

        Assert.True(_usb.Disposed);
        Assert.Throws<InvalidOperationException>(() => device.Send(Gradient()));
    }

    [Fact]
    public void Dispose_closes()
    {
        var device = CreateDevice();
        device.Open();

        device.Dispose();

        Assert.True(_usb.Disposed);
    }

    [Fact]
    public void Find_returns_the_first_ring_without_opening_it()
    {
        var entries = new[]
        {
            UsbDeviceEntry.FromPath(ScreenPath)!,
            UsbDeviceEntry.FromPath(RingPath)!,
            new UsbDeviceEntry(LedRing88Device.VendorId, LedRing88Device.ProductId, "second ring"),
        };

        using var ring = LedRings.Find(entries, path =>
        {
            _openedPaths.Add(path);
            return _usb;
        });

        Assert.NotNull(ring);
        Assert.Empty(_openedPaths);
        ring.Open();
        Assert.Equal([RingPath], _openedPaths);
    }

    [Fact]
    public void Find_returns_null_without_a_ring() =>
        Assert.Null(LedRings.Find([UsbDeviceEntry.FromPath(ScreenPath)!], _ => throw new InvalidOperationException()));
}
