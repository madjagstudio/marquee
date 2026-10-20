using System.Globalization;
using System.Text.RegularExpressions;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

public enum GradientKind { Linear, Radial }

/// The gradient editor's gradient: a paint written as `{ linear: [...], angle: 90 }` or `{ radial: [...], center:
/// [0.3, 0.4], radius: 0.8 }`, either with `stops`, wherever a key takes a paint (a style's fill, track, text_fill, a
/// stroke's or text stroke's color, a fill layer's fill). Each edit returns a new model; Write turns it into the
/// edits that change only the keys it changed, as one undo step.
///
/// Keys left out stay left out: Angle, Center and Radius are null when the file doesn't write them (Effective* gives
/// what draws), and Stops is null while the colors spread evenly.
public sealed partial record GradientModel
{
    static readonly SchemaBlock Common = ThemeSchema.Gradient.Common;
    static readonly SchemaBlock LinearBlock = ThemeSchema.Gradient.Variant("linear")!;
    static readonly SchemaBlock RadialBlock = ThemeSchema.Gradient.Variant("radial")!;

    /// The gradient's keys in the order the file writes them, so a gradient written back keeps its order.
    IReadOnlyList<string> _order = [];

    GradientModel(GradientKind kind, IReadOnlyList<string> colors)
    {
        Kind = kind;
        Colors = colors;
    }

    public GradientKind Kind { get; private init; }

    public IReadOnlyList<string> Colors { get; private init; }

    /// Where each color sits, 0 to 1, one per color; null spreads them evenly.
    public IReadOnlyList<double>? Stops { get; private init; }

    /// A linear gradient's direction in degrees, clockwise from left to right.
    public double? Angle { get; private init; }

    /// A radial gradient's centre, as [x, y] fractions of the area.
    public (double X, double Y)? Center { get; private init; }

    /// A radial gradient's reach, as a fraction of half the area's longer side: 0 to 3.
    public double? Radius { get; private init; }

    public double EffectiveAngle => Angle ?? Default(LinearBlock, "angle");

    public (double X, double Y) EffectiveCenter => Center ?? (0.5, 0.5);

    public double EffectiveRadius => Radius ?? Default(RadialBlock, "radius");

    /// Where each color draws: Stops, or spread evenly from 0 to 1.
    public IReadOnlyList<double> EffectiveStops => Stops ?? Even(Colors.Count);

    /// The fewest colors a gradient takes, and the radius's range, from the catalog.
    public static int MinColors => LinearBlock.Key("linear")!.MinItems ?? 2;

    public static double MaxRadius => RadialBlock.Key("radius")!.Max ?? double.MaxValue;

    /// The key the colors are under: `linear` or `radial`.
    public string KindKey => Kind == GradientKind.Linear ? "linear" : "radial";

    /// What is wrong with the gradient, in the catalog's terms, or null when it can be written.
    public string? Error => Check();

    /// The gradient a paint holds, or null for a color or anything that isn't a gradient.
    public static GradientModel? Read(YamlValue? paint)
    {
        if (paint is not YamlMap map) return null;
        var (linear, radial) = (map["linear"], map["radial"]);
        var kind = radial is null ? GradientKind.Linear : GradientKind.Radial;
        if ((linear is null) == (radial is null) || (linear ?? radial) is not YamlList colors
            || !colors.Items.All(item => item is YamlText))
            return null;

        var known = kind == GradientKind.Linear ? new[] { "linear", "stops", "angle" } : ["radial", "stops", "center", "radius"];
        if (map.Entries.Any(e => !known.Contains(e.Key))) return null;
        if (!TryNumbers(map["stops"], out var stops) || !TryNumbers(map["center"], out var center)
            || !TryNumber(map["angle"], out var angle) || !TryNumber(map["radius"], out var radius)
            || center is { Count: not 2 })
            return null;

        return new GradientModel(kind, colors.Items.Cast<YamlText>().Select(c => c.Value).ToList())
        {
            Stops = stops,
            Angle = angle,
            Center = center is [var x, var y] ? (x, y) : null,
            Radius = radius,
            _order = map.Entries.Select(e => e.Key).ToList(),
        };
    }

    /// The gradient at `paint` in the file, or null.
    public static GradientModel? Read(LayoutDocument document, YamlPath paint) => Read(document.ValueAt(paint));

    /// The gradient a paint row shows, to start the gradient editor from: the one the widget sets, or, when it sets
    /// none, the one it inherits from its named style. Null for a color, mixed values or no gradient.
    public static GradientModel? Read(PropertyRow row) => row.IsSet ? Read(row.Value) : Read(row.Inherited);

    /// A new gradient from a color, for the paint control's gradient switch: the color at both ends, to recolour.
    public static GradientModel FromColor(string color, GradientKind kind = GradientKind.Linear) => new(kind, [color, color]);

