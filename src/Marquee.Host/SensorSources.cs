using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Hardware;

namespace Marquee.Host;

/// `--sensors lhm|hwinfo|none` and config.yaml's `sensors:`: which hardware source feeds the sensor store. The names
/// are SensorSourceNames.
public static class SensorSources
{
    /// Null for "none", or a name that isn't a source.
    public static ISensorProvider? Create(string name, ILog log)
    {
        switch (name)
        {
            case SensorSourceNames.LibreHardwareMonitor:
                foreach (var problem in HardwareEnvironment.Problems())
                    log.Warn(problem);
                return new LibreHardwareMonitorProvider();
            case SensorSourceNames.HwInfo:
                return new HwInfoProvider();
            default:
                return null;
        }
    }
}
