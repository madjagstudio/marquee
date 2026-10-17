using System.Globalization;
using SkiaSharp;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Themes.Parsing;

/// Reading helpers over YamlDotNet's node model. Every failure is a ThemeException carrying the line of the node at fault.
/// `what` names the thing being read in messages, e.g. "canvas.fps".
static class Yaml
{
    public static YamlNode? Load(string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            throw new ThemeException($"YAML syntax error: {ex.Message}", (int)ex.Start.Line);
        }
        catch (InvalidOperationException ex)
        {
            // YamlDotNet throws this, without a position, for some malformed flow collections such as an unclosed [.
            throw new ThemeException($"YAML syntax error in layout.yaml: {ex.Message} Check for an unclosed [ or {{.");
        }

        return stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode;
    }

    public static int Line(YamlNode node) => (int)node.Start.Line;

    public static ThemeException Error(YamlNode node, string reason) => new(reason, Line(node));

    public static Fields Map(YamlNode node, string what, params string[] allowed)
    {
        if (node is not YamlMappingNode map)
            throw Error(node, $"{what} must be a mapping of keys to values");

        var values = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var (keyNode, value) in map.Children)
        {
            var key = keyNode is YamlScalarNode { Value: { } k } ? k : throw Error(keyNode, $"Keys in {what} must be plain names");
            if (!allowed.Contains(key))
                throw Error(keyNode, $"Unknown key '{key}' in {what}. Expected one of: {string.Join(", ", allowed)}");
            values[key] = value;
        }

        return new Fields(map, what, values);
    }

    public static IReadOnlyList<YamlNode> List(YamlNode node, string what) =>
        node is YamlSequenceNode sequence ? sequence.Children.ToList() : throw Error(node, $"{what} must be a list");

    public static string Text(YamlNode node, string what)
    {
        if (node is not YamlScalarNode { Value: { } value })
            throw Error(node, $"{what} must be a single value, not a list or mapping");
        if (value.Length == 0)
            throw Error(node, $"{what} is empty");
        return value;
    }

    public static double Number(YamlNode node, string what, double min = double.MinValue, double max = double.MaxValue)
    {
        var text = Text(node, what);
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw Error(node, $"{what} must be a number, not '{text}'");
        if (value < min || value > max)
            throw Error(node, max == double.MaxValue ? $"{what} must be at least {min}" : $"{what} must be between {min} and {max}");
        return value;
    }

    public static float Float(YamlNode node, string what, double min = double.MinValue, double max = double.MaxValue)
    {
        var result = (float)Number(node, what, min, max);
        if (!float.IsFinite(result))
            throw Error(node, $"{what} is too large");
        return result;
    }

    public static int Integer(YamlNode node, string what, int min, int max)
    {
        var value = Number(node, what, min, max);
        if (value != Math.Floor(value))
            throw Error(node, $"{what} must be a whole number");
        return (int)value;
    }

    public static bool Bool(YamlNode node, string what) => Text(node, what) switch
    {
        "true" => true,
        "false" => false,
        var other => throw Error(node, $"{what} must be true or false, not '{other}'"),
    };

    /// One of a fixed set of words, e.g. orientation: portrait | landscape.
    public static T Choice<T>(YamlNode node, string what, params (string Name, T Value)[] options)
    {
        var text = Text(node, what);
        foreach (var (name, value) in options)
            if (name == text) return value;
        throw Error(node, $"{what} must be one of: {string.Join(", ", options.Select(o => o.Name))}; not '{text}'");
    }

    /// A pair of numbers such as `at: [10, 20]`.
    public static SKPoint Pair(YamlNode node, string what, double min = double.MinValue)
    {
        if (node is not YamlSequenceNode { Children.Count: 2 } pair)
            throw Error(node, $"{what} must be a pair of numbers like [10, 20]");
        return new SKPoint(Float(pair.Children[0], what, min), Float(pair.Children[1], what, min));
    }
}

/// The keys of one mapping, already checked against the allowed names.
sealed class Fields(YamlMappingNode node, string what, IReadOnlyDictionary<string, YamlNode> values)
{
    public YamlMappingNode Node { get; } = node;
    public string What { get; } = what;

    public bool Has(string key) => values.ContainsKey(key);

    public YamlNode? Optional(string key) => values.GetValueOrDefault(key);

    public YamlNode Required(string key) =>
        values.TryGetValue(key, out var value) ? value : throw Yaml.Error(Node, $"{What} is missing '{key}'");

    /// Exactly one of the given keys must be present; returns which.
    public string OneOf(params string[] keys)
    {
        var present = keys.Where(Has).ToList();
        return present.Count switch
        {
            1 => present[0],
            0 => throw Yaml.Error(Node, $"{What} needs one of: {string.Join(", ", keys)}"),
            _ => throw Yaml.Error(Node, $"{What} can only have one of: {string.Join(", ", present)}"),
        };
    }
}
