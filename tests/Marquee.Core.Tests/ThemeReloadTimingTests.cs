using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Xunit.Abstractions;

namespace Marquee.Core.Tests;

/// What a property change costs the theme editor: loading the example's text again through a warm cache, and drawing a
/// frame of it. The spec's target is under 50 ms for both; the times, and the first load's without a warm cache, are
/// written to the test output. That files are reused is ThemeAssetCacheTests' to check, not this test's. It runs alone,
/// with ValidatorThreadTests, both because it plays the showcase's clip and so that other tests don't skew the times.
[Collection(nameof(ValidatorThreadTests))]
public class ThemeReloadTimingTests(ITestOutputHelper output)
{
    const int Reloads = 20;

    /// The spec's target for the whole update, the reload and the first frame: about 10 ms on a desktop PC, so a busy
    /// CI runner has room to spare.
    static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(50);

    static string ThemeFolder(string name, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "themes", name));

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    [InlineData("example-agents")]
    [InlineData("example-showcase")]
    public void Reloading_an_example_from_its_text_with_a_warm_cache_is_quick(string name)
    {
        var folder = ThemeFolder(name);
        var text = File.ReadAllText(Path.Combine(folder, ThemeLoader.LayoutFileName));
        var sensors = TestSensors.Of(("cpu.temp", 64), ("cpu.load", 23), ("gpu.temp", 58), ("gpu.load", 41), ("ram.used_pct", 71));
        using var cache = new ThemeAssetCache(); // the real shader and clip measurements, as the editor makes
        var cold = Stopwatch.StartNew();
        var theme = ThemeLoader.Load(folder, text, cache);
        cold.Stop();

        var loads = new List<TimeSpan>();
        var frames = new List<TimeSpan>();
        try
        {
            for (var i = 0; i < Reloads; i++)
            {
                // A property change: new text, so a new spec, against the same files.
                var edited = text + $"\n# edit {i}\n";
                var clock = Stopwatch.StartNew();
                var next = ThemeLoader.Load(folder, edited, cache);
                loads.Add(clock.Elapsed);
                theme.Dispose();
                theme = next;

                clock.Restart();
                using (var renderer = new ThemeRenderer(theme))
                using (renderer.Render(sensors, TimeSpan.FromSeconds(i)))
                    frames.Add(clock.Elapsed);
            }
        }
        finally
        {
            theme.Dispose();
        }

        var load = Median(loads);
        var frame = Median(frames);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: reload {load.TotalMilliseconds:0.0} ms, first frame {frame.TotalMilliseconds:0.0} ms (medians of {Reloads}); " +
            $"slowest reload {loads.Max().TotalMilliseconds:0.0} ms; the first, cold load {cold.Elapsed.TotalMilliseconds:0.0} ms"));
        Assert.True(load + frame < Limit, $"reloading {name} took {load.TotalMilliseconds} ms and drawing it {frame.TotalMilliseconds} ms");
    }

    /// Dragging the showcase's video widget a pixel at a time, or its size handle: each edit loads the text again with
    /// the box moved or resized, and the clip plays on in the player it has, scaled to the new box, rather than being
    /// opened, measured and started from its first frame again.
    [Theory]
    [InlineData("at: [24, 250]", "at: [{0}, 250]", 24)]
    [InlineData("size: [252, 158]", "size: [{0}, 158]", 252)]
    public void Moving_or_resizing_a_video_widget_keeps_its_clip_playing_and_is_quick(string box, string edit, int from)
    {
        var folder = ThemeFolder("example-showcase");
        var text = File.ReadAllText(Path.Combine(folder, ThemeLoader.LayoutFileName));
        Assert.Contains(box, text);
        var sensors = TestSensors.Of(("cpu.temp", 64), ("cpu.load", 23), ("gpu.temp", 58), ("gpu.load", 41), ("ram.used_pct", 71));
        using var cache = new ThemeAssetCache();
        var theme = ThemeLoader.Load(folder, text, cache);
        var player = Radar(theme);

        var loads = new List<TimeSpan>();
        var frames = new List<TimeSpan>();
        var players = new HashSet<Video.VideoPlayer?>(ReferenceEqualityComparer.Instance);
        try
        {
            for (var i = 1; i <= Reloads; i++)
            {
                var edited = text.Replace(box, string.Format(CultureInfo.InvariantCulture, edit, from + i));
                var clock = Stopwatch.StartNew();
                var next = ThemeLoader.Load(folder, edited, cache);
                loads.Add(clock.Elapsed);
                theme.Dispose();
                theme = next;
                players.Add(Radar(theme));

                clock.Restart();
                using (var renderer = new ThemeRenderer(theme))
                using (renderer.Render(sensors, TimeSpan.FromSeconds(i)))
                    frames.Add(clock.Elapsed);
            }
        }
        finally
        {
            theme.Dispose();
        }

        var (load, frame) = (Median(loads), Median(frames));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{edit}: reload {load.TotalMilliseconds:0.0} ms, frame {frame.TotalMilliseconds:0.0} ms (medians of {Reloads}); " +
            $"slowest reload {loads.Max().TotalMilliseconds:0.0} ms"));
        Assert.NotNull(player);
        Assert.Same(player, Assert.Single(players));
        Assert.True(load + frame < Limit, $"an edit took {load.TotalMilliseconds} ms to load and {frame.TotalMilliseconds} ms to draw");
    }

    static Video.VideoPlayer? Radar(LoadedTheme theme) =>
        theme.Assets.Video(Flatten(theme.Spec.Widgets).OfType<VideoWidget>().Single());

    static IEnumerable<WidgetSpec> Flatten(IEnumerable<WidgetSpec> widgets) =>
        widgets.SelectMany(w => w is GroupWidget g ? Flatten(g.Children).Prepend(w) : [w]);

    static TimeSpan Median(List<TimeSpan> times) => times.Order().ElementAt(times.Count / 2);
}
