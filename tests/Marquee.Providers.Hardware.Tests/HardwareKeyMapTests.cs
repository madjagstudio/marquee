namespace Marquee.Providers.Hardware.Tests;

public class HardwareKeyMapTests
{
    static HardwareReading R(HardwareKind hardware, ReadingKind kind, string label, double value) => new(hardware, kind, label, value);

    [Fact]
    public void Intel_and_nvidia_as_librehardwaremonitor_names_them()
    {
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Temperature, "Core Max", 80),
            R(HardwareKind.Cpu, ReadingKind.Temperature, "CPU Package", 76),
            R(HardwareKind.Cpu, ReadingKind.Load, "CPU Total", 23.5),
            R(HardwareKind.Cpu, ReadingKind.Clock, "Bus Speed", 100),
            R(HardwareKind.Cpu, ReadingKind.Clock, "P-Core #1", 5500),
            R(HardwareKind.Cpu, ReadingKind.Clock, "P-Core #2", 5800),
            R(HardwareKind.Cpu, ReadingKind.Power, "CPU Package", 125),
            R(HardwareKind.Gpu, ReadingKind.Temperature, "GPU Core", 61),
            R(HardwareKind.Gpu, ReadingKind.Load, "GPU Core", 99),
            R(HardwareKind.Gpu, ReadingKind.Clock, "GPU Core", 2700),
            R(HardwareKind.Gpu, ReadingKind.Power, "GPU Package", 410),
            R(HardwareKind.Gpu, ReadingKind.Bytes, "GPU Memory Used", 9e9),
            R(HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan 1", 1600),
            R(HardwareKind.Memory, ReadingKind.Load, "Memory", 55),
            R(HardwareKind.Memory, ReadingKind.Bytes, "Memory Used", 17e9),
            R(HardwareKind.Other, ReadingKind.Fan, "Fan #1", 1100),
        ]);

        Assert.Equal(
            new Dictionary<string, double>
            {
                ["cpu.temp"] = 76, ["cpu.load"] = 23.5, ["cpu.clock"] = 5800, ["cpu.power"] = 125,
                ["gpu.temp"] = 61, ["gpu.load"] = 99, ["gpu.clock"] = 2700, ["gpu.power"] = 410, ["gpu.vram_used"] = 9e9,
                ["ram.used_pct"] = 55, ["ram.used"] = 17e9, ["fan.gpu_fan_1"] = 1600, ["fan.fan_1"] = 1100, ["gpu.fan"] = 1600,
            },
            keys);
    }

    [Fact]
    public void Amd_cpu_temperature_is_tctl()
    {
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Temperature, "Core (CCD1)", 60),
            R(HardwareKind.Cpu, ReadingKind.Temperature, "Core (Tctl/Tdie)", 72),
            R(HardwareKind.Cpu, ReadingKind.Power, "Package", 140),
        ]);

        Assert.Equal(72, keys["cpu.temp"]);
        Assert.Equal(140, keys["cpu.power"]);
    }

    [Fact]
    public void The_first_gpu_listed_wins()
    {
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Gpu, ReadingKind.Temperature, "GPU Core", 61),
            R(HardwareKind.Gpu, ReadingKind.Temperature, "GPU Core", 45),
        ]);

        Assert.Equal(61, keys["gpu.temp"]);
    }

    [Fact]
    public void A_gpu_without_driver_sensors_falls_back_to_its_d3d_readings()
    {
        // What LibreHardwareMonitor reports for an AMD RX 7800 XT when it can't reach the GPU driver's sensors.
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Gpu, ReadingKind.Load, "D3D Copy", 3),
            R(HardwareKind.Gpu, ReadingKind.Load, "D3D 3D", 41),
            R(HardwareKind.Gpu, ReadingKind.Bytes, "D3D Dedicated Memory Used", 1.1e9),
        ]);

        Assert.Equal(41, keys["gpu.load"]);
        Assert.Equal(1.1e9, keys["gpu.vram_used"]);
    }

    [Fact]
    public void Readings_on_the_wrong_hardware_are_ignored_and_missing_keys_stay_absent()
    {
        var keys = HardwareKeyMap.Map([R(HardwareKind.Other, ReadingKind.Temperature, "CPU Package", 50)]);

        Assert.Empty(keys);
    }

    [Fact]
    public void Ryzen_and_radeon_voltages_and_gpu_fan_as_librehardwaremonitor_names_them()
    {
        // A Ryzen 7 5800X and a Radeon RX 6800 XT, elevated with PawnIO: the sensor names LibreHardwareMonitorLib 0.9.6
        // gives them (Amd17Cpu, AmdGpu and the Super I/O chip's board configuration).
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Temperature, "Core (Tctl/Tdie)", 68),
            R(HardwareKind.Cpu, ReadingKind.Voltage, "Core #1 VID", 1.4),
            R(HardwareKind.Cpu, ReadingKind.Voltage, "Core (SVI2 TFN)", 1.356),
            R(HardwareKind.Cpu, ReadingKind.Voltage, "SoC (SVI2 TFN)", 1.1),
            R(HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core", 1.05),
            R(HardwareKind.Gpu, ReadingKind.Voltage, "GPU Memory", 1.35),
            R(HardwareKind.Gpu, ReadingKind.Voltage, "GPU SoC", 0.9),
            R(HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan", 1450),
            R(HardwareKind.Other, ReadingKind.Voltage, "Vcore", 1.37),
            R(HardwareKind.Other, ReadingKind.Fan, "CPU Fan", 1100),
        ]);

        Assert.Equal(
            new Dictionary<string, double>
            {
                ["cpu.temp"] = 68, ["cpu.voltage"] = 1.356, ["gpu.voltage"] = 1.05, ["gpu.fan"] = 1450,
                ["fan.gpu_fan"] = 1450, ["fan.cpu_fan"] = 1100,
            },
            keys);
    }

    [Fact]
    public void Intel_and_nvidia_voltages_and_gpu_fan_as_librehardwaremonitor_names_them()
    {
        // A Core i9-13900K with its integrated GPU and an RTX 4090. The provider lists the card with the most dedicated
        // memory first; the integrated GPU's "GPU Core" voltage must not win over the card's "GPU Core Voltage".
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Voltage, "CPU Core", 1.25),
            R(HardwareKind.Cpu, ReadingKind.Voltage, "CPU Core #1", 1.24),
            R(HardwareKind.Cpu, ReadingKind.Voltage, "CPU Core #2", 1.26),
            R(HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core Voltage", 0.95),
            R(HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan 1", 1600),
            R(HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan 2", 1650),
            R(HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core", 0.7),
            R(HardwareKind.Other, ReadingKind.Voltage, "Vcore", 1.27),
            R(HardwareKind.Other, ReadingKind.Voltage, "+12V", 12.1),
        ]);

        Assert.Equal(
            new Dictionary<string, double>
            {
                ["cpu.voltage"] = 1.25, ["gpu.voltage"] = 0.95, ["gpu.fan"] = 1600,
                ["fan.gpu_fan_1"] = 1600, ["fan.gpu_fan_2"] = 1650,
            },
            keys);
    }

    [Theory]
    [InlineData("Core (SVI2 TFN)")] // LibreHardwareMonitor, AMD Zen to Zen 3
    [InlineData("VDDCR")] // LibreHardwareMonitor, AMD Zen 4 with PM table 0x540104
    [InlineData("CPU Core")] // LibreHardwareMonitor, Intel
    [InlineData("CPU Core Voltage (SVI2 TFN)")] // HWiNFO, AMD Zen 2 and Zen 3
    [InlineData("CPU Core Voltage (SVI3 TFN)")] // HWiNFO, AMD Zen 4 and later
    [InlineData("VR VCC Voltage (SVID VOUT)")] // HWiNFO, Intel
    [InlineData("Core VIDs (avg)")] // HWiNFO, the cores' requested voltages
    public void Each_cpu_core_voltage_label_is_cpu_voltage(string label)
    {
        var keys = HardwareKeyMap.Map([R(HardwareKind.Cpu, ReadingKind.Voltage, label, 1.2)]);

        Assert.Equal(1.2, keys["cpu.voltage"]);
    }

    [Theory]
    [InlineData("Vcore")]
    [InlineData("VCore")]
    [InlineData("CPU Vcore")]
    [InlineData("CPU Core")]
    public void Without_a_cpu_reading_the_motherboards_core_voltage_is_cpu_voltage(string label)
    {
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Voltage, "SoC (SVI2 TFN)", 1.1),
            R(HardwareKind.Other, ReadingKind.Voltage, "Vcore SoC", 1.05),
            R(HardwareKind.Other, ReadingKind.Voltage, label, 1.31),
        ]);

        Assert.Equal(1.31, keys["cpu.voltage"]);
    }

    [Fact]
    public void A_vid_is_cpu_voltage_only_when_nothing_measures_the_core()
    {
        var withVcore = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Voltage, "Core VIDs (avg)", 1.4),
            R(HardwareKind.Other, ReadingKind.Voltage, "Vcore", 1.31),
        ]);
        var vidOnly = HardwareKeyMap.Map([R(HardwareKind.Cpu, ReadingKind.Voltage, "Core VIDs (avg)", 1.4)]);

        Assert.Equal(1.31, withVcore["cpu.voltage"]);
        Assert.Equal(1.4, vidOnly["cpu.voltage"]);
    }

    [Theory]
    [InlineData("GPU Core")] // LibreHardwareMonitor, AMD and Intel
    [InlineData("GPU Core Voltage")] // LibreHardwareMonitor and HWiNFO, NVIDIA
    [InlineData("GPU Core Voltage (VDDCR_GFX)")] // HWiNFO, AMD
    public void Each_gpu_core_voltage_label_is_gpu_voltage(string label)
    {
        var keys = HardwareKeyMap.Map([R(HardwareKind.Gpu, ReadingKind.Voltage, label, 1.05)]);

        Assert.Equal(1.05, keys["gpu.voltage"]);
    }

    const string Card = "NVIDIA GeForce RTX 4080";
    const string Integrated = "AMD Radeon(TM) Graphics";

    static HardwareReading On(string device, HardwareKind hardware, ReadingKind kind, string label, double value) =>
        new(hardware, kind, label, value) { Device = device };

    [Fact]
    public void Gpu_voltage_comes_from_the_gpu_listed_first_even_when_another_has_a_label_ranked_higher()
    {
        // The card is listed first, but the integrated GPU after it reports AMD's label, which the rule ranks above
        // NVIDIA's. Taking the device first, then its best label, keeps gpu.voltage on the card.
        var keys = HardwareKeyMap.Map(
        [
            On(Card, HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core", 0.7),
            On(Card, HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core Voltage", 0.9),
            On(Integrated, HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core Voltage (VDDCR_GFX)", 1.1),
        ]);

        Assert.Equal(0.9, keys["gpu.voltage"]); // the card's best label, not its first reading
    }

    [Fact]
    public void Librehardwaremonitor_with_an_integrated_gpu_and_a_card_takes_the_cards_voltage_and_fan()
    {
        // A Ryzen 9 7950X's integrated GPU and a Radeon RX 7900 XTX: LibreHardwareMonitor names both GPUs' core
        // voltage "GPU Core". The provider lists the GPU with the most dedicated memory first; listed the other way
        // round, the card's dedicated memory still makes it the one gpu.voltage and gpu.fan come from.
        HardwareReading[] card =
        [
            On("AMD Radeon RX 7900 XTX", HardwareKind.Gpu, ReadingKind.Temperature, "GPU Core", 55),
            On("AMD Radeon RX 7900 XTX", HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core", 1.05),
            On("AMD Radeon RX 7900 XTX", HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan", 1450),
            On("AMD Radeon RX 7900 XTX", HardwareKind.Gpu, ReadingKind.Bytes, "GPU Memory Total", 24e9),
        ];
        HardwareReading[] integrated =
        [
            On(Integrated, HardwareKind.Gpu, ReadingKind.Temperature, "GPU Core", 48),
            On(Integrated, HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core", 1.2),
            On(Integrated, HardwareKind.Gpu, ReadingKind.Bytes, "D3D Dedicated Memory Total", 0.5e9),
        ];

        var cardFirst = HardwareKeyMap.Map([.. card, .. integrated]);
        var integratedFirst = HardwareKeyMap.Map([.. integrated, .. card]);

        Assert.Equal((1.05, 1450), (cardFirst["gpu.voltage"], cardFirst["gpu.fan"]));
        Assert.Equal((1.05, 1450), (integratedFirst["gpu.voltage"], integratedFirst["gpu.fan"]));
        // The older gpu.* keys are unchanged: the first reading with the best label, in the order listed.
        Assert.Equal(55, cardFirst["gpu.temp"]);
        Assert.Equal(48, integratedFirst["gpu.temp"]);
    }

    [Fact]
    public void Gpus_without_dedicated_memory_readings_keep_the_order_they_are_listed_in()
    {
        var keys = HardwareKeyMap.Map(
        [
            On("First", HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan 2", 1200),
            On("Second", HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core Voltage (VDDCR_GFX)", 1.1),
            On("Second", HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan 1", 900),
            On("First", HardwareKind.Gpu, ReadingKind.Voltage, "GPU Core", 0.8),
        ]);

        Assert.Equal(0.8, keys["gpu.voltage"]);
        Assert.Equal(1200, keys["gpu.fan"]);
    }

    [Fact]
    public void Gpu_fan_is_the_first_gpu_fan_and_every_fan_keeps_its_fan_key()
    {
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Other, ReadingKind.Fan, "System Fan #1", 900),
            R(HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan1", 0),
            R(HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan2", 1500),
            R(HardwareKind.Gpu, ReadingKind.Fan, "GPU Fan", 1200),
        ]);

        // A fan stopped at idle reads 0 RPM, which is a real speed.
        Assert.Equal(0, keys["gpu.fan"]);
        Assert.Equal(0, keys["fan.gpu_fan1"]);
        Assert.Equal(1500, keys["fan.gpu_fan2"]);
        Assert.Equal(1200, keys["fan.gpu_fan"]);
        Assert.Equal(900, keys["fan.system_fan_1"]);
    }

    [Fact]
    public void Without_voltages_or_a_gpu_fan_the_new_keys_stay_absent()
    {
        // What an unelevated LibreHardwareMonitor reports: no CPU voltages, and this card reports no fan.
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Load, "CPU Total", 12),
            R(HardwareKind.Gpu, ReadingKind.Load, "D3D 3D", 41),
            R(HardwareKind.Other, ReadingKind.Fan, "GPU Fan", 800),
        ]);

        Assert.Equal(["cpu.load", "fan.gpu_fan", "gpu.load"], keys.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Voltages_that_are_not_core_voltages_are_ignored()
    {
        var keys = HardwareKeyMap.Map(
        [
            R(HardwareKind.Cpu, ReadingKind.Voltage, "SoC (SVI2 TFN)", 1.1),
            R(HardwareKind.Cpu, ReadingKind.Voltage, "VDDCR SoC", 1.05),
            R(HardwareKind.Gpu, ReadingKind.Voltage, "GPU Memory", 1.35),
            R(HardwareKind.Gpu, ReadingKind.Voltage, "12VHPWR Pin 1", 12.1),
            R(HardwareKind.Other, ReadingKind.Voltage, "+12V", 12.1),
            R(HardwareKind.Other, ReadingKind.Voltage, "Voltage", 12.6),
        ]);

        Assert.Empty(keys);
    }

    [Theory]
    [InlineData("CPU Fan", "cpu_fan")]
    [InlineData("Fan #1", "fan_1")]
    [InlineData("  Pump--A  ", "pump_a")]
    [InlineData("Chassis1", "chassis1")]
    public void Slug(string label, string slug) => Assert.Equal(slug, HardwareKeyMap.Slug(label));
}
