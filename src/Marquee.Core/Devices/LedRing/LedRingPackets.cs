using SkiaSharp;

namespace Marquee.Core.Devices.LedRing;

/// The LED ring's only command: set 20 LEDs from one 64-byte packet. Byte 0 is the command (0x11), byte 1 the index
/// of the packet's first LED, bytes 2 and 3 are zero, and red, green and blue for each LED follow from byte 4.
/// A frame is three packets covering all 60 LEDs.
/// Protocol facts from sgtaziz/lian-li-linux (MIT, crates/lianli-devices/src/winusb/led.rs); no code was copied.
public static class LedRingPackets
{
    public const int LedCount = 60;

    const int LedsPerPacket = 20;
    const int PacketSize = 64;
    const byte SetLeds = 0x11;
    const int ColorsOffset = 4;

    /// Alpha is ignored, so apply brightness before calling. A frame shorter than 60 LEDs is padded with black.
    public static byte[][] Encode(ReadOnlySpan<SKColor> colors)
    {
        if (colors.Length > LedCount)
            throw new ArgumentException($"The LED ring has {LedCount} LEDs, not {colors.Length}.", nameof(colors));

        var packets = new byte[LedCount / LedsPerPacket][];
        for (var p = 0; p < packets.Length; p++)
        {
            var packet = new byte[PacketSize];
            var first = p * LedsPerPacket;
            packet[0] = SetLeds;
            packet[1] = (byte)first;
            for (var i = 0; i < LedsPerPacket && first + i < colors.Length; i++)
            {
                var color = colors[first + i];
                var offset = ColorsOffset + i * 3;
                packet[offset] = color.Red;
                packet[offset + 1] = color.Green;
                packet[offset + 2] = color.Blue;
            }
            packets[p] = packet;
        }
        return packets;
    }
}
