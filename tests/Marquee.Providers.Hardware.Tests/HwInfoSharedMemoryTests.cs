using static Marquee.Providers.Hardware.Tests.HwInfoFixture;

namespace Marquee.Providers.Hardware.Tests;

public class HwInfoSharedMemoryTests
{
    static readonly string[] Sensors =
    [
        "System: ASUS ROG STRIX X670E-E",
        "CPU [#0]: AMD Ryzen 9 7950X: Enhanced",
        "GPU [#0]: NVIDIA GeForce RTX 4090",
        "ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)",
    ];

    static readonly Reading[] Readings =
    [
        new(HwInfoReadingType.Usage, 0, "Physical Memory Load", "%", 41.5),
        new(HwInfoReadingType.Other, 0, "Physical Memory Used", "MB", 26_000),
        new(HwInfoReadingType.Temperature, 1, "CPU (Tctl/Tdie)", "°C", 71.25),
        new(HwInfoReadingType.Usage, 1, "Total CPU Usage", "%", 12),
        new(HwInfoReadingType.Clock, 1, "Core 0 Clock (perf #1)", "MHz", 5450),
        new(HwInfoReadingType.Clock, 1, "Core 1 Clock (perf #2)", "MHz", 5700),
        new(HwInfoReadingType.Clock, 1, "Bus Clock", "MHz", 100),
        new(HwInfoReadingType.Power, 1, "CPU Package Power", "W", 88),
        new(HwInfoReadingType.Voltage, 1, "CPU Core Voltage (SVI3 TFN)", "V", 1.2),
        new(HwInfoReadingType.Temperature, 2, "GPU Temperature", "°C", 58),
        new(HwInfoReadingType.Usage, 2, "GPU Core Load", "%", 97),
        new(HwInfoReadingType.Other, 2, "GPU Memory Allocated", "MB", 8192),
        new(HwInfoReadingType.Fan, 3, "Chassis1", "RPM", 900, UserLabel: "Front Intake"),
        new(HwInfoReadingType.Fan, 3, "CPU", "RPM", 1250),
    ];

    [Fact]
    public void Parse_reads_sensors_and_readings_using_the_header_sizes()
    {
        var readings = HwInfoSharedMemory.Parse(Build(Sensors, Readings));

        Assert.Equal(Readings.Length, readings.Count);
        Assert.Equal(new HwInfoReading("CPU [#0]: AMD Ryzen 9 7950X: Enhanced", HwInfoReadingType.Temperature, "CPU (Tctl/Tdie)", "", "°C", 71.25), readings[2]);
        Assert.Equal("Front Intake", readings[12].UserLabel);
    }

    [Fact]
    public void Readings_map_to_canonical_keys_end_to_end()
    {
        var keys = HardwareKeyMap.Map(HwInfoSharedMemory.ToHardware(HwInfoSharedMemory.Parse(Build(Sensors, Readings))));

        Assert.Equal(71.25, keys["cpu.temp"]);
        Assert.Equal(12, keys["cpu.load"]);
        Assert.Equal(5700, keys["cpu.clock"]);
        Assert.Equal(88, keys["cpu.power"]);
        Assert.Equal(58, keys["gpu.temp"]);
        Assert.Equal(97, keys["gpu.load"]);
        Assert.Equal(8192.0 * 1024 * 1024, keys["gpu.vram_used"]);
        Assert.Equal(41.5, keys["ram.used_pct"]);
        Assert.Equal(26_000.0 * 1024 * 1024, keys["ram.used"]);
        Assert.Equal(900, keys["fan.front_intake"]);
        Assert.Equal(1250, keys["fan.cpu"]);
        Assert.DoesNotContain(keys.Keys, k => k.Contains("volt"));
    }

    [Theory]
    [InlineData(0x44414544u, "HWiNFO has stopped sharing sensor data.")]
    [InlineData(0x12345678u, "This is not HWiNFO sensor shared memory.")]
    public void Wrong_signatures_are_rejected(uint signature, string message) =>
        Assert.Equal(message, Assert.Throws<InvalidDataException>(() => HwInfoSharedMemory.Parse(Build(Sensors, Readings, signature))).Message);

    [Fact]
    public void Truncated_memory_is_rejected()
    {
        var memory = Build(Sensors, Readings);

        Assert.Throws<InvalidDataException>(() => HwInfoSharedMemory.Parse(memory.AsSpan(0, 20)));
        Assert.Equal("HWiNFO shared memory has an unexpected layout.",
            Assert.Throws<InvalidDataException>(() => HwInfoSharedMemory.Parse(memory.AsSpan(0, memory.Length - 10))).Message);
    }
}
