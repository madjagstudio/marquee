using System.Globalization;
using Marquee.Core.Sensors;

namespace Marquee.Core.Expressions;

/// Turns values into display text. Numbers use the invariant culture, so a theme looks the same on every PC; only the
/// `time` helper (Formatting.Time.cs) follows the PC's zone and culture.
public static partial class Formatting
{
    /// Shown in place of a value whose source is missing or stale.
    public const string Missing = "—";

    public const string DefaultNumberFormat = "0.##";

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// Numbers with a .NET numeric format such as "0.0" (default "0.##"); strings as they are.
    public static string Format(SensorValue value, string? numberFormat = null) =>
        value.Number is { } n ? n.ToString(numberFormat ?? DefaultNumberFormat, Invariant) : value.Text ?? "";

    /// A value widget's `format`: a .NET composite format such as "{0:0.0}°C", a helper name ("duration", "bytes"), or
    /// "time" with an optional pattern ("time:HH:mm"), which writes in the locale's zone and culture (the PC's if null).
    public static string Apply(string? format, SensorValue value, TimeLocale? locale = null)
    {
        if (format is null) return Format(value);
        if (TimeFormatPattern(format) is { } pattern)
            return value.Number is { } seconds ? Time(seconds, pattern, locale ?? TimeLocale.Pc) : value.Text ?? "";
        if (Helper(format) is { } helper) return value.Number is { } n ? helper(n) : value.Text ?? "";
        return string.Format(Invariant, format, value.Number is { } number ? number : value.Text);
    }

    /// Most digits a standard number format may ask for, as in "F15". Themes are shared, and "F999999999" would build
    /// a gigabyte of zeros every frame.
    public const int MaxFormatPrecision = 15;

    /// Longest custom number format, such as "0.0#".
    public const int MaxCustomFormatLength = 32;

    /// Widest padding a composite format item may ask for, as in "{0,8}".
    public const int MaxAlignment = 100;

    /// Why a .NET number format such as "0.0" or "F2" can't be used, or null if it's fine.
    public static string? NumberFormatError(string format)
    {
        if (NumberFormatLimitError(format) is { } limit) return limit;
        try
        {
            _ = 0.0.ToString(format, Invariant);
            return null;
        }
        catch (FormatException)
        {
            return $"Invalid number format '{format}'";
        }
    }

    /// Why a value widget's `format` can't be used, or null if it's fine.
    public static string? FormatError(string format)
    {
        if (Helper(format) is not null) return null;
        if (TimeFormatPattern(format) is { } pattern)
            return IsTimePattern(pattern) ? null : $"format \"{format}\" is not a valid time pattern such as \"time:HH:mm\"";
        if (!format.Contains("{0")) return $"format \"{format}\" never shows the value; include {{0}}, e.g. \"{{0:0.0}}°C\"";

        // Check what each item asks for before formatting anything with it. An item whose number format is merely
        // invalid is left to string.Format below, so it keeps the message for an invalid format string.
        foreach (var (width, numberFormat) in CompositeItems(format))
        {
            if (width > MaxAlignment)
                return $"format \"{format}\" pads the value to {width} characters (the limit is {MaxAlignment})";
            if (numberFormat is not null && NumberFormatLimitError(numberFormat) is { } limit)
                return limit;
        }

        try
        {
            _ = string.Format(Invariant, format, 0.0);
            return null;
        }
        catch (FormatException)
        {
            return $"format \"{format}\" is not a valid .NET format string such as \"{{0:0.0}}°C\"";
        }
    }

    public static Func<double, string>? Helper(string name) => name switch
    {
        "duration" => Duration,
        "bytes" => Bytes,
        _ => null,
    };

    /// Seconds as "2h 14m". Days show hours, hours show minutes, under a minute shows seconds. Negative is "0s";
    /// NaN or infinite is Missing.
    public static string Duration(double seconds)
    {
        if (!double.IsFinite(seconds)) return Missing;
        var total = (long)Math.Max(0, Math.Floor(seconds));
        var (d, h, m, s) = (total / 86400, total % 86400 / 3600, total % 3600 / 60, total % 60);
        return d > 0 ? string.Create(Invariant, $"{d}d {h}h")
            : h > 0 ? string.Create(Invariant, $"{h}h {m}m")
            : m > 0 ? string.Create(Invariant, $"{m}m")
            : string.Create(Invariant, $"{s}s");
    }

    /// Bytes in binary units: "512 B", "1.5 KB", "8.5 GB", "120 GB". NaN or infinite is Missing.
    public static string Bytes(double bytes)
    {
        if (!double.IsFinite(bytes)) return Missing;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var number = unit == 0 || value >= 100 ? value.ToString("0", Invariant) : value.ToString("0.0", Invariant);
        return $"{number} {units[unit]}";
    }

    /// The cost limits of NumberFormatError: a standard format ("F2": one letter, then a precision) may ask for at
    /// most MaxFormatPrecision digits, and a custom format ("0.0#") may be at most MaxCustomFormatLength long.
    static string? NumberFormatLimitError(string format)
    {
        if (format.Length > 0 && char.IsAsciiLetter(format[0]) && format.Skip(1).All(char.IsAsciiDigit))
        {
            var digits = format[1..].TrimStart('0');
            return digits.Length > 2 || (digits.Length > 0 && int.Parse(digits, Invariant) > MaxFormatPrecision)
                ? $"number format '{format}' asks for too many digits (the limit is {MaxFormatPrecision})"
                : null;
        }

        return format.Length > MaxCustomFormatLength
            ? $"number format '{format}' is too long (the limit is {MaxCustomFormatLength} characters)"
            : null;
    }

    /// The padding width (the alignment without its sign; 0 if none) and number format (null if none) of each item in
    /// a composite format such as "{0,-8:0.0}°C". Only reads as far as the format is well formed; string.Format
    /// reports anything malformed.
    static IEnumerable<(long Width, string? NumberFormat)> CompositeItems(string format)
    {
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                i++;
                continue;
            }

            if (format[i] != '{') continue;
            if (i + 1 < format.Length && format[i + 1] == '{')
            {
                i++;
                continue;
            }

            var close = format.IndexOf('}', i + 1);
            if (close < 0) yield break;
            var item = format[(i + 1)..close];
            var colon = item.IndexOf(':');
            var head = colon >= 0 ? item[..colon] : item;
            var comma = head.IndexOf(',');
            var width = comma >= 0 && long.TryParse(head[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, Invariant, out var a)
                ? (a == long.MinValue ? long.MaxValue : Math.Abs(a))
                : 0;
            yield return (width, colon >= 0 ? item[(colon + 1)..] : null);
            i = close;
        }
    }
}
