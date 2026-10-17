using Marquee.Core.Sensors;
using Marquee.Providers.Custom;

namespace Marquee.Providers.Custom.Tests;

public class CustomValuesTests
{
    [Theory]
    [InlineData("30", 30)]
    [InlineData("30s", 30)]
    [InlineData("1.5m", 90)]
    [InlineData("2h", 7200)]
    [InlineData("1D", 86400)]
    [InlineData(" 5m ", 300)]
    public void Durations(string text, double seconds)
    {
        Assert.True(DurationText.TryParse(text, out var duration));
        Assert.Equal(TimeSpan.FromSeconds(seconds), duration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5s")]
    [InlineData("5w")]
    [InlineData("s")]
    [InlineData("1e3")]
    [InlineData("366d")]
    [InlineData(null)]
    public void Invalid_durations(string? text) => Assert.False(DurationText.TryParse(text, out _));

    [Theory]
    [InlineData("custom.build", true)]
    [InlineData("custom.queue.depth", true)]
    [InlineData("custom.gpu_2.fan", true)]
    [InlineData("custom.1st", true)]
    [InlineData("custom", false)]
    [InlineData("custom.", false)]
    [InlineData("cpu.temp", false)]
    [InlineData("custom.a-b", false)]
    [InlineData("custom..a", false)]
    [InlineData("Custom.a", false)]
    public void Keys(string key, bool valid) => Assert.Equal(valid, CustomValues.IsValidKey(key));

    [Fact]
    public void Long_keys_are_refused() => Assert.False(CustomValues.IsValidKey("custom." + new string('a', 58)));

    [Fact]
    public void Parses_numbers_text_and_ttl()
    {
        var update = CustomValues.Parse("""{"custom.build":"ok","custom.queue":12.5,"ttl":"30s"}""");

        Assert.Equal([new("custom.build", SensorValue.Of("ok")), new("custom.queue", SensorValue.Of(12.5))], update.Values);
        Assert.Equal(TimeSpan.FromSeconds(30), update.Ttl);
    }

    [Fact]
    public void Ttl_may_be_seconds_and_is_optional()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), CustomValues.Parse("""{"custom.a":1,"ttl":45}""").Ttl);
        Assert.Null(CustomValues.Parse("""{"custom.a":1}""").Ttl);
    }

    [Theory]
    [InlineData("nope", "The body is not valid JSON")]
    [InlineData("[1]", "The body must be a JSON object")]
    [InlineData("{}", "The body has no values to set.")]
    [InlineData("""{"ttl":30}""", "The body has no values to set.")]
    [InlineData("""{"cpu.temp":99}""", "'cpu.temp' is not a custom key.")]
    [InlineData("""{"custom.a":true}""", "custom.a: values must be numbers or text, not true.")]
    [InlineData("""{"custom.a":null}""", "custom.a: values must be numbers or text, not null.")]
    [InlineData("""{"custom.a":{"b":1}}""", "custom.a: values must be numbers or text, not object.")]
    [InlineData("""{"custom.a":1,"ttl":"soon"}""", "ttl must be a positive number of seconds")]
    [InlineData("""{"custom.a":1,"ttl":0}""", "ttl must be a positive number of seconds")]
    public void Refuses_bad_updates(string json, string message) =>
        Assert.StartsWith(message, Assert.Throws<CustomValuesException>(() => CustomValues.Parse(json)).Message);

    [Fact]
    public void Refuses_long_text() =>
        Assert.Throws<CustomValuesException>(() => CustomValues.Parse($"{{\"custom.a\":\"{new string('x', 257)}\"}}"));

    [Fact]
    public void Refuses_too_many_values() =>
        Assert.Throws<CustomValuesException>(() =>
            CustomValues.Parse("{" + string.Join(",", Enumerable.Range(0, 101).Select(i => $"\"custom.k{i}\":{i}")) + "}"));

    [Fact]
    public void Apply_writes_with_the_ttl()
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var store = new SensorStore(() => now);

        CustomValues.Parse("""{"custom.build":"ok","custom.queue":3,"ttl":30}""").Apply(store);

        Assert.True(store.Snapshot().TryGet("custom.build", out var build));
        Assert.Equal("ok", build.Text);
        now = now.AddSeconds(30);
        Assert.False(store.Snapshot().TryGet("custom.queue", out _));
    }

    [Theory]
    [InlineData("12", null, """{"custom.a":12}""")]
    [InlineData("-3.5", 30, """{"custom.a":-3.5,"ttl":30}""")]
    [InlineData("ok", null, """{"custom.a":"ok"}""")]
    [InlineData("1,5", null, """{"custom.a":"1,5"}""")]
    [InlineData("build 42 ok", null, """{"custom.a":"build 42 ok"}""")]
    public void Push_body(string value, int? ttl, string expected) =>
        Assert.Equal(expected, CustomValuesClient.Body("custom.a", value, ttl is { } t ? TimeSpan.FromSeconds(t) : null));
}
