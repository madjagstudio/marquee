using Marquee.Core.Sensors;

namespace Marquee.Core.Themes;

public enum WhenCondition { Above, Below, Equals, Stale }

/// A conditional style. Every matching rule merges its style on top, in the order the rules are written.
/// Number holds the threshold for above/below; equals keeps both the text and, if it parses, the number.
public sealed record WhenRule(WhenCondition Condition, double Number, string? Text, StyleSpec Style)
{
    public bool Matches(SensorValue? value, bool stale) => Matches(Condition, Number, Text, value, stale);

    /// Also the test for the LED ring's rules, so a condition means the same on the ring as on a widget.
    internal static bool Matches(WhenCondition condition, double number, string? text, SensorValue? value, bool stale)
    {
        if (condition == WhenCondition.Stale) return stale;
        if (stale || value is not { } v) return false;

        return condition switch
        {
            WhenCondition.Above => v.Number > number,
            WhenCondition.Below => v.Number < number,
            _ => v.Number is { } n ? Math.Abs(n - number) < 1e-9 : v.Text == text,
        };
    }
}