    /// The gradient as a paint mapping, its keys in the order the file had them and new ones after.
    public YamlMap ToYaml()
    {
        var entries = new List<(string Key, YamlValue Value)> { (KindKey, new YamlList(Colors.Select(c => (YamlValue)c).ToList())) };
        if (Stops is { } stops) entries.Add(("stops", Numbers(stops)));
        if (Angle is { } angle) entries.Add(("angle", YamlValue.Number(angle)));
        if (Center is { } center) entries.Add(("center", Numbers([center.X, center.Y])));
        if (Radius is { } radius) entries.Add(("radius", YamlValue.Number(radius)));

        var order = _order.Select((key, i) => (key, i)).ToDictionary(e => e.key == "linear" || e.key == "radial" ? KindKey : e.key, e => e.i);
        return new YamlMap(entries
            .OrderBy(e => order.GetValueOrDefault(e.Key, int.MaxValue))
            .Select(e => new KeyValuePair<string, YamlValue>(e.Key, e.Value)).ToList());
    }

    /// Writes the gradient to the paint at `paint` as one undo step: only the keys that changed, so the rest keep their
    /// text; the whole paint when it was a color or the other kind. A paint in a widget's or item's `style` is written
    /// as the colour rows write one (StyleValues.Set): over a named style (`style: panel`) the style becomes `{ extends:
    /// panel, fill: … }`, so the other widgets using it don't change. Throws PropertyValueException when Error says why
    /// it can't be written. `merge` makes a drag of a stop or the angle one step.
    public EditorCommand Write(YamlPath paint, bool merge = false)
    {
        if (Error is { } error) throw new PropertyValueException(error);
        return new EditDocument("Edit gradient", document => WriteTo(document, paint))
        {
            MergeKey = merge ? ("gradient", paint) : null,
        };
    }

    LayoutDocument WriteTo(LayoutDocument document, YamlPath paint)
    {
        if (Read(document.ValueAt(paint)) is not { } old || old.Kind != Kind)
        {
            return InStyle(paint) is (var style, var key)
                ? StyleValues.Set(document, style, [(key, ToYaml())])
                : EditDocument.Set(document, paint, ToYaml());
        }

        // The paint is a gradient in the file, so any style it is in is a mapping already.
        if (!old.Colors.SequenceEqual(Colors)) document = EditDocument.Set(document, paint.Child(KindKey), ToYaml()[KindKey]!);
        document = Key(document, paint.Child("stops"), Same(old.Stops, Stops), Stops is { } s ? Numbers(s) : null);
        document = Key(document, paint.Child("angle"), old.Angle == Angle, Angle is { } a ? YamlValue.Number(a) : null);
        document = Key(document, paint.Child("center"), old.Center == Center, Center is { } c ? Numbers([c.X, c.Y]) : null);
        return Key(document, paint.Child("radius"), old.Radius == Radius, Radius is { } r ? YamlValue.Number(r) : null);
    }

    /// The `style` a paint is in and the paint's steps inside it (`stroke`, `color` for `widgets[0].style.stroke.color`),
    /// or null for a paint outside a style, such as a fill layer's fill.
    static (YamlPath Style, IReadOnlyList<string> Key)? InStyle(YamlPath paint)
    {
        var key = new List<string>();
        for (var at = paint; at.Last is { IsKey: true } last; at = at.Parent!)
        {
            if (last.Key == "style" && key.Count > 0) return (at, key);
            key.Insert(0, last.Key!);
        }

        return null;
    }

    /// Sets the key, or removes it when it is now left out, unless it is unchanged.
    static LayoutDocument Key(LayoutDocument document, YamlPath path, bool same, YamlValue? value) =>
        same ? document : value is null ? EditDocument.Remove(document, path) : EditDocument.Set(document, path, value);

    public GradientModel WithKind(GradientKind kind) => kind == Kind ? this : this with
    {
        Kind = kind,
        // An angle means nothing to a radial gradient, nor a centre and radius to a linear one.
        Angle = kind == GradientKind.Linear ? Angle : null,
        Center = kind == GradientKind.Radial ? Center : null,
        Radius = kind == GradientKind.Radial ? Radius : null,
    };

    /// Adds `color` before color `index` (at the end when null). With stops, it sits halfway between its neighbours.
    public GradientModel AddColor(string color, int? index = null)
    {
        var at = index ?? Colors.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(at, nameof(index));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(at, Colors.Count, nameof(index));
        var colors = Colors.ToList();
        colors.Insert(at, color);
        if (Stops is not { } stops) return this with { Colors = colors };

        var before = at > 0 ? stops[at - 1] : 0;
        var after = at < stops.Count ? stops[at] : 1;
        var newStops = stops.ToList();
        newStops.Insert(at, (before + after) / 2);
        return this with { Colors = colors, Stops = newStops };
    }

