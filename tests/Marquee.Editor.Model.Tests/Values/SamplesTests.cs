using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Tests.TestSupport;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Model.Tests.Values;

/// The believable values a key shows when nothing reads it: by key, then by pattern, then a plain 50.
public class SamplesTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    static double Number(string key) => Samples.ValueFor(key, Now).Number!.Value;

    [Theory]
    [InlineData("cpu.temp", 52)]
    [InlineData("gpu.temp", 58)]
    [InlineData("cpu.load", 30)]
    [InlineData("gpu.load", 30)]
    [InlineData("ram.used_pct", 45)]
    [InlineData("fps", 120)]
    [InlineData("fps.1pct_low", 96)]
    [InlineData("frametime", 8.3)]
    [InlineData("cpu.clock", 4500)]
    [InlineData("gpu.clock", 2100)]
    [InlineData("cpu.power", 65)]
    [InlineData("gpu.power", 180)]
    [InlineData("cpu.voltage", 1.2)]
    [InlineData("gpu.voltage", 0.95)]
    [InlineData("gpu.fan", 1400)]
    [InlineData("fan.cpu_fan", 1100)]
    [InlineData("ram.used", 16.0 * 1024 * 1024 * 1024)]
    [InlineData("gpu.vram_used", 6.0 * 1024 * 1024 * 1024)]
    [InlineData("claude.five_hour.pct", 42)]
    [InlineData("claude.seven_day.pct", 18)]
    [InlineData("claude.seven_day_model.pct", 25)]
    [InlineData("claude.context.pct", 35)]
    [InlineData("claude.sessions.total", 3)]
    [InlineData("claude.sessions.busy", 1)]
    [InlineData("claude.sessions.idle", 2)]
    [InlineData("codex.primary.pct", 30)]
    [InlineData("codex.secondary.pct", 12)]
    [InlineData("codex.primary.window_min", 300)]
    [InlineData("codex.secondary.window_min", 10080)]
    [InlineData("codex.sessions.total", 2)]
    [InlineData("custom.anything", 50)]
    public void Known_keys_have_believable_numbers(string key, double expected)
    {
        Assert.Equal(expected, Number(key), 6);
        Assert.True(Samples.Knows(key));
    }

    [Fact]
    public void Times_are_unix_seconds_around_now()
    {
        var seconds = Now.ToUnixTimeSeconds();

        Assert.Equal(seconds + (2 * 60 + 14) * 60, Number("claude.five_hour.resets_at"));
        Assert.True(Number("claude.seven_day.resets_at") > seconds + 86_400);
        Assert.True(Number("codex.primary.resets_at") > seconds);
        Assert.True(Number("claude.updated_at") < seconds);
        Assert.Equal(seconds, Number("now"));
    }

    [Fact]
    public void Text_keys_have_text()
    {
        Assert.Equal("Opus", Samples.ValueFor("claude.seven_day_model.name", Now).Text);
        Assert.Equal("game", Samples.ValueFor("fps.app", Now).Text);
    }

    [Fact]
    public void An_unknown_key_still_has_a_sample()
    {
        Assert.False(Samples.Knows("weather.wind"));
        Assert.Equal(50, Number("weather.wind"));
    }

    [Fact]
    public void A_graph_sample_is_a_gentle_line_ending_at_the_value()
    {
        var history = Samples.HistoryFor("cpu.temp", Now, 60);

        Assert.Equal(60, history.Count);
        Assert.Equal(52, history[^1], 6);
        Assert.True(history.Distinct().Count() > 10, "the line moves");
        Assert.All(history, point => Assert.InRange(point, 52 * 0.85, 52 * 1.15));
        Assert.Equal(history, Samples.HistoryFor("cpu.temp", Now, 60));
    }

    [Fact]
    public void A_text_sample_has_no_history()
    {
        Assert.Empty(Samples.HistoryFor("fps.app", Now, 60));
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Every_key_the_examples_read_is_in_the_table(string example)
    {
        var spec = LayoutParser.Parse(File.ReadAllText(Path.Combine(TempTheme.ExampleFolder(example), ThemeLoader.LayoutFileName)));

        var unknown = ThemeKeys.Of(spec).Where(key => !Samples.Knows(key)).ToList();

        Assert.Empty(unknown);
    }

    public static TheoryData<string> ExampleNames() => new(TempTheme.Examples);
}
