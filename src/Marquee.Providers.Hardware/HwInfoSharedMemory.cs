using System.Buffers.Binary;
using System.Text;

namespace Marquee.Providers.Hardware;

public enum HwInfoReadingType { None, Temperature, Voltage, Fan, Current, Power, Clock, Usage, Other }

public sealed record HwInfoReading(string Sensor, HwInfoReadingType Type, string Label, string UserLabel, string Unit, double Value);

/// Parses HWiNFO's sensor shared memory (enable it in HWiNFO: Settings → Shared Memory Support).
/// Layout, packed and little-endian, per HWiNFO's published shared-memory SDK:
///   header  (44 bytes): u32 signature "HWiS", u32 version, u32 revision, i64 poll time,
///                       u32 sensor offset, u32 sensor size, u32 sensor count,
///                       u32 reading offset, u32 reading size, u32 reading count
///   sensor  (≥ 264):    u32 id, u32 instance, char[128] original name, char[128] user name
///   reading (≥ 316):    u32 type, u32 sensor index, u32 id, char[128] original label, char[128] user label,
///                       char[16] unit, f64 value, f64 min, f64 max, f64 average
/// Element sizes come from the header, so newer versions that append fields still parse. Strings are ANSI.
public static class HwInfoSharedMemory
{
    public const string MapName = @"Global\HWiNFO_SENS_SM2";

    const uint Signature = 0x53695748; // "HWiS"
    const uint DeadSignature = 0x44414544; // "DEAD": HWiNFO stopped sharing
    const int HeaderSize = 44;
    const int MinSensorSize = 264;
    const int MinReadingSize = 316;

    public static IReadOnlyList<HwInfoReading> Parse(ReadOnlySpan<byte> memory)
    {
        if (memory.Length < HeaderSize) throw new InvalidDataException("HWiNFO shared memory is too small.");
        var signature = U32(memory, 0);
        if (signature == DeadSignature) throw new InvalidDataException("HWiNFO has stopped sharing sensor data.");
        if (signature != Signature) throw new InvalidDataException("This is not HWiNFO sensor shared memory.");

        var (sensorOffset, sensorSize, sensorCount) = (U32(memory, 20), U32(memory, 24), U32(memory, 28));
        var (readingOffset, readingSize, readingCount) = (U32(memory, 32), U32(memory, 36), U32(memory, 40));
        if (sensorSize < MinSensorSize || readingSize < MinReadingSize
            || sensorOffset + (long)sensorSize * sensorCount > memory.Length
            || readingOffset + (long)readingSize * readingCount > memory.Length)
            throw new InvalidDataException("HWiNFO shared memory has an unexpected layout.");

        var sensors = new string[sensorCount];
        for (var i = 0; i < sensorCount; i++)
            sensors[i] = Text(memory, (int)(sensorOffset + i * sensorSize + 8), 128);

        var readings = new List<HwInfoReading>((int)readingCount);
        for (var i = 0; i < readingCount; i++)
        {
            var at = (int)(readingOffset + i * readingSize);
            var sensor = U32(memory, at + 4);
            readings.Add(new HwInfoReading(
                sensor < sensorCount ? sensors[sensor] : "",
                (HwInfoReadingType)U32(memory, at),
                Text(memory, at + 12, 128),
                Text(memory, at + 140, 128),
                Text(memory, at + 268, 16),
                BinaryPrimitives.ReadDoubleLittleEndian(memory[(at + 284)..])));
        }

        return readings;
    }

    /// HWiNFO readings → canonical readings. Sensors are grouped by their names ("CPU [#0]: …", "GPU [#0]: …");
    /// physical memory readings live under the "System" sensor. Fans use the label the user gave them, if any.
    public static IReadOnlyList<HardwareReading> ToHardware(IEnumerable<HwInfoReading> readings)
    {
        var result = new List<HardwareReading>();
        foreach (var r in readings)
        {
            (ReadingKind? Kind, double Scale) mapping = r.Type switch
            {
                HwInfoReadingType.Temperature => (ReadingKind.Temperature, 1.0),
                HwInfoReadingType.Fan => (ReadingKind.Fan, 1.0),
                HwInfoReadingType.Power => (ReadingKind.Power, 1.0),
                HwInfoReadingType.Clock => (ReadingKind.Clock, 1.0),
                HwInfoReadingType.Usage => (ReadingKind.Load, 1.0),
                HwInfoReadingType.Voltage => (ReadingKind.Voltage, 1.0),
                HwInfoReadingType.Other when r.Unit == "MB" => (ReadingKind.Bytes, 1024.0 * 1024),
                HwInfoReadingType.Other when r.Unit == "GB" => (ReadingKind.Bytes, 1024.0 * 1024 * 1024),
                _ => (null, 0),
            };
            if (mapping.Kind is not { } k) continue;

            result.Add(new HardwareReading(Hardware(r), k, Label(r), r.Value * mapping.Scale) { Device = Device(r.Sensor) });
        }

        return result;
    }

    /// Every reading as HWiNFO gives it, for `marquee sensors --raw`: its type and unit, whether or not a key takes it.
    public static IReadOnlyList<RawReading> ToRaw(IEnumerable<HwInfoReading> readings) =>
        readings.Select(r => new RawReading(Hardware(r), Device(r.Sensor), r.Type.ToString(), Label(r), r.Value, r.Unit)).ToList();

    static HardwareKind Hardware(HwInfoReading r) =>
        r.Label.StartsWith("Physical Memory", StringComparison.OrdinalIgnoreCase) ? HardwareKind.Memory
        : r.Sensor.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ? HardwareKind.Cpu
        : r.Sensor.StartsWith("GPU", StringComparison.OrdinalIgnoreCase) ? HardwareKind.Gpu
        : HardwareKind.Other;

    static string Label(HwInfoReading r) => r.Type == HwInfoReadingType.Fan && r.UserLabel.Length > 0 ? r.UserLabel : r.Label;

    /// The device a sensor belongs to. HWiNFO numbers its CPUs and GPUs and can split one into several sensors
    /// ("CPU [#0]: AMD Ryzen 9 7950X" and "CPU [#0]: AMD Ryzen 9 7950X: Enhanced"), so a numbered sensor's name is
    /// cut at the colon after the device's own name; any other sensor is a device of its own.
    internal static string Device(string sensor)
    {
        var numbered = sensor.IndexOf("]: ", StringComparison.Ordinal);
        if (numbered < 0 || !sensor[..numbered].Contains("[#", StringComparison.Ordinal)) return sensor;
        var end = sensor.IndexOf(':', numbered + 3);
        return end < 0 ? sensor : sensor[..end];
    }

    static uint U32(ReadOnlySpan<byte> memory, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(memory[offset..]);

    static string Text(ReadOnlySpan<byte> memory, int offset, int length)
    {
        var bytes = memory.Slice(offset, length);
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }
}
