using Marquee.Core.Expressions;

namespace Marquee.Core.Themes;

/// The sensor keys a theme reads, for the theme editor's test values panel.
public static class ThemeKeys
{
    /// Every key the spec reads, each once, by part and then in the order of first use within each part: shader
    /// uniforms, then the widgets in order (through their groups, hidden ones included, since unhiding one shouldn't
    /// change the list), then the ring. So it isn't layout.yaml's order where the two differ: a uniform's key comes
    /// first wherever the background is written.
    /// A widget's source comes before its own templates, series and slices. `when` rules compare the source, and a value
    /// widget's `{0}` is its source, so both are covered by it. `now` is the clock, not a key, and isn't listed.
    public static IReadOnlyList<string> Of(ThemeSpec spec)
    {
        var keys = new List<string>();
        Add(keys, spec);
        return keys;
    }

    /// True when the spec reads `now`, the clock, anywhere Of looks: a theme that shows the time, or counts down to one,
    /// changes each second though no key does.
    public static bool ReadsClock(ThemeSpec spec)
    {
        var keys = new KeyList();
        Add(keys, spec);
        return keys.ReadsClock;
    }

    static void Add(List<string> keys, ThemeSpec spec)
    {
        foreach (var layer in spec.Background.OfType<ShaderLayer>())
            foreach (var uniform in layer.Uniforms)
                foreach (var value in uniform.Values)
                    value.AddKeys(keys);
        Add(keys, spec.Widgets);
        spec.Ring?.Source?.AddKeys(keys);
    }

    static void Add(List<string> keys, IEnumerable<WidgetSpec> widgets)
    {
        foreach (var widget in widgets)
        {
            widget.Source?.AddKeys(keys);
            switch (widget)
            {
                case TextWidget text:
                    text.Text.AddKeys(keys);
                    break;
                case SpansWidget spans:
                    foreach (var span in spans.Spans) span.Text.AddKeys(keys);
                    break;
                case ValueWidget { Spans: { } spans }:
                    foreach (var span in spans) span.Text.AddKeys(keys);
                    break;
                case GraphWidget graph:
                    foreach (var series in graph.Series)
                        if (!keys.Contains(series.Key)) keys.Add(series.Key);
                    break;
                case DonutWidget donut:
                    foreach (var slice in donut.Slices) slice.Source.AddKeys(keys);
                    break;
                case GroupWidget group:
                    Add(keys, group.Children);
                    break;
            }
        }
    }
}
