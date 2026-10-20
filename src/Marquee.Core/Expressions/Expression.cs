using System.Text;
using Marquee.Core.Sensors;

namespace Marquee.Core.Expressions;

/// A parsed expression over sensor keys. Evaluating yields null when a key it reads is missing or stale,
/// when it does arithmetic on a string, or when the result isn't a finite number.
public abstract record Expression
{
    public static Expression Parse(string text) => ExpressionParser.Parse(text);

    public abstract SensorValue? Evaluate(SensorSnapshot sensors);

    /// The sensor keys it reads, each once, in the order they first appear. `now` is the clock, not a sensor key, so it
    /// isn't one of them. Built on each call: a record's fields are its equality, so the list can't be kept in one.
    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = new List<string>();
            AddKeys(keys);
            return keys;
        }
    }

    /// Its text leaves Keys out: printing it would build the list, to show only the list's type name. The derived
    /// records print their own members after this.
    protected virtual bool PrintMembers(StringBuilder builder) => false;

    /// Adds the keys it reads that `keys` doesn't hold yet, in the order they appear. Keys compare exactly, as the
    /// sensor store's do.
    internal virtual void AddKeys(List<string> keys)
    {
    }

    protected static SensorValue? Numeric(double? value) =>
        value is { } v && double.IsFinite(v) ? SensorValue.Of(v) : null;
}

sealed record NumberExpr(double Value) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) => Numeric(Value);
}

sealed record KeyExpr(string Key) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) =>
        !sensors.TryGet(Key, out var value) ? null
        : value.Number is { } number ? Numeric(number)
        : value;

    internal override void AddKeys(List<string> keys)
    {
        if (!keys.Contains(Key)) keys.Add(Key);
    }
}

/// The frame time as Unix seconds, so `claude.five_hour.resets_at - now` is the seconds remaining.
sealed record NowExpr : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) =>
        SensorValue.Of(sensors.Now.ToUnixTimeMilliseconds() / 1000.0);

    // Not a key, but a list that asks is told the clock is read.
    internal override void AddKeys(List<string> keys)
    {
        if (keys is KeyList list) list.ReadsClock = true;
    }
}

/// Keys as AddKeys adds them, noting too whether `now`, the clock, is read: what changes each second though no key does.
sealed class KeyList : List<string>
{
    public bool ReadsClock { get; set; }
}

sealed record NegateExpr(Expression Operand) : Expression
{
    public override SensorValue? Evaluate(SensorSnapshot sensors) =>
        Numeric(-Operand.Evaluate(sensors)?.Number);

    internal override void AddKeys(List<string> keys) => Operand.AddKeys(keys);
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

    internal override void AddKeys(List<string> keys)
    {
        Left.AddKeys(keys);
        Right.AddKeys(keys);
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

    internal override void AddKeys(List<string> keys)
    {
        foreach (var argument in Arguments) argument.AddKeys(keys);
    }
}
