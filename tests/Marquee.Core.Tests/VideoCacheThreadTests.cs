using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Through an asset cache, a clip that hasn't changed keeps playing in the same player from one load to the next, so
/// the editor doesn't reopen and measure it on every keystroke, and each player's thread stops once no load uses it.
/// Counting decoder threads only means something while no other test starts or stops players, so these run alone,
/// with ValidatorThreadTests.
[Collection(nameof(ValidatorThreadTests))]
public class VideoCacheThreadTests
{
    const string DecoderThread = "Marquee video decoder";

    const string Layout = """
        canvas: { orientation: landscape }
        widgets:
          - { type: video, at: [100, 100], size: [320, 80], src: clip.gif }
        """;

    static byte[] Gif(SKColor second) => GifWriter.Write(16, 4, [SKColors.Red, second],
        GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10));

    static Video.VideoPlayer? Player(LoadedTheme theme, int index = 0) =>
        theme.Assets.Video((VideoWidget)theme.Spec.Widgets[index]);

    [Fact]
    public void An_unchanged_clip_keeps_its_player_and_a_changed_one_gets_a_new_one()
    {
        using var folder = new TempTheme(Layout);
        folder.AddFile("clip.gif", Gif(SKColors.Lime));
        var before = NamedThreads.Count(DecoderThread);
        using var cache = new ThemeAssetCache(TestProbes.Free, TestProbes.Free);

        var first = ThemeLoader.Load(folder.Folder, Layout, cache);
        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 1), "the player's thread wasn't seen");

        // An edit that doesn't touch the clip: the same player carries on, and no thread starts.
        var second = ThemeLoader.Load(folder.Folder, Layout + "\n  - { type: text, at: [0, 0], size: [100, 40], text: hi }\n", cache);
        Assert.Same(Player(first), Player(second));
        first.Dispose();
        Assert.NotNull(Player(second)!.FrameAt(TimeSpan.Zero));
        Assert.Equal(before + 1, NamedThreads.Count(DecoderThread));

        // The clip changes on disk: the next load opens it again, and the old player stops with the last load using it.
        folder.AddFile("clip.gif", Gif(SKColors.Blue));
        File.SetLastWriteTimeUtc(Path.Combine(folder.Folder, "clip.gif"), new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        var third = ThemeLoader.Load(folder.Folder, Layout, cache);
        Assert.NotSame(Player(second), Player(third));
        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 2), "the new player's thread wasn't seen");
        second.Dispose();
        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 1), "the old player is still running");

        third.Dispose();
        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before), "the last player is still running");
        Assert.Equal(0, cache.Count);
    }

    /// Moving or resizing a video widget, a pixel at a time as a drag does, keeps the clip playing in its player, scaled
    /// to the new box: no new decoder, thread or measurement, and no restart from the first frame.
    [Fact]
    public void A_clip_moved_or_resized_keeps_its_player_scaled_to_the_new_box()
    {
        using var folder = new TempTheme(Layout);
        folder.AddFile("clip.gif", Gif(SKColors.Lime));
        var before = NamedThreads.Count(DecoderThread);
        var probe = new CountingClipProbe();
        using var cache = new ThemeAssetCache(TestProbes.Free, probe.Measure);

        using var first = ThemeLoader.Load(folder.Folder, Layout, cache);
        var player = Player(first)!;
        using var moved = ThemeLoader.Load(folder.Folder, Layout.Replace("[100, 100]", "[101, 100]"), cache);
        Assert.Same(player, Player(moved));
        Assert.Equal(SKRectI.Create(101, 100, 320, 80), player.Destination);
        using var resized = ThemeLoader.Load(folder.Folder, Layout.Replace("[320, 80]", "[160, 40]"), cache);
        Assert.Same(player, Player(resized));
        Assert.Equal(SKRectI.Create(100, 100, 160, 40), player.Destination);
        // Each theme draws it in its own box.
        Assert.Same(player, Player(first));
        Assert.Equal(SKRectI.Create(100, 100, 320, 80), player.Destination);

        Assert.Equal(1, probe.Calls);
        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 1), "the player's thread wasn't seen");
        Assert.Equal(before + 1, NamedThreads.Count(DecoderThread));
    }

    /// Frames made for the old box are dropped, and the clip goes on at the new size from where it was.
    [Fact]
    public void A_resized_clip_draws_frames_of_the_new_size()
    {
        const string Moving = """
            canvas: { orientation: landscape }
            widgets:
              - { type: video, at: [100, 100], size: [320, 80], src: clip.gif, fit: stretch }
            """;
        using var folder = new TempTheme(Moving);
        folder.AddFile("clip.gif", Gif(SKColors.Lime));
        using var cache = new ThemeAssetCache(TestProbes.Free, TestProbes.Free);
        using var first = ThemeLoader.Load(folder.Folder, Moving, cache);
        using (var renderer = new ThemeRenderer(first))
            renderer.Render(TestSensors.Of(), TimeSpan.Zero).Dispose();

        using var resized = ThemeLoader.Load(folder.Folder, Moving.Replace("[320, 80]", "[160, 40]"), cache);
        using var drawer = new ThemeRenderer(resized);
        Assert.True(Wait.Until(() =>
        {
            using var frame = drawer.Render(TestSensors.Of(), TimeSpan.FromSeconds(1));
            return Player(resized)!.FrameAt(TimeSpan.FromSeconds(1))?.Width == 160;
        }), "no frame of the new size came");

        using var drawn = drawer.Render(TestSensors.Of(), TimeSpan.FromSeconds(1));
        var background = Images.PixelAt(drawn, 10, 10);
        Assert.NotEqual(background, Images.PixelAt(drawn, 255, 135));
        Assert.Equal(background, Images.PixelAt(drawn, 265, 145));
    }

    /// A clip that stopped partway with an error (a damaged frame) stays stopped in its player, so the next load opens
    /// it again rather than sharing the stopped player until the file changes.
    [Fact]
    public void A_clip_that_stopped_with_an_error_is_opened_again_on_the_next_load()
    {
        const string Canvas = "canvas: { orientation: landscape }\n";
        using var folder = new TempTheme(Canvas);
        folder.AddFile("clip.gif", Gif(SKColors.Lime));
        var full = Path.Combine(folder.Folder, "clip.gif");
        using var cache = new ThemeAssetCache(TestProbes.Free, TestProbes.Free);
        using var first = ThemeLoader.Load(folder.Folder, Canvas, cache);
        using var second = ThemeLoader.Load(folder.Folder, Canvas, cache);
        var opened = 0;
        OpenedClip Open(int failAt)
        {
            opened++;
            var decoder = new FakeDecoder(10, SKColors.Red, SKColors.Lime, SKColors.Blue) { FailAt = failAt };
            return new OpenedClip(new Video.VideoPlayer(decoder, new Video.FrameScaler(ImageFit.Stretch, decoder.Size, SKRect.Create(8, 8))), null);
        }

        var broken = cache.Clip(first.Assets, full, ImageFit.Stretch, SKRect.Create(8, 8), () => Open(failAt: 1)).Player!;
        string? error = null;
        Assert.True(Wait.Until(() => (error ??= broken.TakeError()) is not null), "the clip never failed");
        var next = cache.Clip(second.Assets, full, ImageFit.Stretch, SKRect.Create(8, 8), () => Open(failAt: -1)).Player;

        Assert.NotSame(broken, next);
        Assert.Equal(2, opened);
    }

    /// Every clip is quick enough, and the calls are counted.
    sealed class CountingClipProbe
    {
        int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public DecodeCost Measure(Video.IVideoDecoder decoder, Video.FrameScaler scaler)
        {
            Interlocked.Increment(ref _calls);
            return TestProbes.Free(decoder, scaler);
        }
    }

    /// A video tool or an export still writing the clip holds it locked. That says nothing about the clip, so the next
    /// load tries it again rather than keeping the refusal until the file changes.
    [Fact]
    public void A_clip_locked_by_another_program_is_opened_again_on_the_next_load()
    {
        using var folder = new TempTheme(Layout);
        folder.AddFile("clip.gif", Gif(SKColors.Lime));
        using var cache = new ThemeAssetCache(TestProbes.Free, TestProbes.Free);

        LoadedTheme locked;
        using (File.Open(Path.Combine(folder.Folder, "clip.gif"), FileMode.Open, FileAccess.Read, FileShare.None))
            locked = ThemeLoader.Load(folder.Folder, Layout, cache);
        using (locked)
        using (var unlocked = ThemeLoader.Load(folder.Folder, Layout, cache))
        {
            Assert.Null(Player(locked));
            Assert.StartsWith("Video 'clip.gif' was skipped: It could not be played: ", Assert.Single(locked.Warnings));
            Assert.NotNull(Player(unlocked));
            Assert.Empty(unlocked.Warnings);
        }
    }

    /// A player hands out one frame at a time, so two widgets showing the same clip in the same box still get one each.
    [Fact]
    public void Two_widgets_playing_the_same_clip_in_one_load_get_a_player_each()
    {
        var layout = Layout + "\n  - { type: video, at: [100, 100], size: [320, 80], src: clip.gif }\n";
        using var folder = new TempTheme(layout);
        folder.AddFile("clip.gif", Gif(SKColors.Lime));
        var before = NamedThreads.Count(DecoderThread);
        using var cache = new ThemeAssetCache(TestProbes.Free, TestProbes.Free);

        using (var first = ThemeLoader.Load(folder.Folder, layout, cache))
        using (var second = ThemeLoader.Load(folder.Folder, layout, cache))
        {
            Assert.NotSame(Player(first, 0), Player(first, 1));
            Assert.Same(Player(first, 0), Player(second, 0));
            Assert.Same(Player(first, 1), Player(second, 1));
            Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 2), "the players' threads weren't seen");
        }

        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before), "the players are still running");
    }

    [Fact]
    public void Disposing_the_cache_stops_every_player()
    {
        using var folder = new TempTheme(Layout);
        folder.AddFile("clip.gif", Gif(SKColors.Lime));
        var before = NamedThreads.Count(DecoderThread);
        var cache = new ThemeAssetCache(TestProbes.Free, TestProbes.Free);
        var theme = ThemeLoader.Load(folder.Folder, Layout, cache);
        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before + 1), "the player's thread wasn't seen");
        var player = Player(theme)!;

        cache.Dispose();

        Assert.True(Wait.Until(() => NamedThreads.Count(DecoderThread) == before), "the player is still running");
        Assert.Null(player.FrameAt(TimeSpan.Zero));
        Assert.Null(Player(theme)); // the theme draws without it (ThemeAssets.Detach)
        theme.Dispose();
    }
}
