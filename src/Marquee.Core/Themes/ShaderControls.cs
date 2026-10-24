using System.Globalization;
using System.Text.RegularExpressions;

namespace Marquee.Core.Themes;

public enum ControlKind { Colour, Number }
public sealed record ShaderControl(string Uniform, ControlKind Kind, string Label, double Min, double Max, string Default);
public sealed record ShaderReacts(string Uniform, string Sentence);
public sealed record ShaderInfo(string? Name, IReadOnlyList<ShaderControl> Controls, ShaderReacts? Reacts)
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class ShaderControls
{
    public static ShaderInfo Read(string source)
    {
        string? name = null;
        ShaderReacts? reacts = null;
        var controls = new List<ShaderControl>();
        var warnings = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in source.Split('\n'))
        {
            var text = line.Trim();
            if (text.Length == 0) continue;
            if (!text.StartsWith("//", StringComparison.Ordinal)) break;
            text = text[2..].Trim();
            if (!text.StartsWith('@')) continue;
            var title = Regex.Match(text, @"^@name\s+(.+)$");
            var colour = Regex.Match(text, """^@control\s+(\w+)\s+colour\s+"([^"]+)"\s+default\s+(#[0-9A-Fa-f]{6})$""");
            var number = Regex.Match(text, """^@control\s+(\w+)\s+number\s+([+-]?[\d.]+)\.\.([+-]?[\d.]+)\s+default\s+([+-]?[\d.]+)\s+"([^"]+)"$""");
            var reaction = Regex.Match(text, """^@reacts\s+(\w+)\s+"([^"]+)"$""");
            if (title.Success && name is null) name = title.Groups[1].Value.Trim();
            else if (colour.Success && used.Add(colour.Groups[1].Value))
                controls.Add(new(colour.Groups[1].Value, ControlKind.Colour, colour.Groups[2].Value, 0, 1, colour.Groups[3].Value));
            else if (number.Success && Parse(number.Groups[2].Value, out var min) && Parse(number.Groups[3].Value, out var max)
                && Parse(number.Groups[4].Value, out var value) && min < max && value >= min && value <= max
                && used.Add(number.Groups[1].Value))
                controls.Add(new(number.Groups[1].Value, ControlKind.Number, number.Groups[5].Value, min, max, number.Groups[4].Value));
            else if (reaction.Success && reacts is null && used.Add(reaction.Groups[1].Value))
                reacts = new(reaction.Groups[1].Value, reaction.Groups[2].Value);
            else warnings.Add(text);
        }
        return new(name, controls, reacts) { Warnings = warnings };
    }

    static bool Parse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
