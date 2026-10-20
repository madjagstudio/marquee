using Marquee.Core.Sensors;
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
        Assert.Equal(1.2, keys["cpu.voltage"]);
    }

    [Fact]
    public void Intel_and_nvidia_voltages_and_gpu_fans_as_hwinfo_names_them()
    {
        // A Core i7-12700K and an RTX 3080 in HWiNFO. Its CPU sensor here has only the cores' requested voltages, so the
        // motherboard's measured Vcore is preferred; the card's fans also keep their fan.* keys.
        string[] sensors =
        [
            "CPU [#0]: Intel Core i7-12700K",
            "GPU [#0]: NVIDIA GeForce RTX 3080",
            "MSI MPG Z690 EDGE WIFI DDR4 (MS-7D31) (Nuvoton NCT6687D)",
        ];
        Reading[] readings =
        [
            new(HwInfoReadingType.Voltage, 0, "Core VIDs (avg)", "V", 1.302),
            new(HwInfoReadingType.Voltage, 1, "GPU Core Voltage", "V", 0.881),
            new(HwInfoReadingType.Fan, 1, "GPU Fan1", "RPM", 1310),
            new(HwInfoReadingType.Fan, 1, "GPU Fan2", "RPM", 1295),
            new(HwInfoReadingType.Voltage, 2, "Vcore", "V", 1.264),
            new(HwInfoReadingType.Voltage, 2, "+12V", "V", 12.096),
            new(HwInfoReadingType.Fan, 2, "CPU", "RPM", 1180),
        ];

        var keys = HardwareKeyMap.Map(HwInfoSharedMemory.ToHardware(HwInfoSharedMemory.Parse(Build(sensors, readings))));

        Assert.Equal(
            new Dictionary<string, double>
            {
                ["cpu.voltage"] = 1.264, ["gpu.voltage"] = 0.881, ["gpu.fan"] = 1310,
                ["fan.gpu_fan1"] = 1310, ["fan.gpu_fan2"] = 1295, ["fan.cpu"] = 1180,
            },
            keys);
    }

    [Fact]
    public void Ryzen_and_radeon_voltages_as_hwinfo_names_them()
    {
        // A Ryzen 9 7950X and a Radeon RX 7900 XTX: the averaged VIDs come first in HWiNFO's list, but the measured SVI3
        // core voltage wins.
        string[] sensors =
        [
            "CPU [#0]: AMD Ryzen 9 7950X",
            "CPU [#0]: AMD Ryzen 9 7950X: Enhanced",
            "GPU [#0]: AMD Radeon RX 7900 XTX",
        ];
        Reading[] readings =
        [
            new(HwInfoReadingType.Voltage, 0, "Core VIDs (avg)", "V", 1.35),
            new(HwInfoReadingType.Voltage, 1, "CPU Core Voltage (SVI3 TFN)", "V", 1.28),
            new(HwInfoReadingType.Voltage, 1, "SoC Voltage (SVI3 TFN)", "V", 1.25),
            new(HwInfoReadingType.Voltage, 2, "GPU Core Voltage (VDDCR_GFX)", "V", 1.07),
            new(HwInfoReadingType.Voltage, 2, "GPU Memory Voltage (VDDIO_MEM)", "V", 1.35),
            new(HwInfoReadingType.Fan, 2, "GPU Fan", "RPM", 1450),
        ];

        var keys = HardwareKeyMap.Map(HwInfoSharedMemory.ToHardware(HwInfoSharedMemory.Parse(Build(sensors, readings))));

        Assert.Equal(
            new Dictionary<string, double>
            {
                ["cpu.voltage"] = 1.28, ["gpu.voltage"] = 1.07, ["gpu.fan"] = 1450, ["fan.gpu_fan"] = 1450,
            },
            keys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_an_integrated_gpu_and_a_card_gpu_voltage_and_gpu_fan_come_from_the_card(bool integratedFirst)
    {
        // A Ryzen 7 7700X's integrated GPU and an RTX 4080. HWiNFO numbers GPUs in its own order, which can put the
        // integrated one first, and the integrated GPU's AMD label ranks above the card's NVIDIA one. The card holds
        // the most dedicated memory, so its readings win, in either order.
        string[] sensors = integratedFirst
            ? ["GPU [#0]: AMD Radeon(TM) Graphics", "GPU [#1]: NVIDIA GeForce RTX 4080"]
            : ["GPU [#0]: NVIDIA GeForce RTX 4080", "GPU [#1]: AMD Radeon(TM) Graphics"];
        var (integrated, card) = integratedFirst ? (0, 1) : (1, 0);
        Reading[] readings =
        [
            new(HwInfoReadingType.Temperature, 0, "GPU Temperature", "°C", 50),
            new(HwInfoReadingType.Temperature, 1, "GPU Temperature", "°C", 60),
            new(HwInfoReadingType.Voltage, integrated, "GPU Core Voltage (VDDCR_GFX)", "V", 1.1),
            new(HwInfoReadingType.Other, integrated, "GPU D3D Memory Dedicated", "MB", 310),
            new(HwInfoReadingType.Voltage, card, "GPU Core Voltage", "V", 0.925),
            new(HwInfoReadingType.Fan, card, "GPU Fan1", "RPM", 1310),
            new(HwInfoReadingType.Other, card, "GPU Memory Allocated", "MB", 2150),
        ];

        var keys = HardwareKeyMap.Map(HwInfoSharedMemory.ToHardware(HwInfoSharedMemory.Parse(Build(sensors, readings))));

        Assert.Equal(0.925, keys["gpu.voltage"]);
        Assert.Equal(1310, keys["gpu.fan"]);
        Assert.Equal(50, keys["gpu.temp"]); // unchanged: the first GPU HWiNFO lists, whichever it is
    }

    [Fact]
    public void Readings_carry_their_device_one_for_each_of_hwinfos_numbered_devices()
    {
        var readings = HwInfoSharedMemory.ToHardware(
        [
            new HwInfoReading("CPU [#0]: AMD Ryzen 9 7950X", HwInfoReadingType.Voltage, "Core VIDs (avg)", "", "V", 1.35),
            new HwInfoReading("CPU [#0]: AMD Ryzen 9 7950X: Enhanced", HwInfoReadingType.Voltage, "CPU Core Voltage (SVI3 TFN)", "", "V", 1.28),
            new HwInfoReading("GPU [#1]: NVIDIA GeForce RTX 4080", HwInfoReadingType.Voltage, "GPU Core Voltage", "", "V", 0.925),
            new HwInfoReading("ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)", HwInfoReadingType.Voltage, "Vcore", "", "V", 1.3),
        ]);

        Assert.Equal(
            ["CPU [#0]: AMD Ryzen 9 7950X", "CPU [#0]: AMD Ryzen 9 7950X", "GPU [#1]: NVIDIA GeForce RTX 4080", "ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)"],
            readings.Select(r => r.Device));
    }

    [Fact]
    public void Voltages_become_volt_readings_on_their_hardware()
    {
        var readings = HwInfoSharedMemory.ToHardware(
        [
            new HwInfoReading("CPU [#0]: AMD Ryzen 9 7950X: Enhanced", HwInfoReadingType.Voltage, "CPU Core Voltage (SVI3 TFN)", "", "V", 1.28),
            new HwInfoReading("ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)", HwInfoReadingType.Voltage, "Vcore", "", "V", 1.3),
        ]);

        Assert.Equal(
            [
                new HardwareReading(HardwareKind.Cpu, ReadingKind.Voltage, "CPU Core Voltage (SVI3 TFN)", 1.28) { Device = "CPU [#0]: AMD Ryzen 9 7950X" },
                new HardwareReading(HardwareKind.Other, ReadingKind.Voltage, "Vcore", 1.3) { Device = "ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)" },
            ],
            readings);
    }

    [Fact]
    public void The_provider_keeps_the_readings_of_its_last_poll()
    {
        using var provider = new HwInfoProvider();
        var store = new SensorStore();
        Assert.Empty(provider.LastReadings);

        provider.Poll(store, Build(Sensors, Readings));

        Assert.Equal(Readings.Length, provider.LastReadings.Count);
        Assert.Equal(
            new RawReading(HardwareKind.Cpu, "CPU [#0]: AMD Ryzen 9 7950X", "Voltage", "CPU Core Voltage (SVI3 TFN)", 1.2, "V"),
            provider.LastReadings[8]);
        Assert.True(store.Snapshot().TryGet("cpu.voltage", out var voltage));
        Assert.Equal(1.2, voltage.Number);
    }

    [Fact]
    public void The_last_readings_keep_zeros_and_readings_the_key_map_does_not_take_and_the_keys_stay_the_same()
    {
        // What someone diagnosing a missing key needs to see: a voltage HWiNFO reads as 0, a current, and a reading in
        // a unit the key map doesn't convert, each as HWiNFO gives it, with its unit. A fan keeps its user's label.
        string[] sensors = ["CPU [#0]: AMD Ryzen 9 7950X: Enhanced", "ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)"];
        Reading[] readings =
        [
            new(HwInfoReadingType.Voltage, 0, "CPU Core Voltage (SVI3 TFN)", "V", 0),
            new(HwInfoReadingType.Current, 0, "CPU Core Current (SVI3 TFN)", "A", 61.5),
            new(HwInfoReadingType.Other, 0, "Core Ratio", "x", 47.5),
            new(HwInfoReadingType.Fan, 1, "Chassis1", "RPM", 900, UserLabel: "Front Intake"),
            new(HwInfoReadingType.Voltage, 1, "Vcore", "V", 1.3),
        ];
        using var provider = new HwInfoProvider();
        var store = new SensorStore();

        provider.Poll(store, Build(sensors, readings));

        Assert.Equal(
            [
                new RawReading(HardwareKind.Cpu, "CPU [#0]: AMD Ryzen 9 7950X", "Voltage", "CPU Core Voltage (SVI3 TFN)", 0, "V"),
                new RawReading(HardwareKind.Cpu, "CPU [#0]: AMD Ryzen 9 7950X", "Current", "CPU Core Current (SVI3 TFN)", 61.5, "A"),
                new RawReading(HardwareKind.Cpu, "CPU [#0]: AMD Ryzen 9 7950X", "Other", "Core Ratio", 47.5, "x"),
                new RawReading(HardwareKind.Other, "ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)", "Fan", "Front Intake", 900, "RPM"),
                new RawReading(HardwareKind.Other, "ASUS ROG STRIX X670E-E (Nuvoton NCT6799D)", "Voltage", "Vcore", 1.3, "V"),
            ],
            provider.LastReadings);
        // The keys are what the key map makes of the readings it takes, as before: HWiNFO's 0 V is a reading, so it wins.
        var keys = HardwareKeyMap.Map(HwInfoSharedMemory.ToHardware(HwInfoSharedMemory.Parse(Build(sensors, readings))));
        Assert.Equal(keys.Keys.Order(StringComparer.Ordinal), store.Snapshot().Keys.Order(StringComparer.Ordinal));
        Assert.Equal(0, keys["cpu.voltage"]);
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
