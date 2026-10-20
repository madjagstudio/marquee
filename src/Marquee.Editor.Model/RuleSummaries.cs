using System.Globalization;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// One of a widget's `when` rules in plain words, for the properties panel's read-only list: "turns red above 85",
/// "hides while stale". StyleName is the named style it layers on, when it names one. E1 never changes rules; E2 edits
/// them.
public sealed record RuleSummary(int Index, YamlPath Path, string Text, string? StyleName);

public static class RuleSummaries
{
    /// The rules of the widget at `widget`, in order; empty when it has none.
    public static IReadOnlyList<RuleSummary> Of(LayoutDocument document, YamlPath widget)
    {
        var rules = widget.Child("when");
        if (Read(document, rules) is not YamlList list) return [];
        return list.Items.Select((rule, i) =>
        {
            var path = rules.Item(i);
            if (rule is not YamlMap map) return new RuleSummary(i, path, "a rule the editor can't read", null);
            var name = (map["style"] as YamlText)?.Value;
            var style = name is not null ? StyleValues.Named(document, name) : map["style"] as YamlMap ?? YamlMap.Empty;
            if (style["extends"] is YamlText { Value: var extends })
                style = StyleValues.Merge(StyleValues.Named(document, extends), style.Without("extends"));
            return new RuleSummary(i, path, $"{Effects(style)} {Condition(map)}", name);
        }).ToList();
    }

    /// When the rule holds, in words that follow what it does.
    static string Condition(YamlMap rule) => rule.Entries.FirstOrDefault(e => e.Key != "style") switch
    {
        { Key: "above", Value: YamlText above } => $"above {above.Value}",
        { Key: "below", Value: YamlText below } => $"below {below.Value}",
        { Key: "equals", Value: YamlText equals } => double.TryParse(equals.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? $"at {equals.Value}"
            : $"when it is {equals.Value}",
        { Key: "stale" } => "while stale",
        _ => "always",
    };

    /// What the style does, in the catalog's order of style keys: "turns red, glows red and pulses".
    static string Effects(YamlMap style)
    {
        var effects = ThemeSchema.Style.Keys
            .Where(key => style[key.Name] is not null)
            .Select(key => Effect(key, style[key.Name]!))
            .Where(effect => effect.Length > 0)
            .Distinct()
            .ToList();
        return effects.Count switch
        {
            0 => "changes nothing",
            1 => effects[0],
            _ => $"{string.Join(", ", effects.Take(effects.Count - 1))} and {effects[^1]}",
        };
    }

    static string Effect(SchemaKey key, YamlValue value) => (key.Name, value) switch
    {
        ("extends", _) => "",
        ("color", YamlText color) => $"turns {ColorNames.Of(color.Value)}",
        ("fill", _) => $"fills {Paint(value)}",
        ("track", _) => $"makes its track {Paint(value)}",
        ("text_fill", _) => $"paints its letters {Paint(value)}",
        ("stroke", YamlMap stroke) when stroke["color"] is { } color => $"outlines in {Paint(color)}",
        ("text_stroke", YamlMap stroke) when stroke["color"] is { } color => $"outlines its letters in {Paint(color)}",
        ("glow", YamlMap glow) when glow["color"] is YamlText color => $"glows {ColorNames.Of(color.Value)}",
        ("shadow", YamlMap) => "casts a shadow",
        ("pulse", _) => "pulses",
        ("opacity", YamlText { Value: "0" }) => "hides",
        ("opacity", YamlText { Value: "1" }) => "shows",
        ("opacity", YamlText opacity) when double.TryParse(opacity.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) =>
            $"fades to {(o * 100).ToString("0", CultureInfo.InvariantCulture)}%",
        ("weight", YamlText weight) => $"turns {weight.Value}",
        ("size", YamlText size) => $"sizes its text to {size.Value} px",
        _ => $"changes its {key.Name.Replace('_', ' ')}",
    };

    /// A paint in words: a color's name, or "with a gradient".
    static string Paint(YamlValue paint) => paint is YamlText color ? ColorNames.Of(color.Value) : "with a gradient";

    static YamlValue? Read(LayoutDocument document, YamlPath path)
    {
        try
        {
            return document.ValueAt(path);
        }
        catch (LayoutEditException)
        {
            return null;
        }
    }
}
