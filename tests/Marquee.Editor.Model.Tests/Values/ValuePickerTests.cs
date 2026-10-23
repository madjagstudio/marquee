using Marquee.Core.Expressions;
using System.Globalization;
using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Tests.TestSupport;
using Marquee.Editor.Model.Values;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Tests.Values;

/// The value picker reads back what it writes, and anything else stays as written, in Advanced.
public class ValuePickerTests
{
    [Fact]
    public Task The_pickers_clock_updates_even_when_the_theme_has_no_clock() => Task.Run(async () =>
    {
        var clock = new FakeClock();
        using var sources = new ValueSources(new FakeFeed(), new FakeLocal(), clock);
        await sources.PollAsync();
        var updated = 0;
        sources.Updated += (_, _) => updated++;
        clock.Advance(TimeSpan.FromMinutes(1));
        await sources.PollAsync();
        Assert.Equal(1, updated);
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds(), sources.Rows(["now"])[0].Value!.Value.Number);
    });

    [Fact]
    public Task Picker_readings_include_unused_live_keys_and_distinguish_samples_and_stale_values() => Task.Run(async () =>
    {
        var local = new FakeLocal();
        var feed = new FakeFeed { Status = SensorFeedStatus.Ok };
        local.Store.Set("cpu.temp", 52);
        feed.Set("gpu.temp", 58);
        using var sources = new ValueSources(feed, local);
        await sources.PollAsync();
        var rows = sources.Rows(["cpu.temp", "gpu.temp", "fps"]);
        Assert.Equal(52, rows[0].Value!.Value.Number);
        Assert.Equal(ValueSourceKind.Local, rows[0].Source);
        Assert.Equal(58, rows[1].Value!.Value.Number);
        Assert.Equal(ValueSourceKind.Tray, rows[1].Source);
        Assert.Equal(ValueSourceKind.Sample, rows[2].Source);
        var updates = 0;
        sources.Updated += (_, _) => updates++;
        feed.Set("gpu.temp", 60, stale: true);
        await sources.PollAsync();
        Assert.Equal(1, updates);
        Assert.True(sources.Rows(["gpu.temp"])[0].Stale);
    });

    [Theory]
    [InlineData("cpu.temp")]
    [InlineData("clamp(cpu.load / 100, 0, 1)")]
    [InlineData("clamp((cpu.temp - 30) / 60, 0, 1)")]
    [InlineData("0.5")]
    public void What_the_picker_writes_reads_back_the_same_and_parses(string text)
    {
        var picked = ValuePicker.Read(text);
        Assert.IsNotType<PickedValue.Advanced>(picked);
        Assert.Equal(text, ValuePicker.Write(picked));
        Expression.Parse(text);
    }

    [Fact]
    public void A_ranged_pick_writes_the_short_form_from_zero_and_the_long_form_otherwise()
    {
        Assert.Equal("clamp(cpu.load / 100, 0, 1)", ValuePicker.Write(new PickedValue.Ranged("cpu.load", 0, 100, 0, 1)));
        Assert.Equal("clamp((cpu.temp - 30) / 60, 0, 1)", ValuePicker.Write(new PickedValue.Ranged("cpu.temp", 30, 90, 0, 1)));
        Assert.Equal("clamp((gpu.load - 0) / 100 * 2 + -1, -1, 1)", ValuePicker.Write(new PickedValue.Ranged("gpu.load", 0, 100, -1, 1)));
    }

    [Theory]
    [InlineData(0, 100, -1, 1)]
    [InlineData(-30, 90, 2, 8)]
    [InlineData(0.5, 2.5, -0.5, 0.5)]
    public void Ranges_read_back_all_four_endpoints(double low, double high, double toLow, double toHigh)
    {
        var picked = new PickedValue.Ranged("cpu.temp", low, high, toLow, toHigh);
        var text = ValuePicker.Write(picked);
        Assert.Equal(picked, ValuePicker.Read(text));
        Expression.Parse(text);
    }

    [Theory]
    [InlineData("clamp(max(cpu.load, gpu.load) / 100, 0, 1)")]
    [InlineData("  cpu.temp + 1 \n")]
    [InlineData("clamp((cpu.load - 0) / 100 * 3 + -1, -1, 1)")]
    [InlineData("clamp((cpu.load - 0) / 100 * 2 + 0, -1, 1)")]
    [InlineData("CPU.temp")]
    [InlineData("1e3")]
    public void A_hand_written_expression_is_advanced_and_unchanged(string text)
    {
        Assert.Equal(new PickedValue.Advanced(text), ValuePicker.Read(text));
        Assert.Equal(text, ValuePicker.Write(ValuePicker.Read(text)));
    }

    [Fact]
    public void Whitespace_is_normalized_for_known_forms() =>
        Assert.Equal(new PickedValue.Ranged("cpu.load", 0, 100, 0, 1),
            ValuePicker.Read(" \tclamp(cpu.load  /\n100, 0, 1) \r\n"));

    [Theory]
    [InlineData(0.5, "0.5")]
    [InlineData(-60, "-60")]
    [InlineData(0.0000001, "0.0000001")]
    [InlineData(1e20, "100000000000000000000")]
    public void Numbers_are_invariant_decimal_expressions(double value, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var picked = new PickedValue.Number(value);
            Assert.Equal(expected, ValuePicker.Write(picked));
            Assert.Equal(picked, ValuePicker.Read(expected));
            Expression.Parse(expected);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("cpu.temp", 30, 90)]
    [InlineData("gpu.load", 0, 100)]
    [InlineData("ram.used_pct", 0, 100)]
    [InlineData("fps", 0, 144)]
    [InlineData("fps.1pct_low", 0, 144)]
    [InlineData("frametime", 0, 50)]
    [InlineData("custom.anything", 0, 100)]
    public void Default_ranges_follow_the_key(string key, double low, double high) =>
        Assert.Equal((low, high), ValuePicker.DefaultRange(key));

    [Fact]
    public Task Keys_combine_sources_theme_and_samples_without_duplicates_or_patterns() => Task.Run(async () =>
    {
        var feed = new FakeFeed { Status = SensorFeedStatus.Ok };
        var local = new FakeLocal();
        feed.Set("custom.tray", 12);
        local.Store.Set("custom.local", 34);
        using var sources = new ValueSources(feed, local);
        sources.SetTheme(LayoutParser.Parse("canvas: { orientation: landscape }\nwidgets: [{ type: value, at: [0, 0], size: [100, 40], source: custom.source }]"));
        await sources.PollAsync();

        var keys = ValuePicker.Keys(sources, ["custom.theme", "cpu.temp", "custom.*"]);

        Assert.Contains("custom.source", keys);
        Assert.Contains("custom.theme", keys);
        Assert.Contains("cpu.temp", keys);
        Assert.Contains("cpu.load", keys);
        Assert.Contains("custom.tray", keys);
        Assert.Contains("custom.local", keys);
        Assert.Contains("fps", keys);
        Assert.DoesNotContain(keys, key => key.Contains('*'));
        Assert.Equal(keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), keys);

        var updates = 0;
        sources.Updated += (_, _) => updates++;
        feed.Set("custom.new", 56);
        await sources.PollAsync();
        Assert.Equal(1, updates);
        Assert.Contains("custom.new", ValuePicker.Keys(sources, []));
    });
}
