using Marquee.Editor.Model.Values;
using Marquee.Editor.Resources;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

/// The test values panel: each key the theme reads with its source and value, and pins that change what the canvas
/// draws with (ValueSources.Snapshot), with no tray and no sensors, so every key starts as a sample.
public class TestValuesPanelTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "widgets:\n" +
        "  - { type: gauge, at: [0, 0], size: [200, 200], source: gpu.load, min: 0, max: 200 }\n" +
        "  - { type: graph, at: [300, 0], size: [400, 100], source: cpu.load, points: 20 }\n" +
        "  - { type: text, at: [0, 300], size: [300, 40], text: \"{now|time:HH:mm}\" }\n";

    [Fact]
    public void Rows_show_each_key_its_source_and_its_value() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("values", Layout));
        var panel = editor.Model.TestValues;

        Assert.Equal(["gpu.load", "cpu.load", "now"], panel.Rows.Select(row => row.Key));
        var gpu = panel.Row("gpu.load")!;
        Assert.Equal(Strings.SourceSample, gpu.SourceText);
        Assert.Equal("30", gpu.ValueText);
        Assert.Equal((0, 200), (gpu.Min, gpu.Max));
        Assert.False(gpu.IsGraph);
        Assert.True(panel.Row("cpu.load")!.IsGraph);
        Assert.True(panel.Row("now")!.IsNow);
        Assert.True(Sta.PumpUntil(() => panel.TrayNotRunning));
    });

    [Fact]
    public void Pins_change_the_snapshot_the_canvas_draws_with() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("values", Layout));
        var panel = editor.Model.TestValues;
        var values = editor.Workspace.Values;
        var gpu = panel.Row("gpu.load")!;

        gpu.Number = 150;
        Assert.Equal(150, values.Snapshot().TryGet("gpu.load", out var pinned) ? pinned.Number : null);
        Assert.Equal(Strings.SourcePinned, panel.Row("gpu.load")!.SourceText);
        Assert.True(panel.UnpinAllCommand.CanExecute(null));

        gpu.NumberText = "175.5";
        Assert.True(gpu.CommitNumber());
        Assert.Equal(175.5, values.Snapshot().TryGet("gpu.load", out var typed) ? typed.Number : null);
        gpu.NumberText = "lots";
        Assert.False(gpu.CommitNumber());
        Assert.Equal(Strings.NotANumber("lots"), gpu.Error);

        gpu.StaleCommand.Execute(null);
        Assert.Equal(Strings.ValueStale("30"), panel.Row("gpu.load")!.ValueText);
        gpu.MissingCommand.Execute(null);
        Assert.False(values.Snapshot().TryGet("gpu.load", out _));
        Assert.Equal(Strings.ValueMissing, panel.Row("gpu.load")!.ValueText);

        gpu.UnpinCommand.Execute(null);
        Assert.Equal(Strings.SourceSample, panel.Row("gpu.load")!.SourceText);
        Assert.False(panel.UnpinAllCommand.CanExecute(null));
    });

    [Fact]
    public void A_graph_key_takes_a_pattern_and_now_takes_a_date_and_time() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("values", Layout));
        var panel = editor.Model.TestValues;
        var values = editor.Workspace.Values;

        var cpu = panel.Row("cpu.load")!;
        cpu.Number = 80;
        cpu.Pattern = HistoryPattern.Ramp;
        Assert.Equal(TestPin.Of(80, HistoryPattern.Ramp), values.TestValues.PinOf("cpu.load"));
        var history = values.Snapshot().History("cpu.load");
        Assert.Equal(20, history.Count);
        Assert.Equal(0, history[0]);
        Assert.Equal(80, history[^1]);

        var now = panel.Row("now")!;
        now.Date = new DateTime(2026, 1, 2);
        now.Time = "21:45:00";
        now.PinNowCommand.Execute(null);
        Assert.Equal(new DateTime(2026, 1, 2, 21, 45, 0), values.TestValues.PinnedNow!.Value.LocalDateTime);
        Assert.Equal(Strings.SourcePinned, panel.Row("now")!.SourceText);
        now.Time = "25:00";
        now.PinNowCommand.Execute(null);
        Assert.Equal(Strings.NotATime("25:00"), now.Error);

        panel.UnpinAllCommand.Execute(null);
        Assert.Null(values.TestValues.PinnedNow);
        Assert.Empty(values.TestValues.Pins);
    });
}
