using System.Buffers.Binary;
using System.Text;
using Marquee.Core.Devices;
using Marquee.Core.Devices.UniversalScreen88;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class UniversalScreen88DeviceTests
{
    static readonly DateTime FixedNow = new(2026, 10, 16, 18, 30, 5);

    readonly FakeUsbTransport _usb = new();

    UniversalScreen88Device CreateDevice() =>
        new(() => _usb, Orientation.Landscape, new HeaderBuilder(() => 1), () => FixedNow);

    static byte Cmd(byte[] write) => Packets.DecryptHeader(write)[0];
    static byte[] Params(byte[] write) => Packets.DecryptHeader(write)[8..];
    static byte[] Payload(byte[] write) => write[512..];

    static byte[] GetVerReply(string firmware, byte status = 0xC8)
    {
        var reply = new byte[512];
        reply[0] = 0x0A;
        reply[1] = status;
        Encoding.ASCII.GetBytes(firmware).CopyTo(reply, 8);
        return reply;
    }

    [Fact]
    public void Open_runs_init_sequence_in_order()
    {
        using var device = CreateDevice();

        device.Open();

        Assert.Equal(
            new byte[] { 0x7B, 0x0A, 0x0F, 0x0E, 0x33, 0x34, 0x66, 0x65 },
            _usb.Writes.Select(Cmd).ToArray());
    }

    [Fact]
    public void Open_sends_frame_rate_brightness_and_local_clock()
    {
        using var device = CreateDevice();

        device.Open();

        Assert.Equal(120, Params(_usb.Writes[2])[0]);
        Assert.Equal(100, Params(_usb.Writes[3])[0]);
        Assert.Equal(new byte[] { 0x07, 0xEA, 10, 16, 18, 30, 5, 2 }, Params(_usb.Writes[4])[..8]);
    }

    [Fact]
    public void Open_reads_firmware_version_from_GetVer_reply()
    {
        _usb.Responder = write => Cmd(write) == 0x0A ? GetVerReply("US88-1.0") : null;
        using var device = CreateDevice();

        device.Open();

        Assert.Equal("US88-1.0", device.FirmwareVersion);
    }

    [Fact]
    public void Open_ignores_GetVer_reply_that_is_not_ok()
    {
        _usb.Responder = write => Cmd(write) == 0x0A ? GetVerReply("US88-1.0", status: 0x00) : null;
        using var device = CreateDevice();

        device.Open();

        Assert.Null(device.FirmwareVersion);
    }

    [Fact]
    public void Second_Open_disposes_first_transport()
    {
        var transports = new List<FakeUsbTransport>();
        using var device = new UniversalScreen88Device(
            () =>
            {
                var transport = new FakeUsbTransport();
                transports.Add(transport);
                return transport;
            },
            Orientation.Landscape, new HeaderBuilder(() => 1), () => FixedNow);

        device.Open();
        device.Open();

        Assert.Equal(2, transports.Count);
        Assert.True(transports[0].Disposed);
        Assert.False(transports[1].Disposed);
    }

    [Fact]
    public void Failed_Open_disposes_transport_and_leaves_device_closed()
    {
        _usb.WriteFault = _ => new IOException("boom");
        using var device = CreateDevice();

        Assert.Throws<IOException>(() => device.Open());

        Assert.True(_usb.Disposed);
        Assert.Throws<InvalidOperationException>(() => device.PushFrame(Images.Landscape(SKColors.Red)));
    }

    [Fact]
    public void Open_clears_both_layers_with_full_panel_images()
    {
        using var device = CreateDevice();

        device.Open();

        foreach (var write in _usb.Writes.TakeLast(2))
        {
            var declared = BinaryPrimitives.ReadUInt32BigEndian(Params(write));
            Assert.Equal((uint)Payload(write).Length, declared);
            using var image = SKImage.FromEncodedData(Payload(write));
            Assert.Equal(480, image.Width);
            Assert.Equal(1920, image.Height);
        }
    }

    [Fact]
    public void PushFrame_sends_header_and_portrait_jpeg_in_one_write()
    {
        using var device = CreateDevice();
        device.Open();
        var before = _usb.Writes.Count;

        device.PushFrame(Images.Landscape(SKColors.Red));

        Assert.Equal(before + 1, _usb.Writes.Count);
        var write = _usb.Writes[^1];
        Assert.Equal(0x65, Cmd(write));
        Assert.Equal((uint)Payload(write).Length, BinaryPrimitives.ReadUInt32BigEndian(Params(write)));
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, Payload(write)[..2]);
        using var image = SKImage.FromEncodedData(Payload(write));
        Assert.Equal(480, image.Width);
        Assert.Equal(1920, image.Height);
    }

    [Fact]
    public void PushFrame_in_portrait_sends_unrotated_panel_jpeg()
    {
        using var device = new UniversalScreen88Device(() => _usb, Orientation.Portrait, new HeaderBuilder(() => 1), () => FixedNow);
        device.Open();

        device.PushFrame(Images.Portrait(SKColors.Red));

        using var image = SKImage.FromEncodedData(Payload(_usb.Writes[^1]));
        var pixel = Images.PixelAt(image, 50, 50);
        Assert.True(pixel.Red > 200 && pixel.Green < 50 && pixel.Blue < 50);
    }

    [Fact]
    public void SetBrightness_clamps_to_100()
    {
        using var device = CreateDevice();
        device.Open();

        device.SetBrightness(150);

        Assert.Equal(0x0E, Cmd(_usb.Writes[^1]));
        Assert.Equal(100, Params(_usb.Writes[^1])[0]);
    }

    [Fact]
    public void PushFrame_before_Open_throws()
    {
        using var device = CreateDevice();

        Assert.Throws<InvalidOperationException>(() => device.PushFrame(Images.Landscape(SKColors.Red)));
    }

    [Fact]
    public void Close_disposes_transport()
    {
        var device = CreateDevice();
        device.Open();

        device.Close();

        Assert.True(_usb.Disposed);
    }

    [Theory]
    [InlineData(Orientation.Portrait, 480, 1920)]
    [InlineData(Orientation.Landscape, 1920, 480)]
    public void Info_reports_canvas_for_orientation(Orientation orientation, int width, int height)
    {
        using var device = new UniversalScreen88Device(() => _usb, orientation, new HeaderBuilder(() => 1), () => FixedNow);

        Assert.Equal(width, device.Info.Width);
        Assert.Equal(height, device.Info.Height);
    }
}
