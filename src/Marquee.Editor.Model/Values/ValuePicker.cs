using System.Globalization;
using System.Text.RegularExpressions;

namespace Marquee.Editor.Model.Values;

public abstract record PickedValue
{
    public sealed record Sensor(string Key) : PickedValue;
    public sealed record Ranged(string Key, double FromLow, double FromHigh, double ToLow, double ToHigh) : PickedValue;
    public sealed record Number(double Value) : PickedValue;
    public sealed record Advanced(string Text) : PickedValue;
}

/// The simple expressions the value picker owns. Other expressions retain their original text in Advanced.
public static class ValuePicker
{
    const string Key = @"(?:[a-z][a-z0-9_]*(?:\.[a-z0-9_*]+)+|fps|frametime|now)";
    const string Number = @"-?\d+(?:\.\d+)?";
    static readonly Regex Sensor = new($@"\A{Key}\z", RegexOptions.CultureInvariant);
    static readonly Regex Numeric = new($@"\A{Number}\z", RegexOptions.CultureInvariant);
    static readonly Regex ShortRange = new($@"\Aclamp\((?<key>{Key}) / (?<width>{Number}), 0, 1\)\z");
    static readonly Regex UnitRange = new($@"\Aclamp\(\((?<key>{Key}) - (?<low>{Number})\) / (?<width>{Number}), 0, 1\)\z");
    static readonly Regex FullRange = new($@"\Aclamp\(\((?<key>{Key}) - (?<low>{Number})\) / (?<width>{Number}) \* (?<scale>{Number}) \+ (?<offset>{Number}), (?<a>{Number}), (?<b>{Number})\)\z");

    public static PickedValue Read(string text)
    {
        var normalized = Regex.Replace(text.Trim(), @"\s+", " ");
        if (Sensor.IsMatch(normalized)) return new PickedValue.Sensor(normalized);
        if (Numeric.IsMatch(normalized) && TryNumber(normalized, out var number)) return new PickedValue.Number(number);

        foreach (var form in new[] { ShortRange, UnitRange, FullRange })
        {
            var match = form.Match(normalized);
            if (!match.Success) continue;
            var values = new Dictionary<string, double>();
            foreach (var name in new[] { "low", "width", "scale", "offset", "a", "b" })
            {
                if (!match.Groups[name].Success) continue;
                if (!TryNumber(match.Groups[name].Value, out var value)) return new PickedValue.Advanced(text);
                values[name] = value;
            }
            var low = values.GetValueOrDefault("low");
            var high = low + values["width"];
            var a = values.GetValueOrDefault("a");
            var b = values.GetValueOrDefault("b", 1);
            // Similar-looking hand-written arithmetic must not silently become a different mapping.
            if (!double.IsFinite(high) || (form == FullRange && (values["offset"] != a || values["scale"] != b - a)))
                return new PickedValue.Advanced(text);
            return new PickedValue.Ranged(match.Groups["key"].Value, low, high, a, b);
        }
        return new PickedValue.Advanced(text);
    }

    public static string Write(PickedValue value) => value switch
    {
        PickedValue.Sensor sensor => sensor.Key,
        PickedValue.Number number => Format(number.Value),
        PickedValue.Advanced advanced => advanced.Text,
        PickedValue.Ranged r when r.ToLow == 0 && r.ToHigh == 1 && r.FromLow == 0 =>
            $"clamp({r.Key} / {Format(r.FromHigh)}, 0, 1)",
        PickedValue.Ranged r when r.ToLow == 0 && r.ToHigh == 1 =>
            $"clamp(({r.Key} - {Format(r.FromLow)}) / {Format(r.FromHigh - r.FromLow)}, 0, 1)",
        PickedValue.Ranged r =>
            $"clamp(({r.Key} - {Format(r.FromLow)}) / {Format(r.FromHigh - r.FromLow)} * {Format(r.ToHigh - r.ToLow)} + {Format(r.ToLow)}, {Format(r.ToLow)}, {Format(r.ToHigh)})",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static (double Low, double High) DefaultRange(string key) => key switch
    {
        _ when key.EndsWith(".temp", StringComparison.Ordinal) => (30, 90),
        _ when key.EndsWith(".load", StringComparison.Ordinal) || key.EndsWith("pct", StringComparison.Ordinal) => (0, 100),
        _ when key.StartsWith("fps", StringComparison.Ordinal) => (0, 144),
        "frametime" => (0, 50),
        _ => (0, 100),
    };

    public static IReadOnlyList<string> Keys(ValueSources sources, IEnumerable<string> themeKeys) =>
        themeKeys.Concat(sources.KnownKeys()).Concat(Samples.Keys)
            .Where(key => !key.Contains('*')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    // Round-trip precision, expanded to decimal notation so even very small numbers fit the expression grammar.
    static string Format(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentAt = text.IndexOf('E');
        if (exponentAt < 0) return text;
        var sign = text.StartsWith('-') ? "-" : "";
        var mantissa = text[sign.Length..exponentAt];
        var point = mantissa.IndexOf('.');
        var digits = mantissa.Replace(".", "");
        var position = (point < 0 ? digits.Length : point) + int.Parse(text[(exponentAt + 1)..], CultureInfo.InvariantCulture);
        return sign + (position <= 0 ? "0." + new string('0', -position) + digits :
            position >= digits.Length ? digits + new string('0', position - digits.Length) : digits.Insert(position, "."));
    }
}
