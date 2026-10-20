using System.Diagnostics;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// A player decodes ahead on its own thread, so a frame only shows once that thread has prepared it. Tests wait for the
/// expected frame rather than sleeping; a frame that never comes fails after Wait's timeout.
public class VideoPlayerTests
{
    static readonly SKColor[] Rgb = [SKColors.Red, SKColors.Lime, SKColors.Blue];

    static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    static VideoPlayer Play(FakeDecoder decoder) =>
        new(decoder, new FrameScaler(ImageFit.Stretch, decoder.Size, SKRect.Create(8, 8)));

    static SKColor? ColorAt(VideoPlayer player, TimeSpan time) =>
        player.FrameAt(time) is { } image ? image.PeekPixels().GetPixelColor(1, 1) : null;

    static void AssertShows(VideoPlayer player, TimeSpan time, SKColor expected)
    {
        SKColor? shown = null;
        Wait.Until(() => (shown = ColorAt(player, time)) == expected);
        Assert.True(shown == expected, $"At {time.TotalMilliseconds} ms expected {expected}, got {shown}");
    }

    /// A clip of `count` frames, `frameMs` apart, each a color that gives its number.
    static FakeDecoder Numbered(int frameMs, int count) =>
        new(frameMs, [.. Enumerable.Range(0, count).Select(i => new SKColor((byte)i, (byte)(i >> 8), 0))]);

    static int NumberAt(VideoPlayer player, TimeSpan time)
    {
        var color = ColorAt(player, time)!.Value;
        return color.Red | color.Green << 8;
    }

    /// One frame of a render loop at `time` on the test's render clock: the number of the clip frame it shows. Then it
    /// waits until the decoder has filled its queue again, as a decoder with time to spare would before the next frame.
    static int Render(VideoPlayer player, TimeSpan time)
    {
        var shown = NumberAt(player, time);
        Assert.True(Wait.Until(() => player.Queued == VideoPlayer.FramesAhead), "The decoder never filled its queue");
        return shown;
    }

    [Fact]
    public void Frames_show_at_their_times()
    {
        using var player = Play(new FakeDecoder(100, Rgb));

        Assert.Equal(SKColors.Red, ColorAt(player, Ms(0))); // the first frame is ready as soon as the player is
        AssertShows(player, Ms(99), SKColors.Red);
        AssertShows(player, Ms(150), SKColors.Lime);
        AssertShows(player, Ms(200), SKColors.Blue);
    }

    [Fact]
    public void Frames_are_scaled_to_the_size_they_are_drawn_at()
    {
        using var player = Play(new FakeDecoder(100, Rgb));

        var frame = player.FrameAt(Ms(0))!;

        Assert.Equal((8, 8), (frame.Width, frame.Height));
        Assert.Equal(SKRectI.Create(8, 8), player.Destination);
    }

    [Fact]
    public void The_clip_loops_with_each_pass_after_the_one_before()
    {
        var decoder = new FakeDecoder(100, Rgb);
        using var player = Play(decoder);

        for (var ms = 0; ms <= 1000; ms += 50)
            AssertShows(player, Ms(ms), Rgb[ms / 100 % 3]);
        Assert.True(decoder.Rewinds >= 3, $"Rewound {decoder.Rewinds} times");
    }

    [Fact]
    public void A_clip_whose_first_frame_is_not_at_zero_shows_each_frame_for_its_length_across_every_loop()
    {
        // Frames at 500, 600 and 700 ms, as in an H.264 stream with B-frames and no edit list, or a clip cut from a
        // recording. The first frame doesn't wait out the 500 ms, and the last isn't held for them at each loop.
        using var player = Play(new FakeDecoder(100, Rgb) { FirstMs = 500 });

        for (var frame = 0; frame <= 9; frame++)
        {
            AssertShows(player, Ms(frame * 100), Rgb[frame % 3]);
            AssertShows(player, Ms(frame * 100 + 99), Rgb[frame % 3]);
        }
    }

    [Fact]
    public void The_clip_starts_with_the_first_frame_drawn_whatever_the_clock_says()
    {
        // The render loop's clock runs from when the loop started, so a theme loaded later starts at a large time.
        using var player = Play(new FakeDecoder(100, Rgb));

        Assert.Equal(SKColors.Red, ColorAt(player, TimeSpan.FromMinutes(10)));
        AssertShows(player, TimeSpan.FromMinutes(10) + Ms(150), SKColors.Lime);
    }

