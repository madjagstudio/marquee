using Marquee.Core.Sensors;

namespace Marquee.Core.Themes;

public enum WhenCondition { Above, Below, Equals, Stale }

/// A conditional style. Every matching rule merges its style on top, in the order the rules are written.
/// Number holds the threshold for above/below; equals keeps both the text and, if it parses, the number.
public sealed record WhenRule(WhenCondition Condition, double Number, string? Text, StyleSpec Style)
{
    public bool Matches(SensorValue? value, bool stale)
    {
        if (Condition == WhenCondition.Stale) return stale;
        if (stale || value is not { } v) return false;

        return Condition switch
        {
            WhenCondition.Above => v.Number > Number,
            WhenCondition.Below => v.Number < Number,
            _ => v.Number is { } n ? Math.Abs(n - Number) < 1e-9 : v.Text == Text,
        };
    }
}
