using Marquee.Core.Expressions;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

public class TemplateTests
{
    static TemplateResult Render(string template, params (string, object)[] values) =>
        Template.Parse(template).Render(TestSensors.Of(values));

    [Fact]
    public void Plain_text_is_static_and_unchanged()
    {
        var template = Template.Parse("CPU");

        Assert.True(template.IsStatic);
        Assert.Equal(new TemplateResult("CPU", false), template.Render(TestSensors.Of()));
    }

    [Fact]
    public void Placeholders_use_the_default_number_format() =>
        Assert.Equal("72.46°C", Render("{cpu.temp}°C", ("cpu.temp", 72.456)).Text);

    [Fact]
    public void Placeholders_take_a_numeric_format() =>
        Assert.Equal("72°C", Render("{cpu.temp:0}°C", ("cpu.temp", 72.456)).Text);

    [Fact]
    public void Several_placeholders_and_expressions()
    {
        var result = Render("{claude.sessions.busy}/{claude.sessions.total} working, {gpu.load / 100:0.0}",
            ("claude.sessions.busy", 2), ("claude.sessions.total", 5), ("gpu.load", 50));

        Assert.Equal("2/5 working, 0.5", result.Text);
        Assert.False(result.Stale);
    }

    [Fact]
    public void Duration_helper_counts_down_to_a_timestamp() =>
        Assert.Equal("resets in 2h 14m",
            Render("resets in {claude.five_hour.resets_at - now|duration}", ("claude.five_hour.resets_at", TestSensors.UnixTime + 8040)).Text);

    [Fact]
    public void Bytes_helper() =>
        Assert.Equal("8.5 GB", Render("{gpu.vram_used|bytes}", ("gpu.vram_used", 8.5 * 1024 * 1024 * 1024)).Text);

    [Fact]
    public void Strings_render_as_they_are() =>
        Assert.Equal("Ryzen 9 7950X", Render("{cpu.name}", ("cpu.name", "Ryzen 9 7950X")).Text);

    [Fact]
    public void Missing_values_show_a_dash_and_mark_the_result_stale() =>
        Assert.Equal(new TemplateResult("— / 50%", true), Render("{cpu.temp} / {gpu.load}%", ("gpu.load", 50)));

    [Fact]
    public void Doubled_braces_are_literal() =>
        Assert.Equal("{72}", Render("{{{cpu.temp}}}", ("cpu.temp", 72)).Text);

    [Theory]
    [InlineData("{cpu.temp", "Unclosed '{'", 1)]
    [InlineData("a } b", "Unmatched '}' (write '}}' for a literal brace)", 3)]
    [InlineData("x {1 +}", "Expression ended unexpectedly", 7)]
    [InlineData("{}", "Expression is empty", 2)]
    [InlineData("{gpu.load|kelvin}", "Unknown helper 'kelvin' (use duration, bytes or time)", 11)]
    [InlineData("{gpu.load:0|bytes}", "A placeholder takes a format after ':' or a helper after '|', not both", 10)]
    [InlineData("{gpu.load:}", "Empty format after ':'", 10)]
    [InlineData("{gpu.load:X}", "Invalid number format 'X'", 11)]
    [InlineData("{x:F999999999}", "number format 'F999999999' asks for too many digits (the limit is 15)", 4)]
    [InlineData("{x:000000000000000000000000000000000}", "number format '000000000000000000000000000000000' is too long (the limit is 32 characters)", 4)]
    public void Malformed_templates_report_the_character(string template, string reason, int character)
    {
        var ex = Assert.Throws<ExpressionException>(() => Template.Parse(template));

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(character, ex.Position + 1);
    }
}
