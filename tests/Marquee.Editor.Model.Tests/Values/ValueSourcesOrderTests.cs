using Marquee.Core.Sensors;
using Marquee.Core.Themes.Parsing;
using Marquee.Editor.Model.Tests.TestSupport;
using Marquee.Editor.Model.Values;
using Marquee.Host.SensorFeed;

namespace Marquee.Editor.Model.Tests.Values;

/// Which value a key shows: a pin, then the tray's feed, then the editor's own readings, then a sample.
public class ValueSourcesOrderTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: gauge, at: [0, 0], size: [100, 100], source: cpu.temp, min: 20, max: 100 }\n" +
        "  - { type: value, at: [0, 100], size: [100, 40], source: gpu.temp }\n" +
        "  - { type: value, at: [0, 140], size: [100, 40], source: ram.used_pct }\n" +
        "  - { type: graph, at: [0, 200], size: [200, 100], source: cpu.load, points: 30 }\n" +
        "  - { type: text, at: [0, 300], size: [200, 40], text: \"{custom.status}\" }\n";

    readonly FakeFeed _feed = new();
    readonly FakeLocal _local = new();
    readonly ManualTime _time = new();

    ValueSources Sources()
    {
        var sources = new ValueSources(_feed, _local, _time);
        sources.SetTheme(LayoutParser.Parse(Layout));
        return sources;
    }

    static double? Number(SensorSnapshot snapshot, string key) => snapshot.TryGet(key, out var value) ? value.Number : null;

    [Fact]
    public async Task Each_key_takes_the_highest_source_that_has_it()
    {
        using var sources = Sources();
        _feed.Status = SensorFeedStatus.Ok;
        _feed.Set("cpu.temp", 61);
        _local.Store.Set("cpu.temp", 40);
        _local.Store.Set("gpu.temp", 44);
        sources.TestValues.Pin("ram.used_pct", TestPin.Of(77));
        _feed.Set("ram.used_pct", 20);
        _local.Store.Set("ram.used_pct", 21);

        await sources.PollAsync();
        var snapshot = sources.Snapshot();

        Assert.Equal(77, Number(snapshot, "ram.used_pct"));
        Assert.Equal(61, Number(snapshot, "cpu.temp"));
        Assert.Equal(44, Number(snapshot, "gpu.temp"));
        Assert.Equal(30, Number(snapshot, "cpu.load"));
        Assert.Equal(ValueSourceKind.Pinned, sources.SourceOf("ram.used_pct"));
        Assert.Equal(ValueSourceKind.Tray, sources.SourceOf("cpu.temp"));
        Assert.Equal(ValueSourceKind.Local, sources.SourceOf("gpu.temp"));
        Assert.Equal(ValueSourceKind.Sample, sources.SourceOf("cpu.load"));
    }

    [Fact]
    public void Before_any_reading_every_key_is_a_sample()
    {
        using var sources = Sources();

        var snapshot = sources.Snapshot();

        Assert.Equal(52, Number(snapshot, "cpu.temp"));
        Assert.Equal(50, Number(snapshot, "custom.status"));
        Assert.All(sources.Rows(), row => Assert.Equal(row.Key == "now" ? ValueSourceKind.Local : ValueSourceKind.Sample, row.Source));
    }

    [Fact]
    public async Task A_stale_tray_value_shows_stale_rather_than_falling_through()
    {
        using var sources = Sources();
        _feed.Status = SensorFeedStatus.Ok;
        _feed.Set("gpu.temp", 70, stale: true);
        _local.Store.Set("gpu.temp", 44);

        await sources.PollAsync();
        var snapshot = sources.Snapshot();

        Assert.False(snapshot.TryGet("gpu.temp", out _));
        Assert.True(snapshot.TryGetLast("gpu.temp", out var last, out var stale));
        Assert.Equal((70.0, true), (last.Number, stale));
        Assert.Equal(ValueSourceKind.Tray, sources.SourceOf("gpu.temp"));
    }

    [Fact]
    public async Task Text_values_come_through()
    {
        using var sources = Sources();
        _feed.Status = SensorFeedStatus.Ok;
        _feed.Set("custom.status", "deploying");

        await sources.PollAsync();

        Assert.True(sources.Snapshot().TryGet("custom.status", out var value));
        Assert.Equal("deploying", value.Text);
    }

    [Fact]
    public void A_number_pin_shows_its_number_and_its_pattern_as_history()
    {
        using var sources = Sources();

        sources.TestValues.Pin("cpu.load", TestPin.Of(90, HistoryPattern.Ramp));
        var snapshot = sources.Snapshot();

        Assert.Equal(90, Number(snapshot, "cpu.load"));
        var history = snapshot.History("cpu.load");
        Assert.Equal(30, history.Count);
        Assert.Equal((0.0, 90.0), (history[0], history[^1]));
    }

    [Fact]
    public async Task A_stale_pin_keeps_the_value_underneath_and_marks_it_stale()
    {
        using var sources = Sources();
        _feed.Status = SensorFeedStatus.Ok;
        _feed.Set("cpu.temp", 61);
        await sources.PollAsync();

        sources.TestValues.Pin("cpu.temp", TestPin.Stale);
        var snapshot = sources.Snapshot();

        Assert.False(snapshot.TryGet("cpu.temp", out _));
        Assert.True(snapshot.TryGetLast("cpu.temp", out var last, out var stale));
        Assert.Equal((61.0, true), (last.Number, stale));
        Assert.Equal(ValueSourceKind.Pinned, sources.SourceOf("cpu.temp"));
    }

    [Fact]
    public void A_missing_pin_takes_the_key_away()
    {
        using var sources = Sources();

        sources.TestValues.Pin("cpu.temp", TestPin.Missing);
        var snapshot = sources.Snapshot();

        Assert.False(snapshot.TryGetLast("cpu.temp", out _, out _));
        Assert.DoesNotContain("cpu.temp", snapshot.Keys);
        var row = Assert.Single(sources.Rows(), row => row.Key == "cpu.temp");
        Assert.Equal((ValueSourceKind.Pinned, (SensorValue?)null), (row.Source, row.Value));
    }

    [Fact]
    public void Pinning_now_sets_the_snapshots_clock()
    {
        using var sources = Sources();
        var at = new DateTimeOffset(2026, 12, 31, 23, 59, 30, TimeSpan.FromHours(-5));

        Assert.Equal(_time.GetUtcNow(), sources.Snapshot().Now);
        sources.TestValues.PinNow(at);

        Assert.Equal(at, sources.Snapshot().Now);
        Assert.Equal(ValueSourceKind.Pinned, sources.SourceOf("now"));
        sources.TestValues.Unpin("now");
        Assert.Equal(_time.GetUtcNow(), sources.Snapshot().Now);
        Assert.Equal(ValueSourceKind.Local, sources.SourceOf("now"));
    }

    [Fact]
    public void A_sampled_graph_key_has_a_gentle_history()
    {
        using var sources = Sources();

        var history = sources.Snapshot().History("cpu.load");

        Assert.Equal(30, history.Count);
        Assert.Equal(30, history[^1], 6);
        Assert.True(history.Distinct().Count() > 5);
    }

    [Fact]
    public async Task Local_history_comes_through_for_a_graph()
    {
        using var sources = Sources();
        _local.Store.Set("cpu.load", 10);
        _local.Store.Set("cpu.load", 20);

        await sources.PollAsync();

        Assert.Equal([10.0, 20.0], sources.Snapshot().History("cpu.load"));
    }

    [Fact]
    public void The_rows_list_each_key_with_its_source_value_pin_and_range()
    {
        using var sources = Sources();
        sources.TestValues.Pin("cpu.temp", TestPin.Of(88));

        var rows = sources.Rows();

        Assert.Equal(["cpu.temp", "gpu.temp", "ram.used_pct", "cpu.load", "custom.status", "now"], rows.Select(row => row.Key));
        var temp = rows[0];
        Assert.Equal((ValueSourceKind.Pinned, 88.0, false), (temp.Source, temp.Value?.Number, temp.Stale));
        Assert.Equal(TestPin.Of(88), temp.Pin);
        Assert.Equal(new SliderRange(20, 100), temp.Range);
        Assert.True(rows[3].IsGraph);
        Assert.Equal(_time.GetUtcNow().ToUnixTimeSeconds(), rows[^1].Value?.Number);
    }

    [Fact]
    public void Pins_are_never_written_to_the_theme()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        using var sources = new ValueSources(_feed, _local, _time);
        sources.Attach(session);
        var before = folder.Text;

        sources.TestValues.Pin("cpu.temp", TestPin.Of(99));
        sources.TestValues.PinNow(DateTimeOffset.UnixEpoch);

        Assert.Equal(before, folder.Text);
        Assert.Equal(before, session.Document.Text);
        Assert.Single(Directory.EnumerateFiles(folder.Folder));
    }

    [Fact]
    public void Attached_to_a_session_the_frames_read_the_value_sources_and_the_list_follows_the_theme()
    {
        using var folder = TempTheme.With(Layout);
        using var session = folder.Open();
        using var sources = new ValueSources(_feed, _local, _time);

        sources.Attach(session);
        sources.TestValues.Pin("cpu.temp", TestPin.Of(99));

        Assert.Equal(99, Number(session.Values(), "cpu.temp"));
        Assert.Equal(["cpu.temp", "gpu.temp", "ram.used_pct", "cpu.load", "custom.status", "now"], sources.TestValues.Keys);

        session.Apply(new Marquee.Editor.Model.SetValue(Marquee.Core.Themes.Editing.YamlPath.Parse("widgets[1].source"), "fps"));

        Assert.Contains("fps", sources.TestValues.Keys);
        Assert.DoesNotContain("gpu.temp", sources.TestValues.Keys);
    }
}
