using Marquee.Core.Expressions;

namespace Marquee.Editor.Model;

public enum TokenKind { Reading, Time, Date }
public enum TimeFormat { Hour12, Hour12Seconds, Hour24, Hour24Seconds }
public enum DateFormat { Long, Short, MonthDay, Weekday }

public abstract record TemplatePart
{
    public sealed record Literal(string Text) : TemplatePart;
    public sealed record Token(string Key, string? Format, TokenKind Kind) : TemplatePart
    {
        internal TemplateSyntaxPart? Syntax { get; init; }
        internal TokenKind OriginalKind { get; init; }
    }
}

/// Editable template parts; Core owns all token grammar and validation.
public static class TextTemplate
{
    public static IReadOnlyList<TemplatePart> Parse(string text) => Template.Parse(text).Syntax.Select(part =>
    {
        if (part.Literal is { } literal) return (TemplatePart)new TemplatePart.Literal(literal);
        var kind = part.Helper == "date" ? TokenKind.Date
            : part.Helper == "time" ? IsDatePattern(part.Format) ? TokenKind.Date : TokenKind.Time
            : TokenKind.Reading;
        return new TemplatePart.Token(part.Key!, part.Format, kind) { Syntax = part, OriginalKind = kind };
    }).ToList().AsReadOnly();

    public static string Write(IEnumerable<TemplatePart> parts) => string.Concat(parts.Select(part => part switch
    {
        TemplatePart.Literal literal => literal.Text.Replace("{", "{{").Replace("}", "}}"),
        TemplatePart.Token token => WriteToken(token),
        _ => throw new ArgumentException("Unknown template part", nameof(parts)),
    }));

    static string WriteToken(TemplatePart.Token token)
    {
        if (token.Syntax is { } original && token.Kind == token.OriginalKind)
        {
            var source = original.Source;
            if (token.Format != original.Format)
            {
                source = original.FormatStart >= 0
                    ? source[..(original.FormatStart - 1)] + (token.Format is null ? "}" : ":" + token.Format + "}")
                    : source[..^1] + (token.Format is null ? "}" : ":" + token.Format + "}");
                if (token.Kind == TokenKind.Date && original.Helper == "time")
                {
                    var helperStart = source.IndexOf("time", source.IndexOf('|'), StringComparison.Ordinal);
                    source = source[..helperStart] + "date" + source[(helperStart + 4)..];
                }
            }
            return source[..original.KeyStart] + token.Key + source[(original.KeyStart + original.KeyLength)..];
        }
        var helper = token.Kind switch { TokenKind.Time => "|time", TokenKind.Date => "|date", _ => "" };
        return "{" + token.Key + helper + (token.Format is null ? "" : ":" + token.Format) + "}";
    }

    public static string Pattern(TimeFormat format) => format switch
    {
        TimeFormat.Hour12 => "h:mm tt", TimeFormat.Hour12Seconds => "h:mm:ss tt",
        TimeFormat.Hour24 => "HH:mm", TimeFormat.Hour24Seconds => "HH:mm:ss",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string Pattern(DateFormat format) => format switch
    {
        DateFormat.Long => "D", DateFormat.Short => "d", DateFormat.MonthDay => "M", DateFormat.Weekday => "dddd",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    static bool IsDatePattern(string? pattern)
    {
        if (pattern is null) return false;
        if (pattern.Length == 1) return "dDMmYy".Contains(pattern[0]);
        var date = false;
        char quote = '\0';
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\') { i++; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '\'' or '"') { quote = c; continue; }
            if ("hHmstfFzK".Contains(c)) return false;
            if ("dMyg".Contains(c)) date = true;
        }
        return date;
    }

    public static bool IsClock(string text)
    {
        var parts = Parse(text);
        return parts.OfType<TemplatePart.Token>().Any() && parts.All(part => part switch
        {
            TemplatePart.Literal literal => string.IsNullOrWhiteSpace(literal.Text),
            TemplatePart.Token token => token.Key == "now" && token.Kind is TokenKind.Time or TokenKind.Date,
            _ => false,
        });
    }

    public static string ChangeFormat(string text, TokenKind kind, string format) => Write(Parse(text).SelectMany(part =>
        part is TemplatePart.Token token && token.Kind == kind ? ChangeTokenFormat(token, format) : [part]));

    public static IReadOnlyList<TemplatePart> ChangeTokenFormat(TemplatePart.Token token, string format)
    {
        // Standard general/full patterns contain both components. Split only on edit so changing
        // one component cannot discard the other, while untouched templates still round-trip exactly.
        if (token.Kind is TokenKind.Time or TokenKind.Date && token.Format is "f" or "F" or "g" or "G")
            return [new TemplatePart.Token(token.Key, token.Kind == TokenKind.Date ? format
                    : token.Format is "f" or "F" ? "D" : "d", TokenKind.Date),
                new TemplatePart.Literal(" "),
                new TemplatePart.Token(token.Key, token.Kind == TokenKind.Time ? format
                    : token.Format is "f" or "g" ? "t" : "T", TokenKind.Time)];
        return [token with { Format = format }];
    }

    public static string PickReading(string text, string key)
    {
        var parts = Parse(text).ToList();
        var readings = parts.OfType<TemplatePart.Token>().Where(token => token.Kind == TokenKind.Reading).ToList();
        if (readings.Count > 1) return text;
        if (readings.Count == 1) parts[parts.IndexOf(readings[0])] = readings[0] with { Key = key };
        else parts.Add(new TemplatePart.Token(key, "0", TokenKind.Reading));
        return Write(parts);
    }
}
