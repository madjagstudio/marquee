using Marquee.Core.Video;

namespace Marquee.Core.Tests;

/// A clip's picture may be up to 3840×2160 either way round. The frames a decoder fills can be a little larger, padded
/// to whole coding blocks below and to the right of the picture, but never by more than one block of 64 pixels.
public class VideoLimitsTests
{
    [Theory]
    [InlineData(3840, 2160)]
    [InlineData(2160, 3840)]
    [InlineData(1920, 1088)] // a 1080p H.264 clip, filled to whole 16-row blocks
    [InlineData(3840, 2176)] // a 4K clip filled to whole 64-row blocks
    [InlineData(2176, 3840)]
    public void Decoded_frames_padded_to_whole_blocks_are_allowed(int width, int height) =>
        VideoLimits.CheckDecoded(width, height);

    [Theory]
    [InlineData(3841, 2160)]
    [InlineData(3840, 2177)]
    [InlineData(2177, 3840)]
    [InlineData(4096, 2160)]
    [InlineData(8192, 8192)] // a small picture can't hide in a huge frame
    public void Decoded_frames_larger_than_padding_explains_are_refused(int width, int height)
    {
        var error = Assert.Throws<VideoDecodeException>(() => VideoLimits.CheckDecoded(width, height));

        Assert.Equal($"The clip's frames are {width}×{height}, over the 3840×2160 limit.", error.Message);
    }
}
