using LibreHardwareMonitor.Hardware;
using Marquee.Core.Sensors;

namespace Marquee.Providers.Hardware;

/// CPU, GPU, memory and fan sensors through LibreHardwareMonitorLib (MPL-2.0, used as a library). CPU temperatures
/// and motherboard sensors also need administrator rights and the PawnIO driver; see HardwareEnvironment.
public sealed class LibreHardwareMonitorProvider : ISensorProvider, IRawReadings
{
    /// Values go stale if the provider stops updating them for this long.
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    Computer? _computer;

    public string Name => "Hardware sensors (LibreHardwareMonitor)";
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public IReadOnlyList<RawReading> LastReadings { get; private set; } = [];

    public void Poll(SensorStore store)
    {
        try
        {
            _computer ??= Open();
            foreach (var hardware in _computer.Hardware)
                Update(hardware);

            // Physical memory only, and the GPU with the most dedicated memory first: on a Ryzen with integrated
            // graphics both GPUs are GpuAmd, and the integrated one is listed first.
            var ordered = _computer.Hardware
                .Where(h => !(h.HardwareType == HardwareType.Memory && h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(h => Kind(h.HardwareType) == HardwareKind.Gpu ? 1 : 0)
                .ThenByDescending(DedicatedMemory);

            var readings = new List<HardwareReading>();
            var raw = new List<RawReading>();
            foreach (var hardware in ordered)
                Collect(hardware, Kind(hardware.HardwareType), readings, raw);

            LastReadings = raw;
            foreach (var (key, value) in HardwareKeyMap.Map(readings))
                store.Set(key, value, Ttl);
        }
        catch
        {
            Close(); // Reopen from scratch on the next poll.
            throw;
        }
    }

    public void Dispose() => Close();

    static Computer Open()
    {
        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
        computer.Open();
        return computer;
    }

    void Close()
    {
        _computer?.Close();
        _computer = null;
    }

    static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            Update(sub);
    }

    /// Sub-hardware (the motherboard's Super I/O chip, where case fans live) is read as part of its parent.
    static void Collect(IHardware hardware, HardwareKind kind, List<HardwareReading> readings, List<RawReading> raw)
    {
        Add(hardware.Name, kind, hardware.Sensors.Select(s => (s.SensorType, s.Name, s.Value)), readings, raw);
        foreach (var sub in hardware.SubHardware)
            Collect(sub, kind, readings, raw);
    }

    /// One device's sensors. Each with a value goes to `raw` as LibreHardwareMonitor reports it; those the key map
    /// takes also go to `readings`, in canonical units.
    internal static void Add(string device, HardwareKind kind, IEnumerable<(SensorType Type, string Name, float? Value)> sensors,
        List<HardwareReading> readings, List<RawReading> raw)
    {
        foreach (var sensor in sensors)
        {
            if (sensor.Value is not { } value) continue;
            raw.Add(new RawReading(kind, device, sensor.Type.ToString(), sensor.Name, value));
            (ReadingKind Kind, double Scale)? reading = sensor.Type switch
            {
                SensorType.Temperature => (ReadingKind.Temperature, 1),
                SensorType.Load => (ReadingKind.Load, 1),
                SensorType.Clock => (ReadingKind.Clock, 1),
                SensorType.Power => (ReadingKind.Power, 1),
                SensorType.Fan => (ReadingKind.Fan, 1),
                SensorType.Voltage => (ReadingKind.Voltage, 1),
                SensorType.Data => (ReadingKind.Bytes, 1024.0 * 1024 * 1024), // reported in GB
                SensorType.SmallData => (ReadingKind.Bytes, 1024.0 * 1024), // reported in MB
                _ => null,
            };
            if (reading is not { } r) continue;

            // Without administrator rights or PawnIO, LibreHardwareMonitor reports unreadable temperatures, clocks,
            // power and voltages as 0. Dropping them lets those keys go stale and show "—" instead of a false 0°C.
            if (value == 0 && r.Kind is ReadingKind.Temperature or ReadingKind.Clock or ReadingKind.Power or ReadingKind.Voltage) continue;

            readings.Add(new HardwareReading(kind, r.Kind, sensor.Name, value * r.Scale) { Device = device });
        }
    }

    static double DedicatedMemory(IHardware hardware) => hardware.Sensors
        .Where(s => s.SensorType == SensorType.SmallData && s.Name is "GPU Memory Total" or "D3D Dedicated Memory Total")
        .Select(s => (double)(s.Value ?? 0))
        .DefaultIfEmpty(0)
        .Max();

    static HardwareKind Kind(HardwareType type) => type switch
    {
        HardwareType.Cpu => HardwareKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareKind.Gpu,
        HardwareType.Memory => HardwareKind.Memory,
        _ => HardwareKind.Other,
    };
}
