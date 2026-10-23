using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using System.Globalization;

namespace Marquee.Editor.Model.Tests;

public class TextTemplateTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" plain words ")]
    [InlineData("CPU {cpu.temp} / {gpu.temp:0.00}")]
    [InlineData("{now|time:h:mm:ss tt} {now|date:dddd, MMMM d, yyyy}")]
    [InlineData("{{CPU}} {{{cpu.temp:0}}}")]
    [InlineData("{ cpu.temp :0.0} {gpu.vram_used|bytes} {cpu.load + 2:0}")]
    [InlineData("{now | time: 'at' HH:mm }")]
    public void Syntax_round_trips_exactly(string text) => Assert.Equal(text, TextTemplate.Write(TextTemplate.Parse(text)));

    [Fact]
    public void Tokens_have_their_reading_and_format()
    {
        var parts = TextTemplate.Parse("CPU {cpu.temp:0} {now|time:HH:mm} {now|date:D}");
        Assert.Equal("CPU ", Assert.IsType<TemplatePart.Literal>(parts[0]).Text);
        var tokens = parts.OfType<TemplatePart.Token>().ToList();
        Assert.Equal(("cpu.temp", "0", TokenKind.Reading), (tokens[0].Key, tokens[0].Format, tokens[0].Kind));
        Assert.Equal(TokenKind.Time, tokens[1].Kind);
        Assert.Equal(TokenKind.Date, tokens[2].Kind);
    }

    [Theory]
    [InlineData("{cpu.temp")]
    [InlineData("{cpu.temp|unknown}")]
    [InlineData("{now|date:}")]
    public void Invalid_syntax_is_rejected_by_core(string text) => Assert.Throws<ExpressionException>(() => TextTemplate.Parse(text));

    [Fact]
    public void Changing_clock_format_preserves_other_parts_and_spelling()
    {
        const string text = "{now | time:h:mm tt} {now|date:dddd}";
        Assert.Equal("{now | time:HH:mm} {now|date:dddd}", TextTemplate.ChangeFormat(text, TokenKind.Time, TextTemplate.Pattern(TimeFormat.Hour24)));
    }

    [Fact]
    public void Date_choices_render_through_core()
    {
        var snapshot = SensorSnapshot.Of(new DateTimeOffset(2026, 10, 22, 17, 30, 45, TimeSpan.Zero), new Dictionary<string, SensorEntry>());
        var locale = new TimeLocale(TimeZoneInfo.Utc, CultureInfo.GetCultureInfo("en-US"));
        var expected = new[] { "Thursday, October 22, 2026", "10/22/2026", "October 22", "Thursday" };
        foreach (var format in Enum.GetValues<DateFormat>())
        {
            var text = TextTemplate.Write([new TemplatePart.Token("now", TextTemplate.Pattern(format), TokenKind.Date)]);
            var rendered = Template.Parse(text).Render(snapshot, locale);
            Assert.False(rendered.Stale);
            Assert.Equal(expected[(int)format], rendered.Text);
        }
    }

    [Theory]
    [InlineData("f", "D")]
    [InlineData("F", "D")]
    [InlineData("g", "d")]
    [InlineData("G", "d")]
    public void Changing_a_combined_clock_time_keeps_its_date(string pattern, string date)
    {
        var text = "Before {now|time:" + pattern + "} after";
        Assert.Equal(text, TextTemplate.Write(TextTemplate.Parse(text)));
        Assert.Equal("Before {now|date:" + date + "} {now|time:HH:mm} after",
            TextTemplate.ChangeFormat(text, TokenKind.Time, "HH:mm"));
    }

    [Theory]
    [InlineData("f", "t")]
    [InlineData("F", "T")]
    [InlineData("g", "t")]
    [InlineData("G", "T")]
    public void Changing_a_combined_date_keeps_its_time(string pattern, string time) =>
        Assert.Equal("{now|date:dddd} {now|time:" + time + "}",
            TextTemplate.ChangeFormat("{now|date:" + pattern + "}", TokenKind.Date, "dddd"));

    [Theory]
    [InlineData("{now|time:dddd}", "{now|date:D}")]
    [InlineData("{ now | time:dddd}", "{ now | date:D}")]
    public void Editing_a_legacy_date_uses_the_date_helper(string text, string expected)
    {
        Assert.Equal(text, TextTemplate.Write(TextTemplate.Parse(text)));
        Assert.Equal(expected, TextTemplate.ChangeFormat(text, TokenKind.Date, "D"));
    }

    [Theory]
    [InlineData("{now|time:HH:mm}", true)]
    [InlineData("{now|time:HH:mm} {now|date:D}", true)]
    [InlineData("{now|time:dddd}", true)]
    [InlineData("Time {now|time:HH:mm}", false)]
    [InlineData("{cpu.temp:0}", false)]
    [InlineData(" ", false)]
    public void Only_clock_tokens_make_a_clock(string text, bool expected) => Assert.Equal(expected, TextTemplate.IsClock(text));

    [Theory]
    [InlineData("dddd, MMMM d, yyyy")]
    [InlineData("dd MMM yyyy")]
    [InlineData("'Today is' dddd")]
    public void Existing_date_patterns_get_date_choices(string pattern) =>
        Assert.Equal(TokenKind.Date, Assert.IsType<TemplatePart.Token>(Assert.Single(TextTemplate.Parse("{now|time:" + pattern + "}"))).Kind);

    [Theory]
    [InlineData(TimeFormat.Hour12, "h:mm tt")]
    [InlineData(TimeFormat.Hour12Seconds, "h:mm:ss tt")]
    [InlineData(TimeFormat.Hour24, "HH:mm")]
    [InlineData(TimeFormat.Hour24Seconds, "HH:mm:ss")]
    public void Time_choices_write_the_requested_patterns(TimeFormat choice, string expected) => Assert.Equal(expected, TextTemplate.Pattern(choice));
}
