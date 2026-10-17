using System.Buffers.Binary;

namespace Marquee.Core.Devices.UniversalScreen88;

/// Encoders for the parameter bytes that start at plaintext offset 8.
public static class CommandParams
{
    public static byte[] Byte(int value) => [(byte)value];

    public static byte[] Length(int length)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)length);
        return bytes;
    }

    public static byte[] Clock(DateTime time)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)time.Year);
        bytes[2] = (byte)time.Month;
        bytes[3] = (byte)time.Day;
        bytes[4] = (byte)time.Hour;
        bytes[5] = (byte)time.Minute;
        bytes[6] = (byte)time.Second;
        bytes[7] = 2;
        return bytes;
    }
}
