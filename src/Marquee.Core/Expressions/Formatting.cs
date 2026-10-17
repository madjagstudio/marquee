using System.Globalization;
using Marquee.Core.Sensors;

namespace Marquee.Core.Expressions;

/// Turns values into display text. Everything uses the invariant culture, so a theme looks the same on every PC.
public static class Formatting
{
    /// Shown in place of a value whose source is missing or stale.
    public const string Missing = "—";

    public const string DefaultNumberFormat = "0.##";

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// Numbers with a .NET numeric format such as "0.0" (default "0.##"); strings as they are.
    public static string Format(SensorValue value, string? numberFormat = null) =>
        value.Number is { } n ? n.ToString(numberFormat ?? DefaultNumberFormat, Invariant) : value.Text ?? "";

    /// A value widget's `format`: a .NET composite format such as "{0:0.0}°C", or a helper name ("duration", "bytes").
    public static string Apply(string? format, SensorValue value)
    {
        if (format is null) return Format(value);
        if (Helper(format) is { } helper) return value.Number is { } n ? helper(n) : value.Text ?? "";
        return string.Format(Invariant, format, value.Number is { } number ? number : value.Text);
    }

    /// Why a value widget's `format` can't be used, or null if it's fine.
    public static string? FormatError(string format)
    {
        if (Helper(format) is not null) return null;
        if (!format.Contains("{0")) return $"format \"{format}\" never shows the value; include {{0}}, e.g. \"{{0:0.0}}°C\"";
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

    /// Seconds as "2h 14m". Days show hours, hours show minutes, under a minute shows seconds. Negative is "0s".
    public static string Duration(double seconds)
    {
        var total = (long)Math.Max(0, Math.Floor(seconds));
        var (d, h, m, s) = (total / 86400, total % 86400 / 3600, total % 3600 / 60, total % 60);
        return d > 0 ? string.Create(Invariant, $"{d}d {h}h")
            : h > 0 ? string.Create(Invariant, $"{h}h {m}m")
            : m > 0 ? string.Create(Invariant, $"{m}m")
            : string.Create(Invariant, $"{s}s");
    }

    /// Bytes in binary units: "512 B", "1.5 KB", "8.5 GB", "120 GB".
    public static string Bytes(double bytes)
    {
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
}
