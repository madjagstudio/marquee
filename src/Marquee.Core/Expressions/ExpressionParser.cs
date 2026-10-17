using System.Globalization;

namespace Marquee.Core.Expressions;

/// Recursive-descent parser. Grammar:
///   expression = term (("+" | "-") term)*
///   term       = unary (("*" | "/" | "%") unary)*
///   unary      = ("-" | "+") unary | primary
///   primary    = number | key | "now" | function "(" arguments ")" | "(" expression ")"
/// Keys are dotted names; segments after the first may start with a digit (`fps.1pct_low`).
static class ExpressionParser
{
    public static Expression Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ExpressionException("Expression is empty", 0);

        var parser = new Parser(text);
        var expression = parser.ParseExpression();
        parser.ExpectEnd();
        return expression;
    }

    sealed class Parser(string text)
    {
        int _pos;

        public Expression ParseExpression()
        {
            var left = ParseTerm();
            while (TryConsume("+-", out var op))
                left = new BinaryExpr(op, left, ParseTerm());
            return left;
        }

        public void ExpectEnd()
        {
            SkipSpace();
            if (_pos < text.Length)
                throw Error($"Unexpected '{text[_pos]}'");
        }

        Expression ParseTerm()
        {
            var left = ParseUnary();
            while (TryConsume("*/%", out var op))
                left = new BinaryExpr(op, left, ParseUnary());
            return left;
        }

        Expression ParseUnary()
        {
            if (TryConsume("-", out _)) return new NegateExpr(ParseUnary());
            if (TryConsume("+", out _)) return ParseUnary();
            return ParsePrimary();
        }

        Expression ParsePrimary()
        {
            SkipSpace();
            if (_pos >= text.Length)
                throw Error("Expression ended unexpectedly");

            var c = text[_pos];
            if (char.IsAsciiDigit(c) || c == '.') return ParseNumber();
            if (char.IsAsciiLetter(c) || c == '_') return ParseName();
            if (c == '(')
            {
                _pos++;
                var inner = ParseExpression();
                Expect(')');
                return inner;
            }

            throw Error($"Unexpected '{c}'");
        }

        Expression ParseNumber()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.')) _pos++;
            var token = text[start.._pos];
            if (!double.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                throw Error($"Invalid number '{token}'", start);
            return new NumberExpr(value);
        }

        Expression ParseName()
        {
            var start = _pos;
            SkipNameChars();
            while (_pos + 1 < text.Length && text[_pos] == '.' && IsNameChar(text[_pos + 1]))
            {
                _pos++;
                SkipNameChars();
            }

            var name = text[start.._pos];
            SkipSpace();
            if (_pos < text.Length && text[_pos] == '(')
            {
                if (name.Contains('.'))
                    throw Error($"'{name}' is not a function", start);
                _pos++;
                return ParseCall(name, start);
            }

            return name == "now" ? new NowExpr() : new KeyExpr(name);
        }

        Expression ParseCall(string name, int start)
        {
            var (min, max, takes) = name switch
            {
                "min" or "max" => (1, int.MaxValue, "at least 1 argument"),
                "clamp" => (3, 3, "3 arguments"),
                "round" => (1, 2, "1 or 2 arguments"),
                _ => throw Error($"Unknown function '{name}' (use min, max, clamp or round)", start),
            };

            var args = new List<Expression>();
            if (!TryConsume(")", out _))
            {
                do args.Add(ParseExpression());
                while (TryConsume(",", out _));
                Expect(')');
            }

            if (args.Count < min || args.Count > max)
                throw Error($"{name}() takes {takes}, not {args.Count}", start);
            return new CallExpr(name, args);
        }

        bool TryConsume(string chars, out char consumed)
        {
            SkipSpace();
            if (_pos < text.Length && chars.Contains(text[_pos]))
            {
                consumed = text[_pos++];
                return true;
            }

            consumed = default;
            return false;
        }

        void Expect(char c)
        {
            SkipSpace();
            if (_pos >= text.Length) throw Error($"Expected '{c}'");
            if (text[_pos] != c) throw Error($"Expected '{c}' but found '{text[_pos]}'");
            _pos++;
        }

        void SkipSpace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }

        void SkipNameChars()
        {
            while (_pos < text.Length && IsNameChar(text[_pos])) _pos++;
        }

        static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

        ExpressionException Error(string reason, int? at = null) => new(reason, at ?? _pos);
    }
}
