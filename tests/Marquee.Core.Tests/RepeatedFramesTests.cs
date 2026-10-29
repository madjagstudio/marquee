using Marquee.Core.Devices;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class RepeatedFramesTests
{
    readonly ManualTime _time = new();

    [Fact]
    public void Nothing_is_a_repeat_before_a_frame_is_sent()
    {
        var repeats = new RepeatedFrames(_time);
        using var frame = Images.Landscape(SKColors.Red);

        Assert.False(repeats.IsRepeat(frame));
    }

    [Fact]
    public void A_frame_with_the_same_pixels_as_the_last_one_sent_is_a_repeat()
    {
        var repeats = new RepeatedFrames(_time);
        using var sent = Images.Landscape(SKColors.Red);
        using var same = Images.Landscape(SKColors.Red);
        repeats.Sent(sent);

        Assert.True(repeats.IsRepeat(same));
    }

    [Fact]
    public void A_frame_with_different_pixels_is_not_a_repeat()
    {
        var repeats = new RepeatedFrames(_time);
        using var sent = Images.Landscape(SKColors.Red);
        using var other = Images.Landscape(SKColors.Blue);
        using var otherShape = Images.Portrait(SKColors.Red);
        repeats.Sent(sent);

        Assert.False(repeats.IsRepeat(other));
        Assert.False(repeats.IsRepeat(otherShape));
    }

    [Fact]
    public void A_repeat_goes_out_again_once_RefreshEvery_has_passed()
    {
        var repeats = new RepeatedFrames(_time);
        using var frame = Images.Landscape(SKColors.Red);
        repeats.Sent(frame);

        _time.Advance(RepeatedFrames.RefreshEvery - TimeSpan.FromMilliseconds(1));
        Assert.True(repeats.IsRepeat(frame));

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(repeats.IsRepeat(frame));
    }

    [Fact]
    public void Forget_makes_the_next_frame_go_out_whatever_it_shows()
    {
        var repeats = new RepeatedFrames(_time);
        using var frame = Images.Landscape(SKColors.Red);
        repeats.Sent(frame);

        repeats.Forget();

        Assert.False(repeats.IsRepeat(frame));
    }
}
