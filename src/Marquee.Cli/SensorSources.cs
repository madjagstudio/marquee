using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Hardware;

/// `--sensors lhm|hwinfo|none`: which hardware source feeds the sensor store.
static class SensorSources
{
    public const string Default = "lhm";

    public static bool IsValid(string? name) => name is "lhm" or "hwinfo" or "none";

    public static ISensorProvider? Create(string name, ILog log)
    {
        switch (name)
        {
            case "lhm":
                foreach (var problem in HardwareEnvironment.Problems())
                    log.Warn(problem);
                return new LibreHardwareMonitorProvider();
            case "hwinfo":
                return new HwInfoProvider();
            default:
                return null;
        }
    }
}
