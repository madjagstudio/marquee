using System.Globalization;
using System.Text.RegularExpressions;
using Marquee.Core.Expressions;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Schema;

namespace Marquee.Editor.Model;

/// Checks a value against its catalog key before the properties panel writes it, so the panel can say what is wrong
/// as it is typed: the kind, the range (with a MinFor's lower bound), the choices, MaxLength, a list's length and its
/// items, a block's keys. Loading the new text checks the rest (EditorSession.Apply refuses text that doesn't load),
/// such as a font that isn't installed or a max below its min.
static partial class PropertyValues
{
    /// Why `value` can't be `key`'s value, or null when it can. A key that TakesFalse also takes false.
    public static string? Check(SchemaKey key, YamlValue value, CheckContext context) =>
        key.TakesFalse && value is YamlText { Value: "false" } ? null : Kind(key, key.Name, value, context);

    static string? Kind(SchemaKey key, string what, YamlValue value, CheckContext context) => key.Kind switch
    {
        SchemaKind.Number => Text(what, value, text => Number(what, text, context.Min ?? key.Min, key.Max, whole: false)
            ?? (key.NotZero && double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) == 0 ? $"{what} can't be 0" : null)),
        SchemaKind.Integer => Text(what, value, text => Number(what, text, context.Min ?? key.Min, key.Max, whole: true)),
        SchemaKind.Boolean => Text(what, value, text => text is "true" or "false" ? null : $"{what} must be true or false, not '{text}'"),
        SchemaKind.String => Text(what, value, text => Words(key, what, text)),
        SchemaKind.Enum => Text(what, value, text => key.Values.Contains(text) ? null : $"{what} must be one of: {string.Join(", ", key.Values)}; not '{text}'"),
        SchemaKind.Color => Text(what, value, text => Color(what, text)),
        SchemaKind.Paint => value is YamlMap ? Gradient(what, value) : Text(what, value, text => Color(what, text)),
        SchemaKind.FontFamily => Text(what, value, _ => null),
        SchemaKind.FontWeight => Text(what, value, text => key.Values.Contains(text) ? null
            : Number(what, text, key.Min, key.Max, whole: true) is { } error ? $"{error}, or one of: {string.Join(", ", key.Values)}" : null),
        SchemaKind.Expression => Text(what, value, text => Expression(what, text)),
        SchemaKind.SensorKey => Text(what, value, text => SensorKey(what, text)),
        SchemaKind.Template => Text(what, value, text => Template(what, text, context.WithValue)),
        SchemaKind.Format => Text(what, value, text => Formatting.FormatError(text) is { } error ? $"{what}: {error}" : null),
        SchemaKind.ThemeFile => Text(what, value, text => File(key, what, text)),
        SchemaKind.Point => Pair(what, value, min: null, "[x, y]"),
        SchemaKind.Size => Pair(what, value, context.Min ?? key.Min ?? 1, "[width, height]"),
        SchemaKind.StyleName => Text(what, value, text => StyleName(what, text, context.StyleNames)),
        SchemaKind.Style => value is YamlMap style ? Block(ThemeSchema.Style, what, style, context with { Min = null })
            : Text(what, value, text => StyleName(what, text, context.StyleNames)),
        SchemaKind.Block => value is YamlMap map && ThemeSchema.Block(key.Of!) is { } block
            ? Block(block, what, map, context with { Min = null })
            : $"{what} must be a mapping, such as {{ width: 2 }}",
        SchemaKind.List => List(key, what, value, context),
        SchemaKind.Map or SchemaKind.Uniforms => value is YamlMap ? null : $"{what} must be a mapping of names to values",
        _ => null,
    };

    /// A single value's check: a list or mapping isn't one, and neither is an empty value.
    static string? Text(string what, YamlValue value, Func<string, string?> check) => value switch
    {
        YamlText { Value.Length: 0 } => $"{what} is empty",
        YamlText text => check(text.Value),
        _ => $"{what} must be a single value, not a list or mapping",
    };

    static string? Number(string what, string text, double? min, double? max, bool whole)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            return $"{what} must be a number, not '{text}'";
        if (whole && value != Math.Floor(value)) return $"{what} must be a whole number";
        return (min, max) switch
        {
            ({ } low, { } high) when value < low || value > high => $"{what} must be between {Show(low)} and {Show(high)}",
            ({ } low, null) when value < low => $"{what} must be at least {Show(low)}",
            (null, { } high) when value > high => $"{what} must be at most {Show(high)}",
            _ => null,
        };
    }

    static string? Words(SchemaKey key, string what, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return $"{what} is only spaces";
        // Counted as people see characters, as NameParser counts them.
        return key.MaxLength is { } max && new StringInfo(text).LengthInTextElements > max
            ? $"{what} is too long (the limit is {max} characters)"
            : null;
    }

    static string? Color(string what, string text) =>
        ColorPattern().IsMatch(text) ? null : $"{what} must be a color such as \"#FF5A36\" or \"#FF5A3680\", not '{text}'";

    static string? Gradient(string what, YamlValue value) =>
        GradientModel.Read(value) is { } gradient
            ? gradient.Error is { } error ? $"{what}: {error}" : null
            : $"{what} must be a color, or a gradient with linear: or radial: and its colors";

    static string? Expression(string what, string text)
    {
        try
        {
            Core.Expressions.Expression.Parse(text);
            return null;
        }
        catch (ExpressionException ex)
        {
            return $"{what}: {ex.Message}";
        }
    }

    /// One key, not an expression: what parses with the key alone as what it reads.
    static string? SensorKey(string what, string text) =>
        Expression(what, text) ?? (Core.Expressions.Expression.Parse(text).Keys is [var only] && only == text.Trim()
            ? null
            : $"{what} must be a single sensor key, such as gpu.load");

    static string? Template(string what, string text, bool withValue)
    {
        try
        {
            Core.Expressions.Template.Parse(text, withValue);
            return null;
        }
        catch (ExpressionException ex)
        {
            return $"{what}: {ex.Message}";
        }
    }

    /// A path inside the theme folder, with one of the key's extensions when it lists any.
    static string? File(SchemaKey key, string what, string text)
    {
        var steps = text.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(text) || text.Contains(':') || steps.Contains(".."))
            return $"{what} must be a path inside the theme folder, such as images/logo.png";
        var extension = Path.GetExtension(text).ToLowerInvariant();
        return key.FileTypes.Count == 0 || key.FileTypes.Contains(extension)
            ? null
            : key.FileTypes.Count == 1 ? $"{what} must be a {key.FileTypes[0]} file"
            : $"{what} must be a {string.Join(", ", key.FileTypes.SkipLast(1))} or {key.FileTypes[^1]} file";
    }

    static string? Pair(string what, YamlValue value, double? min, string form)
    {
        if (value is not YamlList { Items: [YamlText first, YamlText second] })
            return $"{what} must be two numbers, {form}";
        return Number(what, first.Value, min, null, whole: false) ?? Number(what, second.Value, min, null, whole: false);
    }

    static string? StyleName(string what, string text, IReadOnlyList<string> styles) =>
        styles.Contains(text) ? null : $"{what}: there is no style named '{text}' in styles:";

    /// A mapping of the block's keys, each checked; a key the block hasn't is refused, as the parser refuses it.
    static string? Block(SchemaBlock block, string what, YamlMap map, CheckContext context)
    {
        foreach (var (name, value) in map.Entries)
        {
            if (block.Key(name) is not { } key) return $"{what} has no key '{name}'";
            if (Check(key, value, context) is { } error) return $"{what}.{error}";
        }

        return null;
    }

    static string? List(SchemaKey key, string what, YamlValue value, CheckContext context)
    {
        if (value is not YamlList list) return $"{what} must be a list";
        if (key.MinItems is { } min && list.Items.Count < min) return $"{what} needs at least {min} {(min == 1 ? "item" : "items")}";
        if (key.MaxItems is { } max && list.Items.Count > max) return $"{what} can have at most {max} items";

        // A list of numbers takes the key's range for each, as a gradient's stops do.
        var item = key.ItemKind is { } kind
            ? new SchemaKey(what, kind, key.Help) { Min = key.Min, Max = key.Max }
            : key.Of is { } of && ThemeSchema.Block(of) is not null ? new SchemaKey(what, SchemaKind.Block, key.Help) { Of = of } : null;
        if (item is null) return null;
        foreach (var entry in list.Items)
            if (Kind(item, what, entry, context with { Min = null }) is { } error) return error;
        return null;
    }

    static string Show(double value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(ThemeSchema.ColorPattern)]
    private static partial Regex ColorPattern();
}

/// What a check needs beyond the key and value: the theme's style names, a MinFor's lower bound when it holds, and
/// whether `{0}` is a value widget's value.
sealed record CheckContext(IReadOnlyList<string> StyleNames)
{
    public double? Min { get; init; }

    public bool WithValue { get; init; }
}
