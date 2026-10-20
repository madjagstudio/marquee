using SkiaSharp;

namespace Marquee.Core.Themes;

/// Every visual property a widget can have. Null means "not set here", so styles can be layered:
/// a group's inherited properties, then the widget's style, then each matching `when` rule on top.
public sealed record StyleSpec
{
    public static readonly StyleSpec Empty = new();

    public string? Font { get; init; }
    public float? Size { get; init; }
    public int? Weight { get; init; }
    public SKColor? Color { get; init; }
    public PaintSpec? Fill { get; init; }
    public PaintSpec? Track { get; init; }
    public StrokeSpec? Stroke { get; init; }
    public ShadowSpec? Shadow { get; init; }
    public GlowSpec? Glow { get; init; }
    public PulseSpec? Pulse { get; init; }
    public float? Opacity { get; init; }
    public float? Radius { get; init; }
    public float? Padding { get; init; }
    public TextAlign? Align { get; init; }

    /// How the widget mixes with what's under it, as a background layer does. Anything but SrcOver draws the widget on
    /// a layer of its own.
    public SKBlendMode? Blend { get; init; }

    /// Px added between characters; negative tightens.
    public float? LetterSpacing { get; init; }

    /// How far apart the baselines of wrapped lines are, as a multiple of the size.
    public float? LineHeight { get; init; }

    /// Whether text too wide for its box breaks at spaces onto more lines. Off by default, so text that overflows its
    /// box draws on one line as it always has; a `\n` starts a new line either way.
    public bool? Wrap { get; init; }

    /// `over` wins wherever it sets a property. Stroke, shadow, glow and pulse merge field by field,
    /// so a rule can change a stroke's color and keep its width.
    public static StyleSpec Merge(StyleSpec under, StyleSpec over) => new()
    {
        Font = over.Font ?? under.Font,
        Size = over.Size ?? under.Size,
        Weight = over.Weight ?? under.Weight,
        Color = over.Color ?? under.Color,
        Fill = over.Fill ?? under.Fill,
        Track = over.Track ?? under.Track,
        Stroke = StrokeSpec.Merge(under.Stroke, over.Stroke),
        Shadow = ShadowSpec.Merge(under.Shadow, over.Shadow),
        Glow = GlowSpec.Merge(under.Glow, over.Glow),
        Pulse = PulseSpec.Merge(under.Pulse, over.Pulse),
        Opacity = over.Opacity ?? under.Opacity,
        Radius = over.Radius ?? under.Radius,
        Padding = over.Padding ?? under.Padding,
        Align = over.Align ?? under.Align,
        Blend = over.Blend ?? under.Blend,
        LetterSpacing = over.LetterSpacing ?? under.LetterSpacing,
        LineHeight = over.LineHeight ?? under.LineHeight,
        Wrap = over.Wrap ?? under.Wrap,
    };

    /// What a group passes down to its children. Box properties (fill, stroke, radius, ...) stay with the group.
    public StyleSpec Inherited() => new()
    {
        Font = Font, Size = Size, Weight = Weight, Color = Color, Align = Align, LetterSpacing = LetterSpacing, LineHeight = LineHeight,
        Wrap = Wrap,
    };
}

/// Values used where no style sets a property.
public static class StyleDefaults
{
    public const string Font = "Inter";
    public const float Size = 24;
    public const int Weight = 400;
    public static readonly SKColor Color = SKColors.White;
    public const float Opacity = 1;
    public const float Radius = 0;
    public const float Padding = 0;
    public const TextAlign Align = TextAlign.Left;
    public const SKStrokeCap StrokeCap = SKStrokeCap.Butt;
    public const float ShadowX = 2;
    public const float ShadowY = 2;
    public const float ShadowBlur = 0;
    public const PulseTarget PulseProperty = PulseTarget.Opacity;

    /// A graph's line, which is thicker than an outline and round at its ends (ThemeSchema's DefaultFor).
    public const float GraphStrokeWidth = 2;
    public const SKStrokeCap GraphStrokeCap = SKStrokeCap.Round;

    public const float StrokeWidth = 1;
    public const float GlowRadius = 8;
    public const float PulsePeriodMs = 1000;
    public const float PulseMin = 0.4f;
    public const float PulseMax = 1f;
    public const float LetterSpacing = 0;
    public const float LineHeight = 1.2f;
    public const bool Wrap = false;
}

public enum TextAlign { Left, Center, Right }

public sealed record StrokeSpec(float? Width = null, PaintSpec? Paint = null, SKStrokeCap? Cap = null)
{
    public static StrokeSpec? Merge(StrokeSpec? under, StrokeSpec? over) =>
        under is null ? over : over is null ? under : new(over.Width ?? under.Width, over.Paint ?? under.Paint, over.Cap ?? under.Cap);
}

public sealed record ShadowSpec(SKColor? Color = null, float? X = null, float? Y = null, float? Blur = null)
{
    public static ShadowSpec? Merge(ShadowSpec? under, ShadowSpec? over) =>
        under is null ? over : over is null ? under : new(over.Color ?? under.Color, over.X ?? under.X, over.Y ?? under.Y, over.Blur ?? under.Blur);
}

public sealed record GlowSpec(SKColor? Color = null, float? Radius = null)
{
    public static GlowSpec? Merge(GlowSpec? under, GlowSpec? over) =>
        under is null ? over : over is null ? under : new(over.Color ?? under.Color, over.Radius ?? under.Radius);
}

public enum PulseTarget { Opacity, Glow }

/// An oscillation between Min and Max (fractions of the normal opacity or glow radius) once every Period ms.
public sealed record PulseSpec(PulseTarget? Target = null, float? Period = null, float? Min = null, float? Max = null)
{
    public static PulseSpec? Merge(PulseSpec? under, PulseSpec? over) =>
        under is null ? over : over is null ? under : new(over.Target ?? under.Target, over.Period ?? under.Period, over.Min ?? under.Min, over.Max ?? under.Max);
}
