using LibreHardwareMonitor.Hardware;

namespace Marquee.Providers.Hardware.Tests;

/// How LibreHardwareMonitor's sensors become readings, without the hardware: what the key map takes, and what
/// `marquee sensors --raw` lists.
public class LibreHardwareMonitorProviderTests
{
    [Fact]
    public void Every_sensor_with_a_value_is_a_raw_reading_and_the_key_map_takes_only_the_readable_kinds_it_knows()
    {
        // An unelevated Ryzen 7 5800X: LibreHardwareMonitor reports the core voltage and Tctl as 0, which the key map
        // must not take for real values, but --raw shows them, with the Control and Factor sensors it doesn't map.
        var readings = new List<HardwareReading>();
        var raw = new List<RawReading>();

        LibreHardwareMonitorProvider.Add("AMD Ryzen 7 5800X", HardwareKind.Cpu,
        [
            (SensorType.Voltage, "Core (SVI2 TFN)", 0f),
            (SensorType.Temperature, "Core (Tctl/Tdie)", 0f),
            (SensorType.Load, "CPU Total", 0f),
            (SensorType.Control, "Fan Control #1", 45f),
            (SensorType.Factor, "Core #1 Multiplier", 46.5f),
            (SensorType.SmallData, "Cache Used", 512f),
            (SensorType.Clock, "Bus Speed", null),
        ], readings, raw);

        Assert.Equal(
            [
                new RawReading(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Voltage", "Core (SVI2 TFN)", 0),
                new RawReading(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Temperature", "Core (Tctl/Tdie)", 0),
                new RawReading(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Load", "CPU Total", 0),
                new RawReading(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Control", "Fan Control #1", 45),
                new RawReading(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Factor", "Core #1 Multiplier", 46.5),
                new RawReading(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "SmallData", "Cache Used", 512),
            ],
            raw);
        // A load of 0 is real; an unreadable voltage or temperature isn't. Sizes are in bytes.
        Assert.Equal(
            [
                new HardwareReading(HardwareKind.Cpu, ReadingKind.Load, "CPU Total", 0) { Device = "AMD Ryzen 7 5800X" },
                new HardwareReading(HardwareKind.Cpu, ReadingKind.Bytes, "Cache Used", 512.0 * 1024 * 1024) { Device = "AMD Ryzen 7 5800X" },
            ],
            readings);
    }
}
