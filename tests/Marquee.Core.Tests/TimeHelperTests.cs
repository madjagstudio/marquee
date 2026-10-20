using System.Globalization;
using Marquee.Core.Expressions;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// The `time` helper: `{now|time:HH:mm}` in templates and `format: "time:HH:mm"` on value widgets, with the clock
/// pinned through the test sensors and the zone and culture pinned through TestClock.
public class TimeHelperTests
{
    const string ResetsAt = "claude.five_hour.resets_at";

    static TemplateResult Render(string template, TimeLocale locale, params (string, object)[] values) =>
        Template.Parse(template).Render(TestSensors.Of(values), locale);

    static string English(string template, params (string, object)[] values) =>
        Render(template, TestClock.Locale(), values).Text;

    // Newer ICU writes some cultures' short times with a no-break space before AM or PM, which the helper writes as
    // U+00A0; the test only cares that it is the culture's short time.
    static string Spaced(string text) => text.Replace('\u00A0', ' ');

    [Theory]
    [InlineData("{now|time:HH:mm}", "17:30")]
    [InlineData("{now|time:h:mm tt}", "5:30 PM")]
    [InlineData("{now|time:ddd}", "Fri")]
    [InlineData("{now|time:dddd, MMMM d, yyyy}", "Friday, October 16, 2026")]
    [InlineData("{now|time:D}", "Friday, October 16, 2026")]
    [InlineData("{now|time:yyyy-MM-dd HH:mm:ss}", "2026-10-16 17:30:00")]
    [InlineData("{now|time:HH:mm zzz}", "17:30 +05:30")]
    [InlineData("Today is {now|time:dddd}.", "Today is Friday.")]
    [InlineData("{now|time:'Today is' dddd}", "Today is Friday")]
    [InlineData("{now|time:%H}", "17")]
    [InlineData("{now|time: HH:mm }", " 17:30 ")]
    public void Patterns_write_now_in_the_local_zone(string template, string expected) =>
        Assert.Equal(expected, English(template));

    [Fact]
    public void Without_a_pattern_it_is_the_cultures_short_time()
    {
        Assert.Equal("5:30 PM", Spaced(English("{now|time}")));
        Assert.Equal("17:30", Render("{now|time}", TestClock.Locale("de-DE")).Text);
    }

    [Fact]
    public void A_narrow_no_break_space_is_written_as_a_no_break_space()
    {
        // CLDR 42 and later write US English short times with U+202F before AM or PM, which a theme font may not have.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-US").Clone();
        culture.DateTimeFormat.ShortTimePattern = "h:mm\u202Ftt";
        var locale = new TimeLocale(TestClock.Zone, culture);

        Assert.Equal("5:30\u00A0PM", Formatting.Time(TestSensors.UnixTime, "t", locale));
        Assert.Equal("17\u00A030", Formatting.Time(TestSensors.UnixTime, "HH\u202Fmm", locale));
    }

    [Fact]
    public void Day_and_month_names_use_the_culture() =>
        Assert.Equal("Freitag, 16. Oktober 2026", Render("{now|time:dddd, d. MMMM yyyy}", TestClock.Locale("de-DE")).Text);

    [Fact]
    public void Numbers_beside_it_stay_invariant() =>
        Assert.Equal("72.5° at 17:30", Render("{cpu.temp:0.0}° at {now|time}", TestClock.Locale("de-DE"), ("cpu.temp", 72.5)).Text);

    [Fact]
    public void A_timestamp_is_written_in_the_local_zone() =>
        Assert.Equal("resets 19:44", English("resets {" + ResetsAt + "|time:HH:mm}", (ResetsAt, TestSensors.UnixTime + 8040)));

    [Fact]
    public void The_zone_can_move_the_date() =>
        Assert.Equal("Sat 01:00", Render("{now|time:ddd HH:mm}", TestClock.Locale(zone: TestClock.Fixed(TimeSpan.FromHours(13)))).Text);

    [Fact]
    public void A_missing_value_shows_a_dash_and_is_stale() =>
        Assert.Equal(new TemplateResult("resets —", true), Render("resets {" + ResetsAt + "|time}", TestClock.Locale()));

    [Fact]
    public void An_expired_value_shows_a_dash_and_is_stale()
    {
        var store = TestSensors.Store();
        store.Set(ResetsAt, TestSensors.UnixTime, ttl: TimeSpan.Zero);

        var result = Template.Parse("{" + ResetsAt + "|time:HH:mm}").Render(store.Snapshot(), TestClock.Locale());

        Assert.Equal(new TemplateResult("—", true), result);
    }

    [Fact]
    public void Strings_show_as_they_are() =>
        Assert.Equal("soon", English("{" + ResetsAt + "|time}", (ResetsAt, "soon")));

