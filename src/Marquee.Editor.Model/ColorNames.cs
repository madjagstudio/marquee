using System.Globalization;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// A color in a word, for the plain-words summaries of `when` rules: "#FF5A36" is red, "#5A6275" grey.
public static class ColorNames
{
    /// The nearest plain name of a color written as layout.yaml writes it ("#RGB", "#RRGGBB", "#RRGGBBAA" or
    /// "transparent"); the text itself when it isn't one.
    public static string Of(string color)
    {
        if (color == "transparent") return "transparent";
        if (!TryParse(color, out var parsed)) return color;
        if (parsed.Alpha == 0) return "transparent";

        parsed.ToHsl(out var hue, out var saturation, out var lightness);
        if (lightness >= 92) return "white";
        if (lightness <= 10) return "black";
        if (saturation <= 18 || (lightness >= 85 && saturation <= 40)) return "grey";
        return hue switch
        {
            < 15 or >= 345 => "red",
            < 45 => "orange",
            < 70 => "yellow",
            < 165 => "green",
            < 200 => "cyan",
            < 255 => "blue",
            < 290 => "purple",
            _ => "pink",
        };
    }

    static bool TryParse(string text, out SKColor color)
    {
        color = default;
        if (text.Length is not (4 or 7 or 9) || text[0] != '#') return false;
        var hex = text.Length == 4 ? string.Concat(text[1..].Select(c => $"{c}{c}")) : text[1..];
        if (!uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return false;
        color = hex.Length == 8
            ? new SKColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : new SKColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}
