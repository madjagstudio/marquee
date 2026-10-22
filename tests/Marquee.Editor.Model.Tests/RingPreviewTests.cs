using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

public class RingPreviewTests
{
    [Fact]
    public void The_preview_matches_the_ring_renderer_and_its_rules_switch()
    {
        var document = LayoutDocument.Open(
            "canvas: { orientation: landscape }\n" +
            "ring: { color: \"#3A7BFF\", effect: breathe, source: cpu.temp, when: [ { above: 80, color: \"#FF0000\" } ] }\n" +
            "widgets: []\n");
        var store = new SensorStore(() => DateTimeOffset.UnixEpoch);
        store.Set("cpu.temp", 90);
        var preview = new RingPreview(document);
        var time = TimeSpan.FromSeconds(1.25);
        var colors = preview.Colors(store.Snapshot(), time);
        var expected = new SKColor[60];
        RingRenderer.Render(preview.Spec, store.Snapshot(), time, expected);
        Assert.Equal(expected, colors);
        Assert.True(colors.Max(c => c.Red) > colors.Max(c => c.Blue));
        store.Set("cpu.temp", 70);
        colors = preview.Colors(store.Snapshot(), time);
        RingRenderer.Render(preview.Spec, store.Snapshot(), time, expected);
        Assert.Equal(expected, colors);
        Assert.True(colors.Max(c => c.Blue) > colors.Max(c => c.Red));
    }

    [Fact]
    public void A_missing_ring_uses_the_runtime_default()
    {
        var preview = new RingPreview(LayoutDocument.Open("canvas: { orientation: landscape }\nwidgets: []\n"));
        Assert.Equal(RingSpec.Default, preview.Spec);
        Assert.Equal(60, preview.Colors(new SensorStore().Snapshot(), TimeSpan.Zero).Length);
    }
}
