using System.Globalization;
using System.Windows.Media;

namespace Marquee.Editor.Controls;

/// A colour as the picker holds it: hue in degrees (0 to 360), and saturation, value and alpha from 0 to 1. The picker
/// keeps one of these rather than the colour, so dragging to black or grey doesn't lose the hue.
public readonly record struct Hsva(double H, double S, double V, double A)
{
    public static Hsva FromColor(Color color)
    {
        var (r, g, b) = (color.R / 255.0, color.G / 255.0, color.B / 255.0);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var chroma = max - min;
        double hue = 0;
        if (chroma > 0)
        {
            hue = max == r ? (g - b) / chroma
                : max == g ? 2 + (b - r) / chroma
                : 4 + (r - g) / chroma;
            hue = (hue * 60 + 360) % 360;
        }

        return new Hsva(hue, max == 0 ? 0 : chroma / max, max, color.A / 255.0);
    }

    /// The colour, each channel rounded to the nearest of its 256 steps. The hue wraps, and the rest are clamped to 0
    /// to 1.
    public Color ToColor()
    {
        var hue = (H % 360 + 360) % 360 / 60;
        var (s, v) = (Math.Clamp(S, 0, 1), Math.Clamp(V, 0, 1));
        var sector = (int)Math.Floor(hue) % 6;
        var f = hue - Math.Floor(hue);
        var p = v * (1 - s);
        var q = v * (1 - s * f);
        var t = v * (1 - s * (1 - f));
        var (r, g, b) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return Color.FromArgb(Byte(A), Byte(r), Byte(g), Byte(b));
    }

    static byte Byte(double fraction) => (byte)Math.Round(Math.Clamp(fraction, 0, 1) * 255, MidpointRounding.AwayFromZero);
}

/// Colours written as layout.yaml writes them: "#RGB", "#RRGGBB", "#RRGGBBAA" with the alpha last, or "transparent".
public static class ColorText
{
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        var trimmed = text?.Trim() ?? "";
        if (trimmed == "transparent")
        {
            color = Color.FromArgb(0, 0, 0, 0);
            return true;
        }

        if (trimmed.Length is not (4 or 7 or 9) || trimmed[0] != '#') return false;
        var hex = trimmed.Length == 4 ? string.Concat(trimmed[1..].Select(c => $"{c}{c}")) : trimmed[1..];
        if (!hex.All(Uri.IsHexDigit)
            || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            return false;

        color = hex.Length == 8
            ? Color.FromArgb((byte)value, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8))
            : Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// "#RRGGBB" for an opaque colour, else "#RRGGBBAA", in capitals as the examples write them.
    public static string Format(Color color) =>
        color.A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}");
}

/// The colours chosen lately, newest first, for the picker's row of recent colours. Kept for the editor's run, shared
/// by every picker, and never saved.
public sealed class RecentColors(int capacity = RecentColors.DefaultCapacity)
{
    public const int DefaultCapacity = 10;

    readonly List<string> _colors = [];

    /// The one list the editor's pickers share.
    public static RecentColors Shared { get; } = new();

    public IReadOnlyList<string> Colors => _colors;

    public event EventHandler? Changed;

    /// Puts `color` first, written as ColorText writes it; text that isn't a colour is ignored.
    public void Add(string color)
    {
        if (!ColorText.TryParse(color, out var parsed)) return;
        var text = ColorText.Format(parsed);
        _colors.Remove(text);
        _colors.Insert(0, text);
        if (_colors.Count > capacity) _colors.RemoveRange(capacity, _colors.Count - capacity);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
