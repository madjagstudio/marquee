using System.Text;
using Marquee.Core.Sensors;

namespace Marquee.Core.Expressions;

/// Text with placeholders: `{cpu.temp}`, `{cpu.temp:0.0}` (a .NET numeric format), `{gpu.vram_used|bytes}`,
/// `{claude.five_hour.resets_at - now|duration}`, `{now|time:HH:mm}` (a .NET date and time pattern, colons and all).
/// `{{` and `}}` are literal braces. In a value widget's spans, `{0}` is the widget's own value (`{0:0.0}`, `{0|bytes}`).
public sealed class Template
{
    abstract record Part;
    sealed record Literal(string Text) : Part;
    // TimePattern is set for the time helper, which writes in the renderer's zone and culture, so it is kept as data
    // rather than as a Func like the other helpers.
    // IsValue is set for `{0}` in a template that has a value of its own (Parse's withValue), which Render is given.
    sealed record Placeholder(Expression Expression, string? Format, Func<double, string>? Helper, string? TimePattern, bool IsValue) : Part;

    readonly IReadOnlyList<Part> _parts;

    readonly KeyList _keys = [];

    Template(IReadOnlyList<Part> parts)
    {
        _parts = parts;
        foreach (var placeholder in parts.OfType<Placeholder>()) placeholder.Expression.AddKeys(_keys);
        Keys = _keys.AsReadOnly(); // a view: a caller can't cast it back to the list and change it
    }

    /// The sensor keys its placeholders read, each once, in the order they first appear, without `now`. In a value
    /// widget's spans `{0}` reads no key of its own: it is the widget's value, whose key is the widget's source.
    public IReadOnlyList<string> Keys { get; }

    /// Adds the keys it reads that `keys` doesn't hold yet, in order, and, to a KeyList, whether it reads `now`.
    internal void AddKeys(List<string> keys)
    {
        foreach (var key in _keys)
            if (!keys.Contains(key)) keys.Add(key);
        if (_keys.ReadsClock && keys is KeyList list) list.ReadsClock = true;
    }

    public bool IsStatic => _parts.All(p => p is Literal);

    /// Whether it shows a value of its own: a `{0}` in a template parsed with a value.
    public bool ShowsValue => _parts.Any(p => p is Placeholder { IsValue: true });

    /// `withValue` is for a value widget's spans, where `{0}` is the widget's value, which Render's `value` gives. In any
    /// other template `{0}` is the number 0, as it always was.
    public static Template Parse(string text, bool withValue = false)
    {
        var parts = new List<Part>();
        var literal = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    literal.Append('{');
                    i++;
                    continue;
                }

                var close = text.IndexOf('}', i + 1);
                if (close < 0) throw new ExpressionException("Unclosed '{'", i);
                if (literal.Length > 0)
                {
                    parts.Add(new Literal(literal.ToString()));
                    literal.Clear();
                }

                parts.Add(ParsePlaceholder(text, i + 1, close, withValue));
                i = close;
            }
            else if (c == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}')
                {
                    literal.Append('}');
                    i++;
                    continue;
                }

                throw new ExpressionException("Unmatched '}' (write '}}' for a literal brace)", i);
            }
            else
            {
                literal.Append(c);
            }
        }

        if (literal.Length > 0) parts.Add(new Literal(literal.ToString()));
        return new Template(parts);
    }

    /// The rendered text. Stale is true when any placeholder's value was missing; each such placeholder shows "—".
    /// `time` writes in the locale's zone and culture, the PC's own if it is null. `value` is what `{0}` shows in a
    /// template parsed with a value; it is missing like any other when null.
    public TemplateResult Render(SensorSnapshot sensors, TimeLocale? locale = null, SensorValue? value = null)
    {
        var text = new StringBuilder();
        var stale = false;

        foreach (var part in _parts)
        {
            switch (part)
            {
                case Literal literal:
                    text.Append(literal.Text);
                    break;
                case Placeholder { IsValue: true } placeholder when value is { } own:
                    text.Append(Write(placeholder, own, locale));
                    break;
                case Placeholder { IsValue: false } placeholder when placeholder.Expression.Evaluate(sensors) is { } reading:
                    text.Append(Write(placeholder, reading, locale));
                    break;
                default:
                    text.Append(Formatting.Missing);
                    stale = true;
                    break;
            }
        }

        return new TemplateResult(text.ToString(), stale);
    }

    /// A placeholder's value as text: a number through its time pattern, helper or number format; a string as it is.
    static string Write(Placeholder placeholder, SensorValue value, TimeLocale? locale) =>
        value.Number is not { } n ? Formatting.Format(value, placeholder.Format)
        : placeholder.TimePattern is { } pattern ? Formatting.Time(n, pattern, locale ?? TimeLocale.Pc)
        : placeholder.Helper is { } helper ? helper(n)
        : Formatting.Format(value, placeholder.Format);

    static Placeholder ParsePlaceholder(string text, int start, int end, bool withValue)
    {
        var content = text[start..end];
        var pipe = content.IndexOf('|');
        var colon = content.IndexOf(':');
        var name = pipe >= 0 ? content[(pipe + 1)..(colon > pipe ? colon : content.Length)].Trim() : null;
        // A colon after `|time` starts the helper's own date and time pattern, not a number format. A misspelt `time`
        // with a pattern, such as `|Time:HH`, is reported as the unknown helper it is.
        if (name is not null && colon >= 0 && (colon < pipe || name != Formatting.TimeHelper))
        {
            if (colon > pipe && Formatting.Helper(name) is null) throw UnknownHelper(name, start + pipe + 1);
            throw new ExpressionException("A placeholder takes a format after ':' or a helper after '|', not both", start + Math.Min(pipe, colon));
        }

        var expressionText = content;
        string? format = null;
        Func<double, string>? helper = null;
        string? timePattern = null;

        if (name is not null)
        {
            expressionText = content[..pipe];
            if (name == Formatting.TimeHelper)
                timePattern = colon > pipe ? TimePattern(content[(colon + 1)..], start + colon) : Formatting.DefaultTimePattern;
            else
                helper = Formatting.Helper(name) ?? throw UnknownHelper(name, start + pipe + 1);
        }
        else if (colon >= 0)
        {
            expressionText = content[..colon];
            format = content[(colon + 1)..];
            if (format.Length == 0) throw new ExpressionException("Empty format after ':'", start + colon);
            if (Formatting.NumberFormatError(format) is { } error)
                throw new ExpressionException(error, start + colon + 1);
        }

        try
        {
            return new Placeholder(Expression.Parse(expressionText), format, helper, timePattern, withValue && expressionText.Trim() == "0");
        }
        catch (ExpressionException ex)
        {
            throw new ExpressionException(ex.Reason, start + ex.Position);
        }
    }

    static ExpressionException UnknownHelper(string name, int position) =>
        new($"Unknown helper '{name}' (use duration, bytes or time)", position);

    /// The date and time pattern after `|time:`, whose colon is at `colon`. It runs to the closing brace, colons included.
    static string TimePattern(string pattern, int colon) =>
        pattern.Length == 0 ? throw new ExpressionException($"Empty time pattern after '{Formatting.TimeHelper}:'", colon)
        : !Formatting.IsTimePattern(pattern) ? throw new ExpressionException($"Invalid time pattern '{pattern}'", colon + 1)
        : pattern;
}

public readonly record struct TemplateResult(string Text, bool Stale);
