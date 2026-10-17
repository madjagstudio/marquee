using Marquee.Core.Sensors;
using Marquee.Core.Themes;

namespace Marquee.Core.Rendering;

static class StyleEvaluator
{
    /// The widget's style this frame: what its group passes down, its own style, then each matching `when` rule in order.
    public static StyleSpec Effective(WidgetSpec widget, StyleSpec inherited, SensorValue? subject, bool stale)
    {
        var style = StyleSpec.Merge(inherited, widget.Style);
        foreach (var rule in widget.When)
            if (rule.Matches(subject, stale))
                style = StyleSpec.Merge(style, rule.Style);
        return style;
    }

    public static float Opacity(StyleSpec style, TimeSpan time)
    {
        var opacity = style.Opacity ?? 1f;
        if (style.Pulse is { } pulse && (pulse.Target ?? PulseTarget.Opacity) == PulseTarget.Opacity)
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
