using Marquee.Core.Themes;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// Shader and video cost probes for tests that are about something else, so a busy machine can't skip their shader or
/// video layers.
static class TestProbes
{
    /// Every shader fits in any frame budget.
    public static TimeSpan Free(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas) => TimeSpan.Zero;

    /// Every clip decodes in no time, without a frame being read.
    public static DecodeCost Free(IVideoDecoder decoder, FrameScaler scaler) => new(TimeSpan.Zero, TimeSpan.Zero);
}
