namespace Marquee.Editor.Model.Values;

/// The graph history a pinned number fills in, oldest point first. Every pattern ends on the number, so a graph's
/// newest point agrees with what the key's other widgets show.
static class HistoryPatterns
{
    /// One spike every this many points.
    const int SpikeEvery = 8;

    public static double[] Fill(HistoryPattern pattern, double value, SliderRange range, int points)
    {
        if (points <= 1) return [value];
        var history = new double[points];
        switch (pattern)
        {
            case HistoryPattern.Ramp:
                var from = Math.Min(range.Min, value);
                for (var i = 0; i < points; i++)
                    history[i] = from + (value - from) * i / (points - 1);
                break;
            case HistoryPattern.Spikes:
                // Toward the far end of the range: up from a low or middling value, down from one at the top.
                var reach = (range.Max - range.Min) / 2;
                var spike = value + reach <= range.Max ? value + reach : Math.Max(range.Min, value - reach);
                for (var i = 0; i < points; i++)
                    history[i] = (points - 1 - i) % SpikeEvery == SpikeEvery / 2 ? spike : value;
                break;
            default:
                Array.Fill(history, value);
                break;
        }

        return history;
    }
}
