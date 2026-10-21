using System.Text;
using Marquee.Core.Config;
using Marquee.Core.Sensors;
using Marquee.Host.SensorFeed;
using Marquee.Providers.Custom;

namespace Marquee.Host.Tests;

public class SensorFeedContentTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    static string Text(byte[] json) => Encoding.UTF8.GetString(json);

    [Fact]
    public void The_caps_are_1_MB_for_values_and_8_MB_for_history()
    {
        Assert.Equal((1024 * 1024, 8 * 1024 * 1024), (SensorFeedFormat.MaxValuesBytes, SensorFeedFormat.MaxHistoryBytes));
        Assert.Equal((SensorFeedFormat.MaxValuesBytes, SensorFeedFormat.MaxHistoryBytes, 5),
            (SensorFeedLimits.Default.MaxValuesBytes, SensorFeedLimits.Default.MaxHistoryBytes, SensorFeedLimits.Default.ConnectionsPerSecond));
    }

    [Fact]
    public void A_values_message_is_versioned_json_in_key_order()
    {
        var now = T0;
        var store = new SensorStore(() => now);
        store.Set("gpu.temp", 61, TimeSpan.FromSeconds(5));
        store.Set("cpu.name", "Ryzen 9");
        store.Set("cpu.temp", 52.5);
        now = T0.AddSeconds(10);

        var json = SensorFeedContent.Values(store.Snapshot(), SensorFeedFormat.MaxValuesBytes);

        Assert.Equal(
            """{"version":1,"now":"2026-10-20T12:00:10+00:00","truncated":false,"values":{"cpu.name":{"text":"Ryzen 9","stale":false},"cpu.temp":{"number":52.5,"stale":false},"gpu.temp":{"number":61,"stale":true}}}""",
            Text(json));
    }

    [Fact]
    public void A_history_message_is_versioned_json_in_key_order()
    {
        var store = new SensorStore(() => T0);
        store.Set("gpu.load", 7);
        store.Set("cpu.temp", 50);
        store.Set("cpu.temp", 50.5);
        store.Set("cpu.name", "Ryzen 9");

        var json = SensorFeedContent.History(store.Snapshot(), SensorFeedFormat.MaxHistoryBytes);

        Assert.Equal(
            """{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"history":{"cpu.temp":[50,50.5],"gpu.load":[7]}}""",
            Text(json));
    }

    [Fact]
    public void A_frame_is_the_json_after_its_length_as_four_little_endian_bytes()
    {
        var frame = SensorFeedFormat.Frame("{}"u8.ToArray());

        Assert.Equal([2, 0, 0, 0, (byte)'{', (byte)'}'], frame);
    }

    [Fact]
    public void Values_over_the_cap_drop_whole_keys_that_dont_fit_in_key_order()
    {
        var store = new SensorStore(() => T0);
        store.Set("a.small", 1);
        store.Set("b.large", new string('x', 400));
        store.Set("c.small", 2);
        store.Set("d.large", new string('y', 400));
        store.Set("e.small", 3);
        var snapshot = store.Snapshot();
        var full = SensorFeedContent.Values(snapshot, int.MaxValue).Length;

        var json = SensorFeedContent.Values(snapshot, full - 300);

        Assert.True(json.Length <= full - 300, $"{json.Length} bytes");
        var message = SensorFeedJson.ReadValues(json);
        Assert.True(message.Truncated);
        // b.large fits once the small keys before it are in; d.large then doesn't, and e.small still does.
        Assert.Equal(["a.small", "b.large", "c.small", "e.small"], message.Values.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(json, SensorFeedContent.Values(snapshot, full - 300));
    }

    /// The most custom values `marquee push` keeps by default, 1000 keys of 256 characters, fit whole in any script of
    /// Unicode's basic plane: text is written as UTF-8, not escaped to \uXXXX six bytes a character. (Characters past
    /// it, such as emoji, are still escaped, and a store full of them is cut down like any other over the cap.)
    [Theory]
    [InlineData("x")]
    [InlineData("温")]
    [InlineData("é")]
    public void The_most_custom_values_fit_under_the_cap_in_any_script(string character)
    {
        var text = string.Concat(Enumerable.Repeat(character, CustomValues.MaxTextLength / character.Length));
        var store = new SensorStore(() => T0);
        for (var i = 0; i < MarqueeConfig.DefaultMaxCustomKeys; i++) store.Set($"custom.text{i:D4}", text);

        var json = SensorFeedContent.Values(store.Snapshot(), SensorFeedFormat.MaxValuesBytes);

        var message = SensorFeedJson.ReadValues(json);
        Assert.False(message.Truncated, $"{json.Length} bytes");
        Assert.Equal(MarqueeConfig.DefaultMaxCustomKeys, message.Values.Count);
        Assert.Equal(text, message.Values["custom.text0999"].Text);
    }

    [Fact]
    public void Values_at_the_real_cap_stay_under_it()
    {
        var store = new SensorStore(() => T0);
        for (var i = 0; i < 5000; i++) store.Set($"custom.text{i:D4}", new string('x', 256));

        var json = SensorFeedContent.Values(store.Snapshot(), SensorFeedFormat.MaxValuesBytes);

        Assert.InRange(json.Length, SensorFeedFormat.MaxValuesBytes - 400, SensorFeedFormat.MaxValuesBytes);
        var message = SensorFeedJson.ReadValues(json);
        Assert.True(message.Truncated);
        Assert.Equal("custom.text0000", message.Values.Keys.Order(StringComparer.Ordinal).First());
    }

    [Fact]
    public void History_over_the_cap_loses_its_oldest_points_first()
    {
        var store = new SensorStore(() => T0);
        for (var i = 1; i <= 10; i++) store.Set("cpu.temp", i);
        for (var i = 1; i <= 4; i++) store.Set("gpu.temp", 100 + i);
        var snapshot = store.Snapshot();
        var cap = SensorFeedContent.History(snapshot, int.MaxValue).Length - 8;

        var message = SensorFeedJson.ReadHistory(SensorFeedContent.History(snapshot, cap));

        // Leaving out cpu.temp's four oldest points (two bytes each with its comma) is the least that fits. Every key
        // keeps the same number of newest points, and gpu.temp has fewer than that, so it keeps them all.
        Assert.True(message.Truncated);
        Assert.Equal([5.0, 6, 7, 8, 9, 10], message.History["cpu.temp"]);
        Assert.Equal([101.0, 102, 103, 104], message.History["gpu.temp"]);
    }

    [Fact]
    public void History_whose_keys_alone_dont_fit_is_sent_empty()
    {
        var envelope = SensorFeedContent.History(new SensorStore(() => T0).Snapshot(), int.MaxValue).Length;
        var store = new SensorStore(() => T0);
        foreach (var key in new[] { "custom.a", "custom.b", "custom.c" }) store.Set(key, 1);

        var message = SensorFeedJson.ReadHistory(SensorFeedContent.History(store.Snapshot(), envelope + 20));

        Assert.True(message.Truncated);
        Assert.Empty(message.History);
    }

    [Fact]
    public void History_at_the_real_cap_stays_under_it()
    {
        var store = new SensorStore(() => T0);
        for (var key = 0; key < 1000; key++)
            for (var point = 0; point < SensorStore.DefaultHistoryLength; point++)
                store.Set($"custom.k{key:D4}", 1234.567891234 + point);

        var json = SensorFeedContent.History(store.Snapshot(), SensorFeedFormat.MaxHistoryBytes);

        Assert.True(json.Length <= SensorFeedFormat.MaxHistoryBytes, $"{json.Length} bytes");
        var message = SensorFeedJson.ReadHistory(json);
        Assert.True(message.Truncated);
        Assert.Equal(1000, message.History.Count);
        var kept = message.History["custom.k0000"];
        Assert.InRange(kept.Count, 1, SensorStore.DefaultHistoryLength - 1);
        Assert.Equal(1234.567891234 + (SensorStore.DefaultHistoryLength - 1), kept[^1]);
    }

    [Fact]
    public void Messages_round_trip()
    {
        var values = new SensorFeedValues(SensorFeedFormat.Version, T0, new Dictionary<string, FeedValue>
        {
            ["cpu.temp"] = new(52.123456789, null, false),
            ["cpu.name"] = new(null, "Ryzen ✓", true),
        });
        var history = new SensorFeedHistory(SensorFeedFormat.Version, T0, new Dictionary<string, IReadOnlyList<double>>
        {
            ["cpu.temp"] = [1e-7, -3, 1.5e300],
        }, Truncated: true);

        var readValues = SensorFeedJson.ReadValues(SensorFeedJson.Write(values));
        var readHistory = SensorFeedJson.ReadHistory(SensorFeedJson.Write(history));

        Assert.Equal((values.Version, values.Now, values.Truncated), (readValues.Version, readValues.Now, readValues.Truncated));
        Assert.Equal(values.Values.OrderBy(v => v.Key), readValues.Values.OrderBy(v => v.Key));
        Assert.Equal((history.Version, history.Now, history.Truncated), (readHistory.Version, readHistory.Now, readHistory.Truncated));
        Assert.Equal(history.History["cpu.temp"], readHistory.History["cpu.temp"]);
    }

    [Theory]
    [InlineData("""{"version":2,"now":"2026-10-20T12:00:00+00:00","truncated":false,"values":{}}""")]
    [InlineData("""{"now":"2026-10-20T12:00:00+00:00","truncated":false,"values":{}}""")]
    [InlineData("""{"version":1,"now":"noon","truncated":false,"values":{}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"values":{"a":{"stale":false}}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"values":{"a":{"number":1,"text":"x","stale":false}}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"values":{"a":{"number":"1","stale":false}}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"values":{"a":{"text":null,"stale":false}}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"values":[]}""")]
    [InlineData("""{"version":1""")]
    [InlineData("""[]""")]
    public void A_values_message_of_another_version_or_shape_is_refused(string json) =>
        Assert.Throws<FormatException>(() => SensorFeedJson.ReadValues(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData("""{"version":2,"now":"2026-10-20T12:00:00+00:00","truncated":false,"history":{}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"history":{"a":[1,"2"]}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","truncated":false,"history":{"a":1}}""")]
    [InlineData("""{"version":1,"now":"2026-10-20T12:00:00+00:00","history":{}}""")]
    public void A_history_message_of_another_version_or_shape_is_refused(string json) =>
        Assert.Throws<FormatException>(() => SensorFeedJson.ReadHistory(Encoding.UTF8.GetBytes(json)));
}
