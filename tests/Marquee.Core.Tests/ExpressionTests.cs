using Marquee.Core.Expressions;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class ExpressionTests
{
    static double? Eval(string text, params (string, object)[] values) =>
        Expression.Parse(text).Evaluate(TestSensors.Of(values))?.Number;

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("-2 + 5", 3)]
    [InlineData("--4", 4)]
    [InlineData("+4", 4)]
    [InlineData("7 % 4", 3)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("0.5 * 4", 2)]
    [InlineData("8 - 2 - 1", 5)]
    public void Arithmetic_follows_precedence(string text, double expected) =>
        Assert.Equal(expected, Eval(text));

    [Fact]
    public void Keys_read_from_the_snapshot() =>
        Assert.Equal(0.7, Eval("0.2 + gpu.load / 100", ("gpu.load", 50)));

    [Fact]
    public void Key_segments_after_the_first_may_start_with_a_digit() =>
        Assert.Equal(58, Eval("fps.1pct_low", ("fps.1pct_low", 58)));

    [Theory]
    [InlineData("min(3, 1, 2)", 1)]
    [InlineData("max(3, 1, 2)", 3)]
    [InlineData("max(4)", 4)]
    [InlineData("clamp(150, 0, 100)", 100)]
    [InlineData("clamp(-5, 0, 100)", 0)]
    [InlineData("clamp(50, 100, 0)", 50)]
    [InlineData("round(2.5)", 3)]
    [InlineData("round(3.14159, 2)", 3.14)]
    [InlineData("round(clamp(7.6, 0, 5) * 2)", 10)]
    public void Functions(string text, double expected) =>
        Assert.Equal(expected, Eval(text));

    [Fact]
    public void Now_is_the_snapshot_time_in_unix_seconds() =>
        Assert.Equal(TestSensors.UnixTime, Eval("now"));

    [Fact]
    public void A_lone_string_key_returns_its_text()
    {
        var value = Expression.Parse("cpu.name").Evaluate(TestSensors.Of(("cpu.name", "Ryzen 9")));

        Assert.Equal("Ryzen 9", value?.Text);
    }

    [Theory]
    [InlineData("gpu.temp")]
    [InlineData("gpu.temp + 1")]
    [InlineData("max(1, gpu.temp)")]
    [InlineData("-gpu.temp")]
    [InlineData("cpu.name * 2")]
    [InlineData("1 / 0")]
    [InlineData("5 % 0")]
    public void Missing_strings_and_non_finite_results_evaluate_to_null(string text) =>
        Assert.Null(Expression.Parse(text).Evaluate(TestSensors.Of(("cpu.name", "Ryzen 9"))));

    [Theory]
    [InlineData("", "Expression is empty", 1)]
    [InlineData("1 +", "Expression ended unexpectedly", 4)]
    [InlineData("(1", "Expected ')'", 3)]
    [InlineData("1 2", "Unexpected '2'", 3)]
    [InlineData("#", "Unexpected '#'", 1)]
    [InlineData("1..2", "Invalid number '1..2'", 1)]
    [InlineData("foo(1)", "Unknown function 'foo' (use min, max, clamp or round)", 1)]
    [InlineData("1 + clamp(1, 2)", "clamp() takes 3 arguments, not 2", 5)]
    [InlineData("round()", "round() takes 1 or 2 arguments, not 0", 1)]
    [InlineData("cpu.temp(1)", "'cpu.temp' is not a function", 1)]
    [InlineData("max(1, 2", "Expected ')'", 9)]
    [InlineData("cpu.temp.", "Unexpected '.'", 9)]
    public void Malformed_expressions_report_the_reason_and_character(string text, string reason, int character)
    {
        var ex = Assert.Throws<ExpressionException>(() => Expression.Parse(text));

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(character, ex.Position + 1);
        Assert.Equal($"{reason} at character {character}", ex.Message);
    }
}