    /// Removes color `index`, and its stop. Throws InvalidOperationException at the fewest colors a gradient takes.
    public GradientModel RemoveColor(int index)
    {
        if (Colors.Count <= MinColors) throw new InvalidOperationException($"A gradient needs at least {MinColors} colors.");
        var colors = Colors.ToList();
        colors.RemoveAt(index);
        var stops = Stops?.ToList();
        stops?.RemoveAt(index);
        return this with { Colors = colors, Stops = stops };
    }

    /// Moves color `from` to `to`. The stops stay where they are, so the colors trade places along the line.
    public GradientModel MoveColor(int from, int to)
    {
        var colors = Colors.ToList();
        var color = colors[from];
        colors.RemoveAt(from);
        colors.Insert(to, color);
        return this with { Colors = colors };
    }

    public GradientModel SetColor(int index, string color)
    {
        var colors = Colors.ToList();
        colors[index] = color;
        return this with { Colors = colors };
    }

    /// Moves stop `index` to `value`, kept between its neighbours and within 0 to 1, as dragging it along the bar does.
    /// Colors spread evenly get stops of their own first.
    public GradientModel SetStop(int index, double value)
    {
        var stops = EffectiveStops.ToList();
        var low = index > 0 ? stops[index - 1] : 0;
        var high = index < stops.Count - 1 ? stops[index + 1] : 1;
        stops[index] = Math.Clamp(value, low, high);
        return this with { Stops = stops };
    }

    /// Removes the stops, so the colors spread evenly.
    public GradientModel SpreadEvenly() => this with { Stops = null };

    public GradientModel WithAngle(double angle) => this with { Angle = angle };

    public GradientModel WithCenter(double x, double y) => this with { Center = (x, y) };

    public GradientModel WithRadius(double radius) => this with { Radius = radius };

    /// Checked against the catalog's gradient keys: the colors, the stops, the centre and the radius.
    string? Check()
    {
        var colorsKey = (Kind == GradientKind.Linear ? LinearBlock : RadialBlock).Key(KindKey)!;
        if (Colors.Count < (colorsKey.MinItems ?? 0)) return $"a gradient needs at least {colorsKey.MinItems} colors";
        if (Colors.FirstOrDefault(c => !ColorPattern().IsMatch(c)) is { } bad)
            return $"'{bad}' is not a color such as \"#FF5A36\" or \"#FF5A3680\"";

        if (Stops is { } stops)
        {
            var key = Common.Key("stops")!;
            if (stops.Count != Colors.Count) return $"stops needs one number per color: {Colors.Count}, not {stops.Count}";
            if (stops.Any(s => s < key.Min || s > key.Max)) return $"each stop must be between {key.Min} and {key.Max}";
            for (var i = 1; i < stops.Count; i++)
                if (stops[i] < stops[i - 1]) return "each stop must be no lower than the one before";
        }

        if (Kind == GradientKind.Linear && Angle is { } angle && !double.IsFinite(angle)) return "angle must be a number";
        if (Center is { } center)
        {
            var key = RadialBlock.Key("center")!;
            if (!(center.X >= key.Min && center.X <= key.Max && center.Y >= key.Min && center.Y <= key.Max))
                return $"center must be two numbers between {key.Min} and {key.Max}";
        }

        if (Radius is { } radius)
        {
            var key = RadialBlock.Key("radius")!;
            if (!(radius >= key.Min && radius <= key.Max)) return $"radius must be between {key.Min} and {key.Max}";
        }

        return null;
    }

    static IReadOnlyList<double> Even(int count) =>
        Enumerable.Range(0, count).Select(i => count == 1 ? 0 : (double)i / (count - 1)).ToList();

    static double Default(SchemaBlock block, string key) =>
        double.Parse(block.Key(key)!.Default!, CultureInfo.InvariantCulture);

    static YamlList Numbers(IEnumerable<double> values) => new(values.Select(v => (YamlValue)YamlValue.Number(v)).ToList());

    static bool Same(IReadOnlyList<double>? a, IReadOnlyList<double>? b) => a is null ? b is null : b is not null && a.SequenceEqual(b);

    static bool TryNumber(YamlValue? value, out double? number)
    {
        number = null;
        if (value is null) return true;
        if (value is not YamlText { Value: var text }
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed))
            return false;
        number = parsed;
        return true;
    }

    static bool TryNumbers(YamlValue? value, out IReadOnlyList<double>? numbers)
    {
        numbers = null;
        if (value is null) return true;
        if (value is not YamlList list) return false;
        var parsed = new List<double>();
        foreach (var item in list.Items)
        {
            if (!TryNumber(item, out var number) || number is null) return false;
            parsed.Add(number.Value);
        }

        numbers = parsed;
        return true;
    }

    [GeneratedRegex(ThemeSchema.ColorPattern)]
    private static partial Regex ColorPattern();
}
