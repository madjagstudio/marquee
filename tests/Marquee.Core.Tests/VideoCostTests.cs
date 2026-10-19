using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The load-time measurement of what a clip costs to decode and scale, frame by frame.
public class VideoCostTests
{
    static readonly FrameScaler Scaler = new(ImageFit.Cover, new SKSizeI(4, 4), SKRect.Create(1920, 480));

    static SKColor[] RedFrames(int count) => Enumerable.Repeat(SKColors.Red, count).ToArray();

    [Fact]
    public void Up_to_four_frames_after_the_first_are_timed()
    {
        // The first frame pays for setting up the decoder, which a playing clip pays once, so it isn't timed.
        using var decoder = new FakeDecoder(10, RedFrames(100));

        var cost = VideoCost.Measure(decoder, Scaler);

        Assert.Equal(5, decoder.Reads);
        Assert.Equal(TimeSpan.FromMilliseconds(10), cost.FrameDuration);
    }

    [Fact]
    public void Timing_stops_after_a_second_of_clip()
    {
        using var decoder = new FakeDecoder(400, RedFrames(10));

        var cost = VideoCost.Measure(decoder, Scaler);

        Assert.Equal(1 + 3, decoder.Reads); // 400 + 400 + 400 ms timed
        Assert.Equal(TimeSpan.FromMilliseconds(400), cost.FrameDuration);
    }

    [Fact]
    public void The_cost_is_the_average_time_to_decode_and_scale_a_frame()
    {
        using var decoder = new FakeDecoder(10, RedFrames(100)) { Delay = TimeSpan.FromMilliseconds(30) };

        var cost = VideoCost.Measure(decoder, Scaler);

        Assert.True(cost.PerFrame >= TimeSpan.FromMilliseconds(25), $"Measured {cost.PerFrame.TotalMilliseconds} ms");
    }

    [Fact]
    public void A_clip_of_one_frame_costs_nothing_to_play()
    {
        // The player decodes its only frame once and keeps it.
        using var decoder = new FakeDecoder(100, RedFrames(1)) { Delay = TimeSpan.FromMilliseconds(30) };

        var cost = VideoCost.Measure(decoder, Scaler);

        Assert.Equal(TimeSpan.Zero, cost.PerFrame);
    }
}
