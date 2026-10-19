using System.Diagnostics;
using Marquee.Core.Video;

namespace Marquee.Core.Themes;

/// Measures what a clip costs to play: decoding and scaling a frame, against how long a frame shows. Reads frames from
/// the start of the clip; the caller rewinds it afterwards.
delegate DecodeCost VideoProbe(IVideoDecoder decoder, FrameScaler scaler);

/// The average time to decode and scale one frame, and the average time a frame shows.
readonly record struct DecodeCost(TimeSpan PerFrame, TimeSpan FrameDuration);

/// Estimates a clip's decoding cost from its first few frames, when the theme loads. A clip that decodes slower than
/// it plays would crawl and stutter on screen, so it is skipped instead (see VideoAssets).
///
/// The budget is the clip's own frame duration, not the theme's frame time: a player decodes on its own thread, and the
/// render thread only draws the frame it has prepared, a copy of a canvas-sized image. What decoding must keep up with
/// is the clip, every one of whose frames has to be decoded in order whatever the theme's fps.
static class VideoCost
{
    /// Frames timed after the first, which isn't timed: it pays for setting up the decoder, once per clip.
    const int TimedFrames = 4;

    /// Timing stops once this much of the clip is covered, so a slideshow of long frames isn't decoded far ahead.
    static readonly TimeSpan TimedClip = TimeSpan.FromSeconds(1);

    public static DecodeCost Measure(IVideoDecoder decoder, FrameScaler scaler)
    {
        if (decoder.Next() is not { } first) return new DecodeCost(TimeSpan.Zero, TimeSpan.Zero); // the player refuses it
        using var buffers = new FrameBuffers(scaler); // as the player scales frames, reusing one buffer here
        buffers.Scale(first.Pixels).Dispose();

        var clock = new Stopwatch();
        var frames = 0;
        var shown = TimeSpan.Zero;
        while (frames < TimedFrames && shown < TimedClip)
        {
            clock.Start();
            var frame = decoder.Next();
            if (frame is { } decoded) buffers.Scale(decoded.Pixels).Dispose();
            clock.Stop();
            if (frame is not { } timed) break;

            frames++;
            shown += timed.Duration > TimeSpan.Zero ? timed.Duration : VideoPlayer.FallbackDuration;
        }

        // A clip of one frame is decoded once and then only drawn: it has nothing to keep up with.
        if (frames == 0) return new DecodeCost(TimeSpan.Zero, first.Duration);
        return new DecodeCost(clock.Elapsed / frames, shown / frames);
    }
}
