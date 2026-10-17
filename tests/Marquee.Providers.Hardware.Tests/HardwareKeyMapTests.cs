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
                ["ram.used_pct"] = 55, ["ram.used"] = 17e9, ["fan.gpu_fan_1"] = 1600, ["fan.fan_1"] = 1100,
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

    [Theory]
    [InlineData("CPU Fan", "cpu_fan")]
    [InlineData("Fan #1", "fan_1")]
    [InlineData("  Pump--A  ", "pump_a")]
    [InlineData("Chassis1", "chassis1")]
    public void Slug(string label, string slug) => Assert.Equal(slug, HardwareKeyMap.Slug(label));
}
