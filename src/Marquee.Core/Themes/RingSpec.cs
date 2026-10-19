using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using SkiaSharp;

namespace Marquee.Core.Themes;

public enum RingEffect { Static, Breathe, Spin, Rainbow }

/// What the screen's LED ring shows: a theme's `ring:` block. Brightness is 0 to 1 (layout.yaml says 0 to 100) and
/// Speed multiplies each effect's pace (0.1 to 10). Source is what the `when` rules compare, as for a widget.
public sealed record RingSpec(SKColor Color, RingEffect Effect, float Speed, float Brightness, Expression? Source,
    IReadOnlyList<RingRule> When)
{
    /// The ring of a theme without a `ring:` block: a soft static white. LEDs at full white look cold and bluish next to
    /// the screen, so it is tinted slightly warm, and 40% keeps it a glow around the screen rather than a light that
    /// competes with it. A `ring:` block's missing fields don't come from here (see RingParser): a theme that writes a
    /// color gets it at full brightness.
    public static readonly RingSpec Default = new(new SKColor(0xFF, 0xF1, 0xE0), RingEffect.Static, 1, 0.4f, null, []);
}

/// A ring `when` rule: the widgets' conditions, with the ring fields to change when it matches. Every matching rule
/// applies in the order the rules are written, so a later one wins for each field it sets.
public sealed record RingRule(WhenCondition Condition, double Number, string? Text, SKColor? Color, RingEffect? Effect,
    float? Speed, float? Brightness)
{
    public bool Matches(SensorValue? value, bool stale) => WhenRule.Matches(Condition, Number, Text, value, stale);
}