    [Fact]
    public void Without_a_locale_it_uses_the_pcs_zone_and_culture()
    {
        var pc = TimeLocale.Pc;

        Assert.Same(TimeZoneInfo.Local, pc.Zone);
        Assert.Equal(CultureInfo.CurrentCulture, pc.Culture);
        Assert.Equal(TestSensors.Time.ToLocalTime().ToString("dddd HH:mm", CultureInfo.CurrentCulture),
            Template.Parse("{now|time:dddd HH:mm}").Render(TestSensors.Of()).Text);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e300)]
    [InlineData(-1e300)]
    [InlineData(253402300800)]
    public void Times_no_date_can_hold_show_missing(double seconds) =>
        Assert.Equal(Formatting.Missing, Formatting.Time(seconds, "HH:mm", TestClock.Locale()));

    [Fact]
    public void The_last_second_a_date_can_hold() =>
        Assert.Equal("9999-12-31 23:59:59", Formatting.Time(253402300799, "yyyy-MM-dd HH:mm:ss", TestClock.Locale(zone: TimeZoneInfo.Utc)));

    // Saudi Arabia's default calendar, Umm al-Qura, starts in 1900; 1811 is outside it.
    [Fact]
    public void A_time_outside_the_cultures_calendar_shows_missing() =>
        Assert.Equal(Formatting.Missing, Formatting.Time(-5_000_000_000, "yyyy", TestClock.Locale("ar-SA")));

    [Theory]
    [InlineData("{now|time:q}", "Invalid time pattern 'q'", 11)]
    [InlineData("{now|time:U}", "Invalid time pattern 'U'", 11)]
    [InlineData("{now|time:HH 'h}", "Invalid time pattern 'HH 'h'", 11)]
    [InlineData("{now|time:}", "Empty time pattern after 'time:'", 10)]
    [InlineData("{now:0|time}", "A placeholder takes a format after ':' or a helper after '|', not both", 5)]
    [InlineData("{now:0|time:HH:mm}", "A placeholder takes a format after ':' or a helper after '|', not both", 5)]
    [InlineData("{gpu.vram_used|bytes:0}", "A placeholder takes a format after ':' or a helper after '|', not both", 15)]
    [InlineData("{now|times:HH}", "Unknown helper 'times' (use duration, bytes or time)", 6)]
    [InlineData("{now|Time:HH}", "Unknown helper 'Time' (use duration, bytes or time)", 6)]
    [InlineData("{now| clock :HH}", "Unknown helper 'clock' (use duration, bytes or time)", 6)]
    [InlineData("{now|clock}", "Unknown helper 'clock' (use duration, bytes or time)", 6)]
    public void Malformed_time_placeholders_report_the_character(string template, string reason, int character)
    {
        var ex = Assert.Throws<ExpressionException>(() => Template.Parse(template));

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(character, ex.Position + 1);
    }

    [Fact]
    public void The_pattern_runs_to_the_closing_brace_colons_and_bars_included() =>
        Assert.Equal("17:30:00 | 2026", English("{now|time:HH:mm:ss | yyyy}"));

    [Theory]
    [InlineData("time:HH:mm", "17:30")]
    [InlineData("time:dddd", "Friday")]
    [InlineData("time:h:mm tt", "5:30 PM")]
    [InlineData("time:'at' HH:mm", "at 17:30")]
    [InlineData("time:%H", "17")]
    public void Value_widget_formats_take_a_pattern(string format, string expected) =>
        Assert.Equal(expected, Formatting.Apply(format, SensorValue.Of(TestSensors.UnixTime), TestClock.Locale()));

    [Fact]
    public void A_value_widget_format_of_time_alone_is_the_short_time()
    {
        Assert.Equal("5:30 PM", Spaced(Formatting.Apply("time", SensorValue.Of(TestSensors.UnixTime), TestClock.Locale())));
        Assert.Equal("idle", Formatting.Apply("time", SensorValue.Of("idle"), TestClock.Locale()));
    }

    [Fact]
    public void A_composite_format_starting_with_time_stays_a_composite_format()
    {
        Assert.Null(Formatting.FormatError("time: {0}"));
        Assert.Equal("time: 5", Formatting.Apply("time: {0}", SensorValue.Of(5), TestClock.Locale()));
    }

    [Theory]
    [InlineData("time", null)]
    [InlineData("time:HH:mm", null)]
    [InlineData("time:dddd, d MMMM", null)]
    [InlineData("time:q", "format \"time:q\" is not a valid time pattern such as \"time:HH:mm\"")]
    [InlineData("time:", "format \"time:\" is not a valid time pattern such as \"time:HH:mm\"")]
    [InlineData("times", "format \"times\" never shows the value; include {0}, e.g. \"{0:0.0}°C\"")]
    public void FormatError_checks_the_time_pattern(string format, string? error) =>
        Assert.Equal(error, Formatting.FormatError(format));

    const string Header = "canvas: { orientation: landscape }\nwidgets:\n";

    [Fact]
    public void Value_widgets_parse_a_time_format()
    {
        var spec = Layouts.Parse(Header + "  - { type: value, at: [0, 0], size: [100, 40], source: now, format: \"time:HH:mm\" }\n");

        Assert.Equal("time:HH:mm", Assert.IsType<ValueWidget>(Assert.Single(spec.Widgets)).Format);
    }

    [Theory]
    [InlineData("  - type: text\n    at: [0, 0]\n    size: [10, 10]\n    text: \"{now|time:q}\"\n", 6,
        "text: Invalid time pattern 'q' at character 11")]
    [InlineData("  - type: value\n    at: [0, 0]\n    size: [10, 10]\n    source: now\n    format: \"time:q\"\n", 7,
        "format \"time:q\" is not a valid time pattern such as \"time:HH:mm\"")]
    public void Bad_time_patterns_are_file_line_errors(string widgets, int line, string reason)
    {
        var ex = Layouts.Error(Header + widgets);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }
}
