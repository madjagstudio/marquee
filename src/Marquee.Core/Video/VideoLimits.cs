using System.Globalization;

namespace Marquee.Core.Video;

/// The largest frames a clip may have, for every decoder: 4K either way round, so a portrait 2160×3840 clip for a
/// portrait theme is as welcome as a landscape one. One 4K frame is already about 33 MB of pixels.
static class VideoLimits
{
    public const int MaxLongSide = 3840;
    public const int MaxShortSide = 2160;

    /// Decoders fill whole coding blocks, so the frame they hand over can be larger than the picture in it: 16 pixels a
    /// block for H.264 (a 1080p clip comes out 1920×1088), up to 64 for HEVC, VP9 and AV1. 2160 isn't a multiple of 64,
    /// so a 4K clip from such a decoder fills 2176 rows; refusing that would refuse a clip within the limit. Padding to
    /// whole 64-pixel blocks costs at most 0.8% more memory at 4K, and anything past it is a frame over the limit.
    const int Block = 64;

    /// Refuses a picture over the limit with a message for the theme's author.
    public static void Check(int width, int height)
    {
        if (Math.Max(width, height) > MaxLongSide || Math.Min(width, height) > MaxShortSide)
            throw TooLarge(width, height);
    }

    /// Refuses a decoded frame larger than the limit padded to whole blocks: whatever the picture inside it, a frame is
    /// what the decoder allocates and copies, so it is held to the limit too.
    public static void CheckDecoded(long width, long height)
    {
        if (Math.Max(width, height) > Padded(MaxLongSide) || Math.Min(width, height) > Padded(MaxShortSide))
            throw TooLarge(width, height);
    }

    static long Padded(long side) => (side + Block - 1) / Block * Block;

    public static VideoDecodeException TooLarge(long width, long height) => new(string.Create(CultureInfo.InvariantCulture,
        $"The clip's frames are {width}×{height}, over the {MaxLongSide}×{MaxShortSide} limit."));
}
