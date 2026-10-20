using System.Text;

namespace Marquee.Providers.Hardware;

public enum HardwareKind { Cpu, Gpu, Memory, Other }

public enum ReadingKind { Temperature, Load, Clock, Power, Fan, Bytes, Voltage }

/// One raw reading from LibreHardwareMonitor or HWiNFO, already in canonical units: °C, %, MHz, W, RPM, bytes, V.
public sealed record HardwareReading(HardwareKind Hardware, ReadingKind Kind, string Label, double Value)
{
    /// The device the reading belongs to, as the source names it ("NVIDIA GeForce RTX 4080", "GPU [#1]: NVIDIA GeForce
    /// RTX 4080"), so the key map can tell two GPUs apart. Empty when the source doesn't say.
    public string Device { get; init; } = "";
}

/// Raw readings → Marquee's sensor keys. Both sources name sensors differently, so each key lists the labels
/// either source uses, in order of preference.
public static class HardwareKeyMap
{
    /// With `DeviceFirst`, the rule picks the preferred device that has any of its labels, then that device's best
    /// label (see PreferredGpu); otherwise the best label, then the first reading with it, on whatever device.
    sealed record Rule(string Key, HardwareKind Hardware, ReadingKind Kind, string[] Labels, bool DeviceFirst = false);

    static readonly Rule[] Rules =
    [
        new("cpu.temp", HardwareKind.Cpu, ReadingKind.Temperature, ["CPU Package", "Core (Tctl/Tdie)", "CPU (Tctl/Tdie)", "Core (Tctl)", "CPU (Tctl)", "Core Max", "Core Average"]),
        new("cpu.load", HardwareKind.Cpu, ReadingKind.Load, ["CPU Total", "Total CPU Usage"]),
        new("cpu.power", HardwareKind.Cpu, ReadingKind.Power, ["CPU Package", "Package", "CPU Package Power"]),
        new("gpu.temp", HardwareKind.Gpu, ReadingKind.Temperature, ["GPU Core", "GPU Temperature"]),
        new("gpu.load", HardwareKind.Gpu, ReadingKind.Load, ["GPU Core", "GPU Core Load", "GPU Utilization", "D3D 3D"]),
        new("gpu.clock", HardwareKind.Gpu, ReadingKind.Clock, ["GPU Core", "GPU Clock"]),
        new("gpu.power", HardwareKind.Gpu, ReadingKind.Power, ["GPU Package", "GPU Power"]),
        new("gpu.vram_used", HardwareKind.Gpu, ReadingKind.Bytes, ["GPU Memory Used", "GPU Memory Allocated", "GPU D3D Memory Dedicated", "D3D Dedicated Memory Used"]),
        new("ram.used_pct", HardwareKind.Memory, ReadingKind.Load, ["Memory", "Physical Memory Load"]),
        new("ram.used", HardwareKind.Memory, ReadingKind.Bytes, ["Memory Used", "Physical Memory Used"]),

        // cpu.voltage, from three rules in order: the CPU's own core voltage, then the motherboard's (its Super I/O
        // chip measures the same rail, less exactly), then the voltage the cores ask for (VID), which only tracks it.
        // LibreHardwareMonitor's labels are from LibreHardwareMonitorLib 0.9.6; HWiNFO's from its sensor lists.
        new("cpu.voltage", HardwareKind.Cpu, ReadingKind.Voltage,
        [
            "Core (SVI2 TFN)", // LibreHardwareMonitor, AMD Zen to Zen 3 (Amd17Cpu)
            "VDDCR", // LibreHardwareMonitor, AMD Zen 4 with PM table 0x540104, from the SMU's power table (RyzenSMU)
            "CPU Core", // LibreHardwareMonitor, Intel (IntelCpu); "CPU Core #1" and on are the cores' VIDs
            "CPU Core Voltage (SVI2 TFN)", // HWiNFO, AMD Zen 2 and Zen 3
            "CPU Core Voltage (SVI3 TFN)", // HWiNFO, AMD Zen 4 and later
            "VR VCC Voltage (SVID VOUT)", // HWiNFO, Intel, the core regulator's output
        ]),
        // LibreHardwareMonitor's Super I/O board configurations (SuperIOHardware) name it Vcore, VCore or CPU Vcore, and
        // its ASUS embedded controller CPU Core; HWiNFO's motherboard sensors name it Vcore.
        new("cpu.voltage", HardwareKind.Other, ReadingKind.Voltage, ["Vcore", "CPU Vcore", "CPU Core"]),
        new("cpu.voltage", HardwareKind.Cpu, ReadingKind.Voltage, ["Core VIDs (avg)"]), // HWiNFO

        // Device first: an integrated GPU reports a core voltage too, under a label that can rank above the card's
        // (an AMD integrated GPU's VDDCR_GFX beside an NVIDIA card in HWiNFO).
        new("gpu.voltage", HardwareKind.Gpu, ReadingKind.Voltage,
        [
            "GPU Core Voltage (VDDCR_GFX)", // HWiNFO, AMD
            "GPU Core Voltage", // LibreHardwareMonitor (NvidiaGpu) and HWiNFO, NVIDIA
            "GPU Core", // LibreHardwareMonitor, AMD and Intel (AmdGpu, IntelDiscreteGpu, IntelIntegratedGpu)
        ], DeviceFirst: true),
    ];

