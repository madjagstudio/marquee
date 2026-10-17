using Marquee.Core.Sensors;

namespace Marquee.Core.Expressions;

/// A parsed expression over sensor keys. Evaluating yields null when a key it reads is missing or stale,
/// when it does arithmetic on a string, or when the result isn't a finite number.
public abstract record Expression
{
    public static Expression Parse(string text) => ExpressionParser.Parse(text);

    public abstract SensorValue? Evaluate(SensorSnapshot sensors);

    protected static SensorValue? Numeric(double? value) =>
        value is { } v && double.IsFinite(v) ? SensorValue.Of(v) : null;
}

sealed record NumberExpr(double Value) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) => SensorValue.Of(Value);
}

sealed record KeyExpr(string Key) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) =>
        sensors.TryGet(Key, out var value) ? value : null;
}

/// The frame time as Unix seconds, so `claude.five_hour.resets_at - now` is the seconds remaining.
sealed record NowExpr : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) =>
        SensorValue.Of(sensors.Now.ToUnixTimeMilliseconds() / 1000.0);
}

sealed record NegateExpr(Expression Operand) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) =>
        Numeric(-Operand.Evaluate(sensors)?.Number);
}

sealed record BinaryExpr(char Operator, Expression Left, Expression Right) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors)
    {
        if (Left.Evaluate(sensors)?.Number is not { } a || Right.Evaluate(sensors)?.Number is not { } b)
            return null;

        return Numeric(Operator switch
        {
            '+' => a + b,
            '-' => a - b,
            '*' => a * b,
            '/' => a / b,
            '%' => a % b,
            _ => throw new InvalidOperationException($"Unknown operator {Operator}"),
        });
    }
}

sealed record CallExpr(string Function, IReadOnlyList<Expression> Arguments) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors)
    {
        var args = new double[Arguments.Count];
        for (var i = 0; i < args.Length; i++)
        {
            if (Arguments[i].Evaluate(sensors)?.Number is not { } n) return null;
            args[i] = n;
        }

        return Numeric(Function switch
        {
            "min" => args.Min(),
            "max" => args.Max(),
            "clamp" => Math.Clamp(args[0], Math.Min(args[1], args[2]), Math.Max(args[1], args[2])),
            "round" => Math.Round(args[0], args.Length > 1 ? Math.Clamp((int)args[1], 0, 15) : 0, MidpointRounding.AwayFromZero),
            _ => throw new InvalidOperationException($"Unknown function {Function}"),
        });
    }
}
