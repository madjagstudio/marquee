using System.Text;

namespace Marquee.Providers.Hardware;

public enum HardwareKind { Cpu, Gpu, Memory, Other }

public enum ReadingKind { Temperature, Load, Clock, Power, Fan, Bytes }

/// One raw reading from LibreHardwareMonitor or HWiNFO, already in canonical units: °C, %, MHz, W, RPM, bytes.
public sealed record HardwareReading(HardwareKind Hardware, ReadingKind Kind, string Label, double Value);

/// Raw readings → Marquee's sensor keys. Both sources name sensors differently, so each key lists the labels
/// either source uses, in order of preference.
public static class HardwareKeyMap
{
    sealed record Rule(string Key, HardwareKind Hardware, ReadingKind Kind, string[] Labels);

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
    ];

    /// CPU clock readings that aren't core clocks.
    static readonly string[] NotCoreClocks = ["bus", "ring", "uncore", "memory", "llc", "cache"];

    /// For each key the first matching reading wins, so sources list the preferred device first (a discrete GPU
    /// before an integrated one). cpu.clock is the fastest core; every fan becomes fan.{slug of its label}.
    public static IReadOnlyDictionary<string, double> Map(IReadOnlyList<HardwareReading> readings)
    {
        var keys = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var rule in Rules)
        {
            foreach (var label in rule.Labels)
            {
                var match = readings.FirstOrDefault(r =>
                    r.Hardware == rule.Hardware && r.Kind == rule.Kind && string.Equals(r.Label, label, StringComparison.OrdinalIgnoreCase));
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

        return keys;
    }

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