    /// CPU clock readings that aren't core clocks.
    static readonly string[] NotCoreClocks = ["bus", "ring", "uncore", "memory", "llc", "cache"];

    /// A GPU's dedicated memory, its size or what is in use, as either source names it. The GPU reporting the most is
    /// taken to be the card. LibreHardwareMonitor's provider orders its GPUs by the size already; HWiNFO reports only
    /// what is in use, and numbers its GPUs in its own order, which can put an integrated GPU first.
    static readonly string[] DedicatedMemory =
    [
        "GPU Memory Total", "D3D Dedicated Memory Total", // LibreHardwareMonitor, the size
        "GPU Memory Used", "D3D Dedicated Memory Used", // LibreHardwareMonitor
        "GPU Memory Allocated", "GPU D3D Memory Dedicated", // HWiNFO
    ];

    /// For each key the first matching reading wins, so sources list the preferred device first (a discrete GPU
    /// before an integrated one). A key with several rules takes the first rule that matches. gpu.voltage and gpu.fan
    /// take the preferred GPU first, then its best reading (see PreferredGpu); the older gpu.* keys match labels across
    /// GPUs as they always have. cpu.clock is the fastest core; every fan becomes fan.{slug of its label}.
    public static IReadOnlyDictionary<string, double> Map(IReadOnlyList<HardwareReading> readings)
    {
        var keys = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var rule in Rules)
        {
            if (keys.ContainsKey(rule.Key)) continue;
            var candidates = readings.Where(r => r.Hardware == rule.Hardware && r.Kind == rule.Kind).ToList();
            if (rule.DeviceFirst)
            {
                var labelled = candidates.Where(r => rule.Labels.Contains(r.Label, StringComparer.OrdinalIgnoreCase)).ToList();
                if (PreferredGpu(labelled, readings) is not { } device) continue;
                candidates = labelled.Where(r => r.Device == device).ToList();
            }

            foreach (var label in rule.Labels)
            {
                var match = candidates.FirstOrDefault(r => string.Equals(r.Label, label, StringComparison.OrdinalIgnoreCase));
                if (match is null) continue;
                keys[rule.Key] = match.Value;
                break;
            }
        }

        var coreClocks = readings
            .Where(r => r.Hardware == HardwareKind.Cpu && r.Kind == ReadingKind.Clock)
            .Where(r => !NotCoreClocks.Any(word => r.Label.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Value)
            .ToList();
        if (coreClocks.Count > 0)
            keys["cpu.clock"] = coreClocks.Max();

        foreach (var fan in readings.Where(r => r.Kind == ReadingKind.Fan))
            keys.TryAdd($"fan.{Slug(fan.Label)}", fan.Value);

        // Any label: LibreHardwareMonitor names GPU fans "GPU Fan" or "GPU Fan 1", HWiNFO "GPU Fan1", and an HWiNFO
        // user can rename them. The preferred GPU's first fan.
        var gpuFans = readings.Where(r => r.Hardware == HardwareKind.Gpu && r.Kind == ReadingKind.Fan).ToList();
        if (PreferredGpu(gpuFans, readings) is { } fanDevice)
            keys["gpu.fan"] = gpuFans.First(r => r.Device == fanDevice).Value;

        return keys;
    }

    /// Of the devices the candidate readings come from, the one reporting the most dedicated memory, which is the card
    /// beside an integrated GPU; devices reporting as much (none at all, say) keep the order the readings list them in,
    /// by each device's first reading. Null without candidates.
    static string? PreferredGpu(IReadOnlyList<HardwareReading> candidates, IReadOnlyList<HardwareReading> readings) =>
        readings.Select(r => r.Device)
            .Distinct(StringComparer.Ordinal)
            .Where(device => candidates.Any(r => r.Device == device))
            .OrderByDescending(device => readings
                .Where(r => r.Hardware == HardwareKind.Gpu && r.Kind == ReadingKind.Bytes && r.Device == device
                    && DedicatedMemory.Contains(r.Label, StringComparer.OrdinalIgnoreCase))
                .Select(r => r.Value)
                .DefaultIfEmpty(0)
                .Max())
            .Cast<string?>()
            .FirstOrDefault();

    /// "CPU Fan" → "cpu_fan", "Fan #1" → "fan_1".
    public static string Slug(string label)
    {
        var slug = new StringBuilder();
        foreach (var c in label.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '_') slug.Append('_');
        }

        return slug.ToString().TrimEnd('_');
    }
}
