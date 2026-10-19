namespace Marquee.Core.Sensors;

/// The hardware sensor sources, as `--sensors` and config.yaml's `sensors:` spell them, kept in one list so both accept
/// the same names.
public static class SensorSourceNames
{
    public const string LibreHardwareMonitor = "lhm";
    public const string HwInfo = "hwinfo";
    public const string None = "none";

    /// What runs when neither the command line nor config.yaml picks a source.
    public const string Default = LibreHardwareMonitor;

    /// In the order help text and error messages list them.
    public static IReadOnlyList<string> All { get; } = [LibreHardwareMonitor, HwInfo, None];

    /// Names are matched exactly, lower case, as they are written in the usage text.
    public static bool IsValid(string? name) => name is not null && All.Contains(name, StringComparer.Ordinal);
}
