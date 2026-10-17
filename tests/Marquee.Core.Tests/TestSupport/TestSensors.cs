using System.Globalization;
using Marquee.Core.Sensors;

namespace Marquee.Core.Tests.TestSupport;

static class TestSensors
{
    /// The fixed wall-clock time every test snapshot is taken at.
    public static readonly DateTimeOffset Time = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static double UnixTime => Time.ToUnixTimeSeconds();

    /// A snapshot at the fixed time holding exactly these values (strings stay strings; anything else is a number).
    public static SensorSnapshot Of(params (string Key, object Value)[] values) => Store(values).Snapshot();

    public static SensorStore Store(params (string Key, object Value)[] values)
    {
        var store = new SensorStore(() => Time);
        foreach (var (key, value) in values)
        {
            if (value is string text) store.Set(key, text);
            else store.Set(key, Convert.ToDouble(value, CultureInfo.InvariantCulture));
        }

        return store;
    }
}
