using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// The `ring:` section. A field it leaves out takes BlockDefaults' value, so `ring: { effect: spin }` spins white at
/// full brightness.
static class RingParser
{
    static readonly (string Name, RingEffect Value)[] Effects =
        [("static", RingEffect.Static), ("breathe", RingEffect.Breathe), ("spin", RingEffect.Spin), ("rainbow", RingEffect.Rainbow)];

    // The keys the ring and its rules accept: a rule takes one of the widgets' conditions and changes any of Overrides.
    // ThemeSchema describes each, and its drift test reads these lists.
    internal static readonly string[] Overrides = ["color", "effect", "speed", "brightness"];
    internal static readonly string[] Keys = ["color", "effect", "speed", "brightness", "source", "when"];
    internal static readonly string[] RuleKeys = [.. WidgetParser.Conditions, .. Overrides];

    /// Not RingSpec.Default: that dim warm glow suits a theme that says nothing about the ring, but a theme author who
    /// writes `ring: { color: "#3A7BFF" }` expects to see that color, so a block starts from plain white at full
    /// brightness.
    static readonly RingSpec BlockDefaults = new(SKColors.White, RingEffect.Static, 1, 1, null, []);

    /// Each field and each rule is checked on its own when errors are collected, so one mistake doesn't hide the others.
    public static RingSpec Parse(YamlNode node, ParseErrors errors)
    {
        var f = Yaml.Map(node, "ring", errors, Keys);
        var hasSource = f.Has("source");

        return new RingSpec(
            Field(f, "color", BlockDefaults.Color, n => PaintParser.Color(n, "ring.color"), errors),
            Field(f, "effect", BlockDefaults.Effect, n => Effect(n, "ring.effect"), errors),
            Field(f, "speed", BlockDefaults.Speed, n => Speed(n, "ring.speed"), errors),
            Field(f, "brightness", BlockDefaults.Brightness, n => Brightness(n, "ring.brightness"), errors),
            Field(f, "source", BlockDefaults.Source, WidgetParser.Source, errors),
            f.Optional("when") is { } when ? Rules(when, hasSource, errors) : []);
    }

    /// `fallback` when the field is missing, or bad and the error was collected.
    static T Field<T>(Fields f, string key, T fallback, Func<YamlNode, T> parse, ParseErrors errors)
    {
        if (f.Optional(key) is not { } node) return fallback;
        try
        {
            return parse(node);
        }
        catch (ThemeException ex) when (errors.Absorb(ex))
        {
            return fallback;
        }
    }

    static List<RingRule> Rules(YamlNode node, bool hasSource, ParseErrors errors)
    {
        var rules = new List<RingRule>();
        foreach (var ruleNode in Yaml.List(node, "ring.when"))
            if (errors.Recover(() => Rule(ruleNode, hasSource)) is { } rule)
                rules.Add(rule);
        return rules;
    }

    static RingRule Rule(YamlNode node, bool hasSource)
    {
        var f = Yaml.Map(node, "ring when rule", RuleKeys);
        var kind = f.OneOf(WidgetParser.Conditions);
        var value = f.Required(kind);

        // A widget's stale rule can watch its text's keys, but the ring has only its source to go stale.
        if (kind == "stale" && !hasSource)
            throw Yaml.Error(value, "when 'stale' watches the ring's source, but this ring has no source");
        var (condition, number, text) = WidgetParser.Condition(kind, value, hasSource, "ring");

        if (!Overrides.Any(f.Has))
            throw Yaml.Error(node, $"ring when rule needs at least one of: {string.Join(", ", Overrides)}");

        return new RingRule(condition, number, text,
            f.Optional("color") is { } color ? PaintParser.Color(color, "ring.when.color") : null,
            f.Optional("effect") is { } effect ? Effect(effect, "ring.when.effect") : null,
            f.Optional("speed") is { } speed ? Speed(speed, "ring.when.speed") : null,
            f.Optional("brightness") is { } brightness ? Brightness(brightness, "ring.when.brightness") : null);
    }

    static RingEffect Effect(YamlNode node, string what) => Yaml.Choice(node, what, Effects);

    static float Speed(YamlNode node, string what) => Yaml.Float(node, what, 0.1, 10);

    /// Written as a percentage, kept as a fraction.
    static float Brightness(YamlNode node, string what) => Yaml.Float(node, what, 0, 100) / 100;
}
