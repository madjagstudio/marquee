using System.Globalization;

namespace Marquee.Core.Sensors;

/// A sensor reading: a number (temperatures, percentages, byte counts, Unix-second timestamps) or a string.
public readonly record struct SensorValue
{
    SensorValue(double? number, string? text)
    {
        Number = number;
        Text = text;
    }

    public double? Number { get; }
    public string? Text { get; }

    public static SensorValue Of(double number) => new(number, null);
    public static SensorValue Of(string text) => new(null, text);

    public override string ToString() =>
        Number is { } n ? n.ToString(CultureInfo.InvariantCulture) : Text ?? "";
}
