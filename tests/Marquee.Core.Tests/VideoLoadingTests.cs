using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A video layer whose clip can't be played, or can't be decoded fast enough, is skipped with a warning that says why
/// when the theme loads; the rest of the theme still renders.
public class VideoLoadingTests
{
    static readonly SKColor[] Rgb = [SKColors.Red, SKColors.Lime, SKColors.Blue];

    static byte[] Gif() => GifWriter.Write(16, 4, Rgb,
        GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10));

    static TempTheme Theme(string clip, byte[]? contents = null)
    {
        var theme = new TempTheme($"canvas: {{ orientation: landscape }}\nbackground:\n  - fill: \"#203040\"\n  - video: {clip}\n");
        if (contents is not null) theme.AddFile(clip, contents);
        return theme;
    }

    static VideoLayer Layer(LoadedTheme theme) => (VideoLayer)theme.Spec.Background[1];

    /// A probe that reports each cost in turn (the last one from then on) against 33 ms frames, and records what it
    /// was asked to measure.
    sealed class FakeProbe(params double[] milliseconds)
    {
        public List<SKRectI> Calls { get; } = [];

        public DecodeCost Measure(IVideoDecoder decoder, FrameScaler scaler)
        {
            Calls.Add(scaler.Destination);
            return new DecodeCost(TimeSpan.FromMilliseconds(milliseconds[Math.Min(Calls.Count, milliseconds.Length) - 1]),
                TimeSpan.FromMilliseconds(33));
        }
    }

    [Fact]
    public void A_good_clip_loads_with_no_warnings()
    {
        using var folder = Theme("clips/rain.gif", Gif());
        using var theme = folder.Load();

        Assert.Empty(theme.Warnings);
        Assert.NotNull(theme.Assets.Video(Layer(theme)));
    }

    [Fact]
    public void A_missing_clip_is_skipped()
    {
        using var folder = Theme("clips/rain.gif");
        using var theme = folder.Load();

        Assert.Equal(["Video 'clips/rain.gif' was skipped: The file was not found."], theme.Warnings);
        Assert.Null(theme.Assets.Video(Layer(theme)));
    }

    [Fact]
    public void A_clip_outside_the_theme_folder_is_skipped()
    {
        using var folder = Theme("../rain.gif");
        using var theme = folder.Load();

        Assert.Equal(["Video '../rain.gif' was skipped: It is outside the theme folder."], theme.Warnings);
    }

    [Fact]
    public void A_clip_over_the_size_limit_is_skipped_before_it_is_opened()
    {
        Assert.Equal(200L * 1024 * 1024, VideoAssets.MaxClipBytes);

        // The real limit would need a 200 MB file; a smaller one exercises the same check.
        using var folder = new TempFolder();
        File.WriteAllBytes(folder.File("big.gif"), new byte[1024 * 1024 + 1]);
        var warnings = new List<string>();
        var probe = new FakeProbe(1);

        var player = VideoAssets.Load(folder.Path, new VideoLayer("big.gif", ImageFit.Cover, 1, SKBlendMode.SrcOver), warnings,
            new SKSizeI(1920, 480), probe.Measure, maxBytes: 1024 * 1024);

        Assert.Null(player);
        Assert.Equal(["Video 'big.gif' was skipped: The file is 2 MB, over the 1 MB limit."], warnings);
        Assert.Empty(probe.Calls);
    }

    [Fact]
    public void A_clip_that_cannot_be_decoded_is_skipped_with_the_decoders_reason()
    {
        using var folder = Theme("clip.gif", "not a picture"u8.ToArray());
        using var theme = folder.Load();

        Assert.Equal(["Video 'clip.gif' was skipped: The clip is not a GIF or WebP image that can be read."], theme.Warnings);
    }

    [Fact]
    public void An_MP4_that_cannot_be_decoded_is_skipped_with_Windows_reason()
    {
        using var folder = Theme("clip.mp4", "not a video"u8.ToArray());
        using var theme = folder.Load();

        var warning = Assert.Single(theme.Warnings);
        if (MediaFoundation.IsAvailable)
            Assert.StartsWith("Video 'clip.mp4' was skipped: Windows can't read the file as video", warning);
        else
            Assert.Contains("Media Feature Pack", warning);
    }

    [Fact]
    public void A_clip_that_cannot_be_decoded_as_fast_as_it_plays_is_skipped_with_its_cost()
    {
        using var folder = Theme("clips/rain.gif", Gif());
        using var theme = ThemeLoader.Load(folder.Folder, null, TestProbes.Free, new FakeProbe(45).Measure);

        Assert.Equal(["Video 'clips/rain.gif' was skipped: Decoding a frame takes about 45 ms, longer than the 33 ms " +
            "each frame is shown, so playback can't keep up. Use a smaller clip, or one with fewer frames per second."],
            theme.Warnings);
        Assert.Null(theme.Assets.Video(Layer(theme)));
    }

    [Fact]
    public void A_slow_measurement_is_taken_again_before_the_clip_is_skipped()
    {
        using var folder = Theme("clips/rain.gif", Gif());
        var probe = new FakeProbe(500, 5);
        using var theme = ThemeLoader.Load(folder.Folder, null, TestProbes.Free, probe.Measure);

        Assert.Empty(theme.Warnings);
        Assert.Equal(2, probe.Calls.Count);
        Assert.NotNull(theme.Assets.Video(Layer(theme)));
    }

    [Fact]
    public void The_cost_is_measured_scaling_to_where_the_layer_draws()
    {
        using var folder = new TempTheme("canvas: { orientation: landscape }\nbackground:\n  - video: square.gif\n    fit: contain\n");
        folder.AddFile("square.gif", GifWriter.Write(8, 8, Rgb, GifFrame.Solid(SKRectI.Create(8, 8), 0, 10)));
        var probe = new FakeProbe(1);
        using var theme = ThemeLoader.Load(folder.Folder, null, TestProbes.Free, probe.Measure);

        Assert.Equal([new SKRectI(720, 0, 1200, 480)], probe.Calls);
    }

    [Fact]
    public void The_real_measurement_keeps_a_small_clip()
    {
        // The real probe. A 16×4 GIF decodes and scales in a small fraction of its 100 ms frames on any machine.
        using var folder = Theme("clips/rain.gif", Gif());
        using var theme = ThemeLoader.Load(folder.Folder);

        Assert.Empty(theme.Warnings);
        Assert.NotNull(theme.Assets.Video(Layer(theme)));
    }

    [Fact]
    public void Players_stop_when_the_theme_is_disposed()
    {
        using var folder = Theme("clips/rain.gif", Gif());
        var theme = folder.Load();
        var player = theme.Assets.Video(Layer(theme))!;
        Assert.NotNull(player.FrameAt(TimeSpan.Zero));

        theme.Dispose();

        Assert.Null(player.FrameAt(TimeSpan.Zero));
    }
}
