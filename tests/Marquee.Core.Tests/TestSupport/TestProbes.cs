using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// Shader cost probes for tests that are about something else, so a busy machine can't skip their shader layers.
static class TestProbes
{
    /// Every shader fits in any frame budget.
    public static TimeSpan Free(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas) => TimeSpan.Zero;
}
