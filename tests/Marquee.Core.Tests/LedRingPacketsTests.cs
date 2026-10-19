using Marquee.Core.Devices.LedRing;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class LedRingPacketsTests
{
    static SKColor[] Ring(Func<int, SKColor> color) => Enumerable.Range(0, LedRingPackets.LedCount).Select(color).ToArray();

    [Fact]
    public void Encode_makes_three_64_byte_packets_headed_by_command_and_first_led()
    {
        var packets = LedRingPackets.Encode(Ring(_ => SKColors.White));

        Assert.Equal(3, packets.Length);
        Assert.All(packets, p => Assert.Equal(64, p.Length));
        Assert.Equal(new byte[] { 0x11, 0, 0, 0 }, packets[0][..4]);
        Assert.Equal(new byte[] { 0x11, 20, 0, 0 }, packets[1][..4]);
        Assert.Equal(new byte[] { 0x11, 40, 0, 0 }, packets[2][..4]);
    }

    [Fact]
    public void Encode_writes_each_led_as_red_green_blue_from_byte_4()
    {
        var packets = LedRingPackets.Encode(Ring(i => new SKColor((byte)i, (byte)(100 + i), (byte)(200 - i))));

        Assert.Equal(new byte[] { 0, 100, 200 }, packets[0][4..7]);
        Assert.Equal(new byte[] { 19, 119, 181 }, packets[0][61..64]);
        Assert.Equal(new byte[] { 21, 121, 179 }, packets[1][7..10]);
        Assert.Equal(new byte[] { 59, 159, 141 }, packets[2][61..64]);
    }

    [Fact]
    public void Encode_pads_a_short_frame_with_black()
    {
        var packets = LedRingPackets.Encode([new SKColor(0xFF, 0x80, 0x01)]);

        Assert.Equal(new byte[] { 0xFF, 0x80, 0x01 }, packets[0][4..7]);
        Assert.All(packets[0][7..], b => Assert.Equal(0, b));
        Assert.All(packets[1][2..], b => Assert.Equal(0, b));
        Assert.All(packets[2][2..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Encode_ignores_alpha()
    {
        var packets = LedRingPackets.Encode([new SKColor(10, 20, 30, 0)]);

        Assert.Equal(new byte[] { 10, 20, 30 }, packets[0][4..7]);
    }

    [Fact]
    public void Encode_rejects_more_than_60_colors() =>
        Assert.Throws<ArgumentException>(() => LedRingPackets.Encode(new SKColor[LedRingPackets.LedCount + 1]));
}
