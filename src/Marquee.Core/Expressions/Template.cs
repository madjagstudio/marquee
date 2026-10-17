using System.Text;
using Marquee.Core.Sensors;

namespace Marquee.Core.Expressions;

/// Text with placeholders: `{cpu.temp}`, `{cpu.temp:0.0}` (a .NET numeric format), `{gpu.vram_used|bytes}`,
/// `{claude.five_hour.resets_at - now|duration}`. `{{` and `}}` are literal braces.
public sealed class Template
{
    abstract record Part;
    sealed record Literal(string Text) : Part;
    sealed record Placeholder(Expression Expression, string? Format, Func<double, string>? Helper) : Part;

    readonly IReadOnlyList<Part> _parts;

    Template(IReadOnlyList<Part> parts) => _parts = parts;

    public bool IsStatic => _parts.All(p => p is Literal);

    public static Template Parse(string text)
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

                parts.Add(ParsePlaceholder(text, i + 1, close));
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
    public TemplateResult Render(SensorSnapshot sensors)
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
                case Placeholder placeholder when placeholder.Expression.Evaluate(sensors) is { } value:
                    text.Append(placeholder.Helper is { } helper && value.Number is { } n
                        ? helper(n)
                        : Formatting.Format(value, placeholder.Format));
                    break;
                default:
                    text.Append(Formatting.Missing);
                    stale = true;
                    break;
            }
        }

        return new TemplateResult(text.ToString(), stale);
    }

    static Placeholder ParsePlaceholder(string text, int start, int end)
    {
        var content = text[start..end];
        var pipe = content.IndexOf('|');
        var colon = content.IndexOf(':');
        if (pipe >= 0 && colon >= 0)
            throw new ExpressionException("A placeholder takes a format after ':' or a helper after '|', not both", start + Math.Min(pipe, colon));

        var expressionText = content;
        string? format = null;
        Func<double, string>? helper = null;

        if (pipe >= 0)
        {
            expressionText = content[..pipe];
            var name = content[(pipe + 1)..].Trim();
            helper = Formatting.Helper(name)
                ?? throw new ExpressionException($"Unknown helper '{name}' (use duration or bytes)", start + pipe + 1);
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
            return new Placeholder(Expression.Parse(expressionText), format, helper);
        }
        catch (ExpressionException ex)
        {
            throw new ExpressionException(ex.Reason, start + ex.Position);
        }
    }
}

public readonly record struct TemplateResult(string Text, bool Stale);
