using System.Text;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Agents.Tests.TestSupport;

namespace Marquee.Providers.Agents.Tests;

public class ClaudeStatusLineTests
{
    static ClaudeStatus Parse(string json) => ClaudeStatusLine.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Subscriber_status_line_has_both_windows_and_context()
    {
        var status = ClaudeStatusLine.Parse(Fixture.Bytes("statusline/subscriber.json"));

        Assert.Equal(new ClaudeWindow(42.5, 1792247400), status.FiveHour);
        Assert.Equal(new ClaudeWindow(17, 1792573200), status.SevenDay);
        Assert.Equal(31, status.ContextPct);
    }

    [Fact]
    public void Api_key_status_line_has_no_limits_and_a_null_context()
    {
        var status = ClaudeStatusLine.Parse(Fixture.Bytes("statusline/api-key.json"));

        Assert.Equal(new ClaudeStatus(null, null, null), status);
    }

    [Fact]
    public void Each_window_may_be_absent_on_its_own()
    {
        var status = ClaudeStatusLine.Parse(Fixture.Bytes("statusline/seven-day-only.json"));

        Assert.Null(status.FiveHour);
        Assert.Equal(new ClaudeWindow(64, 1792573200), status.SevenDay);
    }

    [Theory]
    [InlineData("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":"2026-10-17T14:30:00Z"}}}""", 1792247400.0)]
    [InlineData("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":1792247400000}}}""", 1792247400.0)]
    [InlineData("""{"rate_limits":{"five_hour":{"used_percentage":5}}}""", null)]
    [InlineData("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":1e30}}}""", null)]
    [InlineData("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":-5}}}""", null)]
    [InlineData("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":0}}}""", null)]
    public void Resets_at_may_be_seconds_milliseconds_iso_or_missing(string json, double? expected) =>
        Assert.Equal(expected, Parse(json).FiveHour!.ResetsAt);

    [Fact]
    public void A_lone_surrogate_in_a_text_field_reads_as_missing()
    {
        var status = Parse("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":"\uD800"}}}""");

        Assert.Null(status.FiveHour!.ResetsAt);
    }

    [Theory]
    [InlineData("not json","the status-line input is not valid JSON")]
    [InlineData("[1, 2]", "the status-line input is not a JSON object")]
    [InlineData("""{"rate_limits":{"five_hour":"42%"}}""", "rate_limits.five_hour is not an object")]
    [InlineData("""{"rate_limits":{"seven_day":{"used":17}}}""", "rate_limits.seven_day has no used_percentage number")]
    [InlineData("""{"rate_limits":{"five_hour":{"used_percentage":"42"}}}""", "rate_limits.five_hour has no used_percentage number")]
    public void Malformed_input_is_a_format_exception(string json, string message) =>
        Assert.StartsWith(message, Assert.Throws<FormatException>(() => Parse(json)).Message);

    [Fact]
    public void Null_windows_and_unknown_fields_are_ignored()
    {
        var status = Parse("""{"rate_limits":{"five_hour":null,"spend_limit":{"used_usd":3}},"something_new":true}""");

        Assert.Equal(new ClaudeStatus(null, null, null), status);
    }

    [Fact]
    public void Apply_writes_the_keys_and_expires_them_at_the_reset()
    {
        var now = Clock.T0;
        var store = new SensorStore(() => now);

        ClaudeStatusLine.Apply(ClaudeStatusLine.Parse(Fixture.Bytes("statusline/subscriber.json")), store, Clock.T0);

        Assert.Equal(42.5, Clock.Read(store, "claude.five_hour.pct"));
        Assert.Equal(1792247400.0, Clock.Read(store, "claude.five_hour.resets_at"));
        Assert.Equal(17.0, Clock.Read(store, "claude.seven_day.pct"));
        Assert.Equal(1792573200.0, Clock.Read(store, "claude.seven_day.resets_at"));
        Assert.Equal(31.0, Clock.Read(store, "claude.context.pct"));
        Assert.Equal(Clock.T0Unix, Clock.Read(store, "claude.updated_at"));

        now = Clock.T0.AddHours(2.5); // 14:30, when the five-hour window resets
        Assert.Null(Clock.Read(store, "claude.five_hour.pct"));
        Assert.Null(Clock.Read(store, "claude.five_hour.resets_at"));
        Assert.Equal(17.0, Clock.Read(store, "claude.seven_day.pct"));
        Assert.Equal(31.0, Clock.Read(store, "claude.context.pct"));
    }

    [Fact]
    public void Apply_skips_a_window_that_already_reset()
    {
        var store = new SensorStore(() => Clock.T0);

        ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(90, Clock.T0Unix - 1), null, null), store, Clock.T0);

        Assert.Null(Clock.Read(store, "claude.five_hour.pct"));
        Assert.Equal(Clock.T0Unix, Clock.Read(store, "claude.updated_at"));
    }

    [Fact]
    public void Apply_keeps_a_window_without_a_reset_time_indefinitely()
    {
        var now = Clock.T0;
        var store = new SensorStore(() => now);

        ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(12, null), null, null), store, Clock.T0);
        now = Clock.T0.AddDays(30);

        Assert.Equal(12.0, Clock.Read(store, "claude.five_hour.pct"));
        Assert.Null(Clock.Read(store, "claude.five_hour.resets_at"));
    }

    [Fact]
    public void Apply_survives_an_absurd_reset_time()
    {
        var store = new SensorStore(() => Clock.T0);
        var status = Parse("""{"rate_limits":{"five_hour":{"used_percentage":5,"resets_at":1e30}}}""");

        ClaudeStatusLine.Apply(status, store, Clock.T0);

        Assert.Equal(5.0, Clock.Read(store, "claude.five_hour.pct"));
    }

    [Fact]
    public void Apply_leaves_earlier_values_alone_when_a_window_is_missing()
    {
        var store = new SensorStore(() => Clock.T0);
        ClaudeStatusLine.Apply(new ClaudeStatus(new ClaudeWindow(40, null), null, 20), store, Clock.T0);

        ClaudeStatusLine.Apply(new ClaudeStatus(null, null, null), store, Clock.T0.AddMinutes(1));

        Assert.Equal(40.0, Clock.Read(store, "claude.five_hour.pct"));
        Assert.Equal(20.0, Clock.Read(store, "claude.context.pct"));
    }
}
