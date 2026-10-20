using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A video widget's player decodes on a thread of its own, as a video layer's does, and must stop with the theme: when
/// a save reloads it, and when the render loop that shows it stops. Counting decoder threads only means something while
/// no other test starts or stops players, so these run alone, with ValidatorThreadTests.
[Collection(nameof(ValidatorThreadTests))]
public class VideoWidgetThreadTests
{
    const string DecoderThread = "Marquee video decoder";

    [Fact]
    public void Reloading_a_theme_stops_the_players_of_the_one_it_replaces()
    {
        using var folder = new TempTheme("""
            canvas: { orientation: landscape }
            background:
              - video: a.gif
            widgets:
              - { type: video, at: [100, 100], size: [320, 80], src: b.gif }
              - type: group
                at: [600, 100]
                size: [400, 300]
                children:
                  - { type: video, at: [0, 0], size: [320, 80], src: b.gif }
            """);
        var gif = GifWriter.Write(16, 4, [SKColors.Red, SKColors.Lime],
            GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10));
        folder.AddFile("a.gif", gif);
        folder.AddFile("b.gif", gif);
        var before = NamedThreads.Count(DecoderThread);

        using (var device = new RecordingDevice())
        using (var loop = new RenderLoop(device, TestSensors.Store(), new ListLog()))
        using (var watcher = new ThemeWatcher(folder.Folder, loop.SetTheme, ex => Assert.Fail(ex.Message)))
        {
            Assert.True(watcher.Reload());
            loop.Step(TimeSpan.Zero);
            Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 3), "the players' threads weren't seen");

            // What a save does: the new theme's players start as it loads, and the old one's stop once it is replaced.
            Assert.True(watcher.Reload());
            Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 6), "the new theme's players weren't seen");
            loop.Step(TimeSpan.FromMilliseconds(100));
            Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 3), "the replaced theme's players are still running");
        }

        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before), "the shown theme's players are still running");
    }
}
