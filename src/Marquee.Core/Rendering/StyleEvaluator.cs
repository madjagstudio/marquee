using Marquee.Core.Sensors;
using Marquee.Core.Themes;

namespace Marquee.Core.Rendering;

static class StyleEvaluator
{
    /// The widget's style this frame: what its group passes down, its own style, then each matching `when` rule in order.
    /// `part` is the style of one part of the widget, such as a donut's slice: it goes over the widget's own style and
    /// under the rules, so a rule changes every part.
    public static StyleSpec Effective(WidgetSpec widget, StyleSpec inherited, SensorValue? subject, bool stale, StyleSpec? part = null)
    {
        var style = StyleSpec.Merge(inherited, widget.Style);
        if (part is not null) style = StyleSpec.Merge(style, part);
        foreach (var rule in widget.When)
            if (rule.Matches(subject, stale))
                style = StyleSpec.Merge(style, rule.Style);
        return style;
    }

    public static float Opacity(StyleSpec style, TimeSpan time)
    {
        var opacity = style.Opacity ?? StyleDefaults.Opacity;
        if (style.Pulse is { } pulse && (pulse.Target ?? StyleDefaults.PulseProperty) == PulseTarget.Opacity)
            opacity *= PulseFactor(pulse, time);
        return Math.Clamp(opacity, 0, 1);
    }

    public static float GlowRadius(StyleSpec style, TimeSpan time)
    {
        var radius = style.Glow?.Radius ?? StyleDefaults.GlowRadius;
        if (style.Pulse is { Target: PulseTarget.Glow } pulse)
            radius *= PulseFactor(pulse, time);
        return radius;
    }

    /// Max at the start of each period, easing down to Min halfway through and back.
    public static float PulseFactor(PulseSpec pulse, TimeSpan time)
    {
        var period = pulse.Period ?? StyleDefaults.PulsePeriodMs;
        var min = pulse.Min ?? StyleDefaults.PulseMin;
        var max = pulse.Max ?? StyleDefaults.PulseMax;
        var phase = time.TotalMilliseconds % period / period;
        var wave = 0.5 + 0.5 * Math.Cos(2 * Math.PI * phase);
        return (float)(min + (max - min) * wave);
    }
}
