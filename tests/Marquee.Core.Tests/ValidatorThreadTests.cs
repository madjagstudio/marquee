using Marquee.Core.Tests.TestSupport;
using SkiaSharp;
using static Marquee.Core.Tests.TestSupport.Validations;

namespace Marquee.Core.Tests;

/// Counting decoder threads only means something while no other test starts or stops players, so these run alone.
[CollectionDefinition(nameof(ValidatorThreadTests), DisableParallelization = true)]
public sealed class ValidatorThreadCollection;

/// Loading a video layer starts its player's decode thread. `marquee validate` loads the theme the way `marquee run`
/// does, and must stop every one before it returns, or the gallery would leave a thread running per clip it checked.
[Collection(nameof(ValidatorThreadTests))]
public class ValidatorThreadTests
{
    const string DecoderThread = "Marquee video decoder";

    [Fact]
    public void Validating_a_video_theme_leaves_no_decoder_thread_running()
    {
        using var folder = new TempTheme("canvas: { orientation: landscape }\nbackground:\n  - video: a.gif\n  - video: b.gif\n");
        var gif = GifWriter.Write(16, 4, [SKColors.Red, SKColors.Lime, SKColors.Blue],
            GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10), GifFrame.Solid(SKRectI.Create(16, 4), 2, 10));
        folder.AddFile("a.gif", gif);
        folder.AddFile("b.gif", gif);
        var before = NamedThreads.Count(DecoderThread);

        // The count sees a loaded theme's players, so it would see any that validating left behind.
        using (folder.Load())
            Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 2), "the players' threads weren't seen");
        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before));

        var report = Validate(folder.Folder);

        Assert.Empty(report.Issues);
        Assert.Equal(before, NamedThreads.Count(DecoderThread));
    }
}
