namespace Marquee.Providers.Hardware;

/// One reading as the source reported it, before HardwareKeyMap: the hardware Marquee files its device under, the
/// device, the source's own name for the reading's type (LibreHardwareMonitor's SensorType, HWiNFO's reading type), its
/// label as the key map matches it, and its value in the source's own unit. HWiNFO names the unit; LibreHardwareMonitor
/// doesn't, its type implies it (Data is GB, SmallData MB).
public sealed record RawReading(HardwareKind Hardware, string Device, string Type, string Label, double Value, string Unit = "");

/// A hardware source that keeps its last poll's readings as the source reported them, before HardwareKeyMap picks keys
/// from them. `marquee sensors --raw` prints them, so the labels the key map looks for can be checked on a real PC.
public interface IRawReadings
{
    /// Every reading the last successful poll saw, in the source's order: those the key map leaves out included, such
    /// as a 0 that an unelevated LibreHardwareMonitor reports for a sensor it can't read, or a type no key takes.
    /// Empty until a poll succeeds.
    IReadOnlyList<RawReading> LastReadings { get; }
}
