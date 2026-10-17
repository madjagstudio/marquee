using System.Buffers.Binary;
using System.Text;

namespace Marquee.Providers.Hardware.Tests;

/// Builds HWiNFO shared memory the way HWiNFO lays it out, with the larger element sizes of newer versions
/// (which append UTF-8 copies of the strings), so tests prove the parser follows the header's sizes.
static class HwInfoFixture
{
    public const int SensorSize = 264 + 128;
    public const int ReadingSize = 316 + 144;

    public sealed record Reading(HwInfoReadingType Type, int Sensor, string Label, string Unit, double Value, string UserLabel = "");

    public static byte[] Build(string[] sensors, Reading[] readings, uint signature = 0x53695748)
    {
        const int header = 44;
        var sensorOffset = header;
        var readingOffset = sensorOffset + sensors.Length * SensorSize;
        var memory = new byte[readingOffset + readings.Length * ReadingSize];

        BinaryPrimitives.WriteUInt32LittleEndian(memory, signature);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(20), (uint)sensorOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(24), SensorSize);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(28), (uint)sensors.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(32), (uint)readingOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(36), ReadingSize);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(40), (uint)readings.Length);

        for (var i = 0; i < sensors.Length; i++)
        {
            var at = sensorOffset + i * SensorSize;
            Text(memory, at + 8, sensors[i]);
            Text(memory, at + 136, sensors[i]);
        }

        for (var i = 0; i < readings.Length; i++)
        {
            var at = readingOffset + i * ReadingSize;
            var r = readings[i];
            BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(at), (uint)r.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(at + 4), (uint)r.Sensor);
            Text(memory, at + 12, r.Label);
            Text(memory, at + 140, r.UserLabel);
            Text(memory, at + 268, r.Unit);
            BinaryPrimitives.WriteDoubleLittleEndian(memory.AsSpan(at + 284), r.Value);
        }

        return memory;
    }

    static void Text(byte[] memory, int offset, string text) => Encoding.Latin1.GetBytes(text, memory.AsSpan(offset));
}