    [Fact]
    public void A_decoder_that_falls_behind_keeps_the_last_frame_without_blocking_and_then_carries_on()
    {
        var decoder = new FakeDecoder(100, Rgb) { BlockFrom = 1 };
        using var player = Play(decoder);
        Assert.Equal(SKColors.Red, ColorAt(player, Ms(0)));

        var clock = Stopwatch.StartNew();
        Assert.Equal(SKColors.Red, ColorAt(player, Ms(10_250)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"FrameAt waited {clock.Elapsed.TotalMilliseconds} ms for the decoder");

        // Far behind its clock, the player picks up where it stopped rather than racing through frames to catch up:
        // the next frame is the one after the frame that stayed on screen.
        decoder.Release();
        AssertShows(player, Ms(10_250), SKColors.Lime);
        AssertShows(player, Ms(10_350), SKColors.Blue);
    }

    [Fact]
    public void A_clip_faster_than_the_render_rate_plays_at_its_own_speed_by_skipping_frames()
    {
        // A 100 fps clip drawn at 10 fps: each render has to move about 10 clip frames on, more than the decoder queues.
        using var player = Play(Numbered(10, 1000));

        for (var ms = 0; ms < 3000; ms += 100) Render(player, Ms(ms));

        Assert.InRange(Render(player, Ms(3000)), 285, 300);
    }

    [Fact]
    public void After_a_jump_in_the_render_clock_the_clip_plays_on_at_its_own_speed_at_once()
    {
        // A 25 fps clip drawn at 50 fps, whose render clock jumps 10 minutes, as it does when the PC wakes from sleep.
        using var player = Play(Numbered(40, 1000));
        for (var ms = 0; ms < 1000; ms += 20) Render(player, Ms(ms));

        var jump = TimeSpan.FromMinutes(10);
        var first = Render(player, jump + Ms(1000));
        for (var ms = 1020; ms < 2000; ms += 20) Render(player, jump + Ms(ms));

        // One second of a 25 fps clip, not a race through the 10 minutes it missed.
        Assert.InRange(Render(player, jump + Ms(2000)) - first, 23, 27);
    }

    [Fact]
    public void Playing_reuses_a_few_frame_buffers_however_many_frames_it_shows()
    {
        using var player = Play(Numbered(10, 1000));

        for (var ms = 0; ms < 1000; ms += 10) Render(player, Ms(ms));

        Assert.InRange(Render(player, Ms(1000)), 95, 100);
        Assert.InRange(player.BuffersCreated, 1, VideoPlayer.FramesAhead + 2); // queued, on screen, being decoded
    }

    [Fact]
    public void A_frame_handed_out_keeps_its_pixels_while_the_decoder_prepares_the_next_ones()
    {
        using var player = Play(Numbered(10, 1000));
        var held = player.FrameAt(Ms(0))!;

        Assert.True(Wait.Until(() => player.Queued == VideoPlayer.FramesAhead));
        Assert.Equal(new SKColor(0, 0, 0), held.PeekPixels().GetPixelColor(1, 1));
        Assert.Equal(1, NumberAt(player, Ms(10))); // the next call releases it, and only then can its buffer be reused
    }

    [Fact]
    public void Dispose_does_not_wait_for_a_decoder_stuck_in_a_call_and_leaves_the_decoder_to_its_thread()
    {
        var decoder = new FakeDecoder(100, Rgb) { BlockFrom = 1 };
        var player = new VideoPlayer(decoder, new FrameScaler(ImageFit.Stretch, decoder.Size, SKRect.Create(8, 8)))
        {
            StopTimeout = Ms(200),
        };
        Assert.True(Wait.Until(() => decoder.ReadBy != Thread.CurrentThread)); // its thread is in Next, blocked

        var clock = Stopwatch.StartNew();
        player.Dispose();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Dispose waited {clock.Elapsed.TotalSeconds:0.0} s");
        Assert.Null(player.FrameAt(Ms(100)));
        Assert.False(decoder.IsDisposed); // still in use by the stuck call
        decoder.Release();
        Assert.True(Wait.Until(() => decoder.IsDisposed), "The decoder was never disposed once its call returned");
    }

    [Fact]
    public void RequestStop_returns_at_once_and_stops_the_frames_while_Dispose_still_cleans_up()
    {
        var decoder = new FakeDecoder(100, Rgb) { BlockFrom = 1 };
        var player = new VideoPlayer(decoder, new FrameScaler(ImageFit.Stretch, decoder.Size, SKRect.Create(8, 8)))
        {
            StopTimeout = Ms(200),
        };
        Assert.True(Wait.Until(() => decoder.ReadBy != Thread.CurrentThread)); // its thread is in Next, blocked

        var clock = Stopwatch.StartNew();
        player.RequestStop();
        Assert.True(clock.Elapsed < Ms(100), $"RequestStop waited {clock.Elapsed.TotalMilliseconds:0} ms");
        Assert.Null(player.FrameAt(Ms(100)));

        decoder.Release();
        player.Dispose();
        Assert.True(decoder.IsDisposed);
        player.Dispose(); // a second time does nothing
    }

    [Fact]
    public void Stopping_several_stuck_players_waits_one_timeout_for_all_of_them_not_one_each()
    {
        // A theme can play 4 clips. With each decoder stuck (a network share that went away), stopping them one after
        // another would hold the render thread for 4 timeouts on a theme switch; signalled together and joined against
        // one deadline, they cost one.
        var timeout = Ms(500);
        var decoders = Enumerable.Range(0, 4).Select(_ => new FakeDecoder(100, Rgb) { BlockFrom = 1 }).ToList();
        var players = decoders.Select(decoder =>
            new VideoPlayer(decoder, new FrameScaler(ImageFit.Stretch, decoder.Size, SKRect.Create(8, 8))) { StopTimeout = timeout })
            .ToList();
        Assert.True(Wait.Until(() => decoders.All(d => d.ReadBy is { } thread && thread != Thread.CurrentThread)));

        var clock = Stopwatch.StartNew();
        ThemeAssets.StopVideos(players);
        clock.Stop();

        Assert.True(clock.Elapsed >= timeout - Ms(50), $"Stopping waited only {clock.Elapsed.TotalMilliseconds:0} ms");
        Assert.True(clock.Elapsed < timeout * 2, $"Stopping waited {clock.Elapsed.TotalMilliseconds:0} ms for 4 stuck players");
        Assert.All(players, player => Assert.Null(player.FrameAt(Ms(100))));
        Assert.All(decoders, decoder => Assert.False(decoder.IsDisposed)); // still in use by the stuck calls
        foreach (var decoder in decoders) decoder.Release();
        Assert.True(Wait.Until(() => decoders.All(d => d.IsDisposed)), "A decoder was never disposed once its call returned");
    }

    [Fact]
    public void A_damaged_frame_stops_playback_on_the_last_good_frame_and_is_reported_once()
    {
        var decoder = new FakeDecoder(100, Rgb) { FailAt = 2 };
        using var player = Play(decoder);
        Assert.Equal(SKColors.Red, ColorAt(player, Ms(0)));
        AssertShows(player, Ms(150), SKColors.Lime);

        string? error = null;
        Assert.True(Wait.Until(() => (error = player.TakeError()) is not null), "The error was never reported");
        Assert.Equal("Frame 3 is damaged.", error);
        Assert.Null(player.TakeError());

        Assert.Equal(SKColors.Lime, ColorAt(player, Ms(250)));
        Assert.Equal(SKColors.Lime, ColorAt(player, Ms(5_000)));
    }

    [Fact]
    public void A_clip_of_one_frame_is_decoded_once()
    {
        var decoder = new FakeDecoder(100, SKColors.Red);
        using var player = Play(decoder);

        // The player's thread reads past the frame, finds the end of a one-frame clip, and stops.
        Assert.True(Wait.Until(() => decoder.ReadBy is { IsAlive: false } && decoder.ReadBy != Thread.CurrentThread));
        Assert.Equal(2, decoder.Reads);
        Assert.Equal(0, decoder.Rewinds);
        Assert.Equal(SKColors.Red, ColorAt(player, Ms(5_000)));
    }

    [Fact]
    public void A_clip_with_no_frames_is_refused()
    {
        using var decoder = new FakeDecoder(100); // still the caller's when the player refuses it

        var error = Assert.Throws<VideoDecodeException>(() => Play(decoder));

        Assert.Equal("The clip has no frames.", error.Message);
    }

    [Fact]
    public void Dispose_stops_the_decoding_thread_and_frees_the_frames()
    {
        var decoder = new FakeDecoder(100, Rgb);
        var player = Play(decoder);
        var shown = player.FrameAt(Ms(0))!;
        Assert.True(Wait.Until(() => decoder.ReadBy != Thread.CurrentThread));
        var thread = decoder.ReadBy!;

        player.Dispose();

        Assert.False(thread.IsAlive);
        Assert.True(decoder.IsDisposed);
        Assert.Equal(IntPtr.Zero, shown.Handle);
        Assert.Null(player.FrameAt(Ms(100)));
        player.Dispose(); // a second dispose does nothing
    }
}
