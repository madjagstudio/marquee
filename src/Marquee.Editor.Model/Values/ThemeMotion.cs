using Marquee.Core.Sensors;
using Marquee.Core.Themes;

namespace Marquee.Editor.Model.Values;

/// What in a theme moves with time rather than with its values, so the editor knows when to draw at the theme's fps
/// and when a frame per change, and per second, is enough. Hidden widgets and layers don't move.
public sealed class ThemeMotion
{
    readonly IReadOnlyList<Pulsing> _pulses;

    ThemeMotion(bool video, bool shader, TimeSpan smoothing, IReadOnlyList<Pulsing> pulses, bool clock)
    {
        Video = video;
        Shader = shader;
        Smoothing = smoothing;
        _pulses = pulses;
        Clock = clock;
    }

    public static ThemeMotion None { get; } = new(false, false, TimeSpan.Zero, [], false);

    /// A video layer or widget: it always plays.
    public bool Video { get; }

    /// A shader layer: its `time` always runs.
    public bool Shader { get; }

    /// The longest `smooth` of any widget: after its values change, the theme moves for this long.
    public TimeSpan Smoothing { get; }

    /// The theme reads `now` (ThemeKeys.ReadsClock): a clock it shows, or a countdown, moves on each second though no
    /// value does, so the canvas is drawn once a second, not only per change.
    public bool Clock { get; }

    public static ThemeMotion Of(ThemeSpec? spec)
    {
        if (spec is null) return None;
        var video = false;
        var shader = false;
        foreach (var layer in spec.Background.Where(layer => !layer.Hidden))
        {
            video |= layer is VideoLayer;
            shader |= layer is ShaderLayer;
        }

        var pulses = new List<Pulsing>();
        var smoothing = 0f;
        Read(spec.Widgets, ref video, ref smoothing, pulses);
        return new ThemeMotion(video, shader, TimeSpan.FromMilliseconds(smoothing), pulses, ThemeKeys.ReadsClock(spec));
    }

    /// True while a widget pulses with these values: its own style pulses, or a `when` rule that pulses matches.
    public bool Pulses(SensorSnapshot values) => _pulses.Any(pulse => pulse.Moves(values));

    /// True while something moves whatever the values do next: a video, a shader or a pulse.
    public bool MovesOnItsOwn(SensorSnapshot values) => Video || Shader || Pulses(values);

    static void Read(IEnumerable<WidgetSpec> widgets, ref bool video, ref float smoothing, List<Pulsing> pulses)
    {
        foreach (var widget in widgets.Where(widget => !widget.Hidden))
        {
            video |= widget is VideoWidget;
            smoothing = Math.Max(smoothing, widget.SmoothMs);
            var rules = widget.When.Where(rule => rule.Style.Pulse is not null).ToList();
            if (widget.Style.Pulse is not null || rules.Count > 0)
                pulses.Add(new Pulsing(widget, widget.Style.Pulse is not null, rules));
            if (widget is GroupWidget group)
                Read(group.Children, ref video, ref smoothing, pulses);
        }
    }

    /// A widget that pulses always, or while one of its rules matches. Whether a rule matches is worked out as the
    /// renderer does (WidgetPainter): against the widget's source, stale when the source has no value or the text
    /// reads a missing key.
    sealed record Pulsing(WidgetSpec Widget, bool Always, IReadOnlyList<WhenRule> Rules)
    {
        public bool Moves(SensorSnapshot values)
        {
            if (Always) return true;
            var subject = Widget.Source?.Evaluate(values);
            var stale = (Widget.Source is not null && subject is null) || TextStale(values);
            return Rules.Any(rule => rule.Matches(subject, stale));
        }

        bool TextStale(SensorSnapshot values) => Widget switch
        {
            TextWidget text => text.Text.Render(values).Stale,
            SpansWidget spans => spans.Spans.Any(span => span.Text.Render(values).Stale),
            _ => false,
        };
    }
}
