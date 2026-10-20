using Marquee.Core.Themes;

namespace Marquee.Editor.Model.Values;

/// The test values panel's model: every key the theme reads, then `now`; the pins; and, for each key, its slider range
/// and whether a graph draws it. Pins are kept by key, so they outlive a theme change that drops the key and comes back
/// to it, and are never saved: they end with the session.
///
/// Thread-safe: the panel changes it on the UI thread while the value sources read it from their poll.
public sealed class TestValues
{
    /// The clock: pinned with PinNow, to a time rather than a number.
    public const string Now = "now";

    readonly object _gate = new();
    volatile Shape _shape = Shape.Empty;
    Dictionary<string, TestPin> _pins = new(StringComparer.Ordinal);
    DateTimeOffset? _pinnedNow;

    /// Raised after the list or a pin changes, on the thread that changed it.
    public event EventHandler? Changed;

    /// The keys the theme reads (ThemeKeys.Of), in its order, then `now`.
    public IReadOnlyList<string> Keys => _shape.Keys;

    /// The pins, by key; `now` is PinnedNow instead.
    public IReadOnlyDictionary<string, TestPin> Pins
    {
        get
        {
            lock (_gate) return _pins;
        }
    }

    /// The time `now` is pinned to; null when it follows the PC's clock.
    public DateTimeOffset? PinnedNow
    {
        get
        {
            lock (_gate) return _pinnedNow;
        }
    }

    /// Reads the keys, slider ranges and graphs of the theme now open; null for none. Pins stay.
    public void SetTheme(ThemeSpec? spec)
    {
        _shape = spec is null ? Shape.Empty : Shape.Of(spec);
        Raise();
    }

    /// The key's slider: from the gauges and bars whose source reads it (all of their ranges together), otherwise 0 to
    /// 100.
    public SliderRange RangeOf(string key) => _shape.Ranges.GetValueOrDefault(key, SliderRange.Default);

    public bool IsGraphKey(string key) => _shape.GraphPoints.ContainsKey(key);

    /// How many points the longest graph drawing the key shows; 0 when no graph draws it.
    public int GraphPoints(string key) => _shape.GraphPoints.GetValueOrDefault(key);

    /// The key's pin; null when it isn't pinned.
    public TestPin? PinOf(string key)
    {
        lock (_gate) return _pins.GetValueOrDefault(key);
    }

    /// Pins the key, replacing any pin it has. Throws ArgumentException for `now`, which takes PinNow, and for a number
    /// that isn't finite.
    public void Pin(string key, TestPin pin)
    {
        if (key == Now) throw new ArgumentException("Pin now to a time with PinNow.", nameof(key));
        if (pin.Kind == PinKind.Number && !double.IsFinite(pin.Number))
            throw new ArgumentException("A pinned number must be finite.", nameof(pin));
        lock (_gate) _pins = new(_pins, StringComparer.Ordinal) { [key] = pin };
        Raise();
    }

    /// Stops `now` following the PC's clock: the canvas shows this time until it is unpinned.
    public void PinNow(DateTimeOffset at)
    {
        lock (_gate) _pinnedNow = at;
        Raise();
    }

    /// Takes the key's pin away, `now` included; false when it had none.
    public bool Unpin(string key)
    {
        lock (_gate)
        {
            if (key == Now)
            {
                if (_pinnedNow is null) return false;
                _pinnedNow = null;
            }
            else
            {
                if (!_pins.ContainsKey(key)) return false;
                var pins = new Dictionary<string, TestPin>(_pins, StringComparer.Ordinal);
                pins.Remove(key);
                _pins = pins;
            }
        }

        Raise();
        return true;
    }

    public void UnpinAll()
    {
        lock (_gate)
        {
            _pins = new(StringComparer.Ordinal);
            _pinnedNow = null;
        }

        Raise();
    }

    /// The history a number pin gives the key: its pattern across the longest graph drawing the key, or the number
    /// alone when no graph does.
    public IReadOnlyList<double> HistoryOf(string key, TestPin pin) =>
        HistoryPatterns.Fill(pin.Pattern, pin.Number, RangeOf(key), GraphPoints(key));

    void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    /// What a theme says about its keys, made once per theme and replaced whole, so a reader never sees half of one.
    sealed record Shape(IReadOnlyList<string> Keys, IReadOnlyDictionary<string, SliderRange> Ranges,
        IReadOnlyDictionary<string, int> GraphPoints)
    {
        public static Shape Empty { get; } = new([Now], new Dictionary<string, SliderRange>(), new Dictionary<string, int>());

        public static Shape Of(ThemeSpec spec)
        {
            var ranges = new Dictionary<string, SliderRange>(StringComparer.Ordinal);
            var graphs = new Dictionary<string, int>(StringComparer.Ordinal);
            Read(spec.Widgets, ranges, graphs);
            var keys = ThemeKeys.Of(spec).Where(key => key != Now).Append(Now).ToList();
            return new Shape(keys, ranges, graphs);
        }

        /// Hidden widgets count, as they do in ThemeKeys: unhiding one shouldn't change a slider.
        static void Read(IEnumerable<WidgetSpec> widgets, Dictionary<string, SliderRange> ranges, Dictionary<string, int> graphs)
        {
            foreach (var widget in widgets)
            {
                switch (widget)
                {
                    case GaugeWidget gauge:
                        Widen(ranges, gauge, new SliderRange(gauge.Min, gauge.Max));
                        break;
                    case BarWidget bar:
                        Widen(ranges, bar, new SliderRange(bar.Min, bar.Max));
                        break;
                    case GraphWidget graph:
                        foreach (var series in graph.Series)
                            graphs[series.Key] = Math.Max(graphs.GetValueOrDefault(series.Key), graph.Points);
                        break;
                    case GroupWidget group:
                        Read(group.Children, ranges, graphs);
                        break;
                }
            }
        }

        static void Widen(Dictionary<string, SliderRange> ranges, WidgetSpec widget, SliderRange range)
        {
            if (widget.Source is null) return;
            foreach (var key in widget.Source.Keys)
                ranges[key] = ranges.TryGetValue(key, out var wider) ? wider.Union(range) : range;
        }
    }
}
