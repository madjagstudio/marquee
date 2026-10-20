using Marquee.Cli.Tests.TestSupport;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Hardware;

namespace Marquee.Cli.Tests;

/// `marquee sensors`: its flags, and the raw listing `--raw` adds. The command itself reads the hardware, so these test
/// its parts; the raw listing tests capture the console, so they run in its collection.
[Collection(nameof(ConsoleCapture))]
public class SensorsCommandTests
{
    [Fact]
    public void Every_flag_lands_in_the_options()
    {
        var options = SensorsCommand.Parse(["--sensors", "hwinfo", "--no-claude-usage", "--raw", "--config", "my.yaml"]);

        Assert.Equal(new SensorsCommand.Options { Sensors = "hwinfo", ClaudeUsage = false, Raw = true, ConfigFile = "my.yaml" }, options);
    }

    [Fact]
    public void Without_flags_everything_comes_from_config_yaml_and_raw_is_off()
    {
        Assert.Equal(new SensorsCommand.Options(), SensorsCommand.Parse([]));
        Assert.False(SensorsCommand.Parse([])!.Raw);
    }

    [Theory]
    [InlineData("--sensors", "aida64")]
    [InlineData("--sensors")]
    [InlineData("--config")]
    [InlineData("--raw", "yes")]
    [InlineData("--unknown")]
    public void A_bad_flag_means_usage(params string[] args)
    {
        Assert.Null(SensorsCommand.Parse(args));
    }

    [Fact]
    public void Usage_shows_the_command()
    {
        Assert.Contains("marquee sensors [--sensors lhm|hwinfo|none] [--no-claude-usage] [--raw] [--config <file>]", Strings.Usage);
    }


    static RawReading R(HardwareKind hardware, string device, string type, string label, double value, string unit = "") =>
        new(hardware, device, type, label, value, unit);

    [Fact]
    public void Raw_lines_are_sorted_by_hardware_then_device_then_type_then_label()
    {
        var lines = SensorsCommand.RawLines(
        [
            R(HardwareKind.Other, "Nuvoton NCT6799D", "Voltage", "Vcore", 1.37),
            R(HardwareKind.Gpu, "AMD Radeon RX 7900 XTX", "Fan", "GPU Fan", 1450),
            R(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Voltage", "SoC (SVI2 TFN)", 1.1),
            R(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Voltage", "core (SVI2 TFN)", 1.356),
            R(HardwareKind.Cpu, "AMD Ryzen 7 5800X", "Temperature", "Core (Tctl/Tdie)", 68.25),
            R(HardwareKind.Gpu, "AMD Radeon RX 7900 XTX", "Voltage", "GPU Core", 1.05),
            R(HardwareKind.Gpu, "AMD Radeon(TM) Graphics", "Voltage", "GPU Core", 0.7),
            R(HardwareKind.Gpu, "AMD Radeon RX 7900 XTX", "Control", "GPU Fan", 35),
            R(HardwareKind.Memory, "Total Memory", "Data", "Memory Used", 16),
        ]);

        // A source's devices keep its order, which is the order the key map prefers them in.
        Assert.Equal(
            [
                "Cpu    Temperature Core (Tctl/Tdie)                 68.25          AMD Ryzen 7 5800X",
                "Cpu    Voltage     core (SVI2 TFN)                  1.356          AMD Ryzen 7 5800X",
                "Cpu    Voltage     SoC (SVI2 TFN)                   1.1            AMD Ryzen 7 5800X",
                "Gpu    Control     GPU Fan                          35             AMD Radeon RX 7900 XTX",
                "Gpu    Fan         GPU Fan                          1450           AMD Radeon RX 7900 XTX",
                "Gpu    Voltage     GPU Core                         1.05           AMD Radeon RX 7900 XTX",
                "Gpu    Voltage     GPU Core                         0.7            AMD Radeon(TM) Graphics",
                "Memory Data        Memory Used                      16             Total Memory",
                "Other  Voltage     Vcore                            1.37           Nuvoton NCT6799D",
            ],
            lines);
    }

    [Fact]
    public void Raw_values_carry_the_sources_unit_and_look_the_same_in_every_culture()
    {
        using var culture = new CultureScope("de-DE");

        var line = Assert.Single(SensorsCommand.RawLines([R(HardwareKind.Cpu, "CPU [#0]: Intel Core i7-12700K", "Voltage", "Vcore", 1.2345678, "V")]));

        Assert.Equal("Cpu    Voltage     Vcore                            1.2346 V       CPU [#0]: Intel Core i7-12700K", line);
    }

    /// A source that forgets everything once disposed, which IRawReadings allows.
    sealed class FakeSource(params RawReading[] readings) : ISensorProvider, IRawReadings
    {
        public bool Disposed { get; private set; }
        public string Name => Disposed ? "" : "Hardware sensors (fake)";
        public TimeSpan Interval => TimeSpan.FromSeconds(1);
        public IReadOnlyList<RawReading> LastReadings => Disposed ? [] : readings;
        public void Poll(SensorStore store) => store.Set("cpu.voltage", 1.25, TimeSpan.FromSeconds(5));
        public void Dispose() => Disposed = true;
    }

    sealed class RecordingLog : ILog
    {
        public List<string> Entries { get; } = [];
        public void Info(string message) => Entries.Add(message);
        public void Warn(string message) => Entries.Add(message);
        public void Error(string message) => Entries.Add(message);
    }

    [Fact]
    public void The_sources_name_and_readings_are_taken_before_it_is_closed()
    {
        var source = new FakeSource(R(HardwareKind.Cpu, "CPU", "Voltage", "CPU Core", 1.25));
        var store = new SensorStore();

        var hardware = SensorsCommand.PollHardware(source, store, new RecordingLog(), twice: false);

        Assert.True(source.Disposed);
        Assert.Equal("Hardware sensors (fake)", hardware!.Source);
        Assert.Equal([R(HardwareKind.Cpu, "CPU", "Voltage", "CPU Core", 1.25)], hardware.Readings);
        Assert.True(store.Snapshot().TryGet("cpu.voltage", out _));
    }

    [Fact]
    public void Raw_prints_the_hardware_sources_readings_under_a_heading()
    {
        var log = new RecordingLog();
        var hardware = new SensorsCommand.HardwareListing("Hardware sensors (fake)",
        [
            R(HardwareKind.Gpu, "NVIDIA GeForce RTX 4080", "Voltage", "GPU Core Voltage", 0.95),
            R(HardwareKind.Cpu, "Intel Core i7-12700K", "Voltage", "CPU Core", 1.25),
        ]);
        using var console = new ConsoleCapture();

        SensorsCommand.WriteRaw(hardware, log);

        Assert.Equal(
            string.Join(Environment.NewLine,
                "",
                "Raw readings from Hardware sensors (fake):",
                "Cpu    Voltage     CPU Core                         1.25           Intel Core i7-12700K",
                "Gpu    Voltage     GPU Core Voltage                 0.95           NVIDIA GeForce RTX 4080",
                ""),
            console.Out);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Raw_without_a_hardware_source_says_there_is_nothing_to_list()
    {
        var log = new RecordingLog();
        using var console = new ConsoleCapture();

        SensorsCommand.WriteRaw(new SensorsCommand.HardwareListing(null, null), log);

        Assert.Equal("", console.Out);
        Assert.Equal(["There is no hardware source (sensors: none), so --raw has no readings to list."], log.Entries);
    }
}
