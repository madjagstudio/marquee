using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// A bar's or gauge's `segments` and `gap`. The room check needs the widget's length or sweep, so it runs once the
/// widget's own keys are read.
static class SegmentParser
{
    /// The keys bar and gauge widgets add for segments; ThemeSchema describes them, and its drift test reads this list.
    internal static readonly string[] Keys = ["segments", "gap"];

    /// The widget with its segments, if it has `segments`. A gap alone is checked but changes nothing.
    public static WidgetSpec Apply(Fields f, WidgetSpec widget, float width, float height)
    {
        var gapNode = f.Optional("gap");
        var gap = gapNode is { } g ? Yaml.Float(g, "gap", 0) : (float?)null;
        if (f.Optional("segments") is not { } countNode) return widget;

        var count = Yaml.Integer(countNode, "segments", SegmentSpec.MinCount, SegmentSpec.MaxCount);
        var at = gapNode ?? countNode;
        return widget switch
        {
            BarWidget bar => bar with
            {
                Segments = Bar(at, count, gap ?? SegmentSpec.DefaultBarGap, bar.Direction == BarDirection.Horizontal ? width : height),
            },
            GaugeWidget gauge => gauge with { Segments = Gauge(at, count, gap ?? SegmentSpec.DefaultGaugeGap, gauge.SweepAngle) },
            _ => widget,
        };
    }

    static SegmentSpec Bar(YamlNode at, int count, float gap, float length)
    {
        var need = Need(count, count - 1, gap);
        if (need > length)
            throw Yaml.Error(at, $"{count} segments of at least {Number(SegmentSpec.MinLength)} px with {Number(gap)} px gaps need "
                + $"{Number(need)} px, but this bar is {Number(length)} px long; use fewer segments or a smaller gap");
        return new SegmentSpec(count, gap);
    }

    /// A full ring has a gap at the join as well as between segments.
    static SegmentSpec Gauge(YamlNode at, int count, float gap, float sweep)
    {
        var ring = Math.Abs(sweep) >= 360;
        var need = Need(count, ring ? count : count - 1, gap);
        if (need > Math.Abs(sweep))
            throw Yaml.Error(at, $"{count} segments of at least {Number(SegmentSpec.MinLength)}° with {Number(gap)}° gaps need "
                + $"{Number(need)}°{(ring ? " (a full ring has a gap at the join too)" : "")}, but this gauge sweeps "
                + $"{Number(Math.Abs(sweep))}°; use fewer segments or a smaller gap");
        return new SegmentSpec(count, gap);
    }

    static double Need(int count, int gaps, float gap) => count * (double)SegmentSpec.MinLength + gaps * (double)gap;

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
