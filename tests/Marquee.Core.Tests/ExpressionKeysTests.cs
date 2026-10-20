using Marquee.Core.Expressions;

namespace Marquee.Core.Tests;

/// The sensor keys an expression or a template reads, which the theme editor's test values panel lists: each once, in
/// the order it first appears, without `now` (the clock, not a sensor) or the functions.
public class ExpressionKeysTests
{
    [Theory]
    [InlineData("cpu.temp", new[] { "cpu.temp" })]
    [InlineData("fps.1pct_low", new[] { "fps.1pct_low" })]
    [InlineData("1 + 2 * 3", new string[0])]
    [InlineData("now", new string[0])]
    [InlineData("claude.five_hour.resets_at - now", new[] { "claude.five_hour.resets_at" })]
    [InlineData("100 - ram.used_pct", new[] { "ram.used_pct" })]
    [InlineData("-(gpu.load % cpu.load) / round(cpu.clock, 2)", new[] { "gpu.load", "cpu.load", "cpu.clock" })]
    [InlineData("clamp(max(cpu.load, gpu.load) / 100, 0, 1)", new[] { "cpu.load", "gpu.load" })]
    public void An_expression_lists_the_keys_it_reads(string text, string[] keys) =>
        Assert.Equal(keys, Expression.Parse(text).Keys);

    [Fact]
    public void A_key_read_twice_is_listed_once_where_it_first_appears() =>
        Assert.Equal(["b", "a"], Expression.Parse("b + a * b - max(a, b, now)").Keys);

    [Fact]
    public void A_key_named_like_a_function_is_a_key_when_it_is_not_called() =>
        Assert.Equal(["min", "x"], Expression.Parse("min + max(min, x)").Keys);

    [Fact]
    public void Keys_compare_exactly_as_the_sensor_store_does() =>
        Assert.Equal(["cpu.temp", "CPU.temp"], Expression.Parse("cpu.temp + CPU.temp").Keys);

    [Theory]
    [InlineData("CPU", new string[0])]
    [InlineData("{{cpu.temp}}", new string[0])]
    [InlineData("{cpu.temp:0}° · {gpu.vram_used|bytes} {cpu.temp}", new[] { "cpu.temp", "gpu.vram_used" })]
    [InlineData("{now|time:HH:mm:ss}", new string[0])]
    [InlineData("{now|time}", new string[0])]
    [InlineData("resets in {claude.seven_day.resets_at - now|duration}", new[] { "claude.seven_day.resets_at" })]
    [InlineData("{cpu.clock / 1000:0.0} GHz, {max(cpu.temp, gpu.temp):0}°", new[] { "cpu.clock", "cpu.temp", "gpu.temp" })]
    [InlineData("{0} and {1:0}", new string[0])]
    public void A_template_lists_the_keys_its_placeholders_read(string text, string[] keys) =>
        Assert.Equal(keys, Template.Parse(text).Keys);

    /// In a value widget's spans `{0}` is the widget's value, so the template itself reads no key for it: the widget's
    /// source is the key (ThemeKeys lists it).
    [Fact]
    public void A_value_placeholder_reads_no_key_of_its_own() =>
        Assert.Equal(["cpu.max"], Template.Parse("{0:0} of {cpu.max} ({0|bytes})", withValue: true).Keys);

    /// Keys is built on each call, so it stays out of a record's text, which would build it, and show only the list's
    /// type name anyway.
    [Fact]
    public void An_expressions_text_leaves_out_its_keys() =>
        Assert.DoesNotContain("Keys", Expression.Parse("cpu.temp + 1").ToString());

    [Fact]
    public void A_templates_keys_can_not_be_changed_through_the_list()
    {
        var template = Template.Parse("{cpu.temp}");

        Assert.False(template.Keys is List<string>, "the template's own list is handed out");
        Assert.Equal(["cpu.temp"], template.Keys);
    }
}
