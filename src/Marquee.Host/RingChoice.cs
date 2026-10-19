using Marquee.Core.Config;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Host;

/// What the LED ring shows, from config.yaml's `ring:` and the ring of the theme on screen.
static class RingChoice
{
    /// Null for `ring: off`, which keeps the ring dark. `ring: theme` is the theme's own ring, or the soft white
    /// default for a theme without a `ring:` block. A color is that color, static, at full brightness, for every
    /// theme: the user named the exact color they want, so it isn't dimmed to the default's 40%, and a color with an
    /// alpha byte ("#3A7BFF80") is the way to dim it, since the ring scales brightness by alpha.
    public static RingSpec? Resolve(RingMode mode, SKColor? color, RingSpec? themeRing) => mode switch
    {
        RingMode.Off => null,
        RingMode.Color when color is { } only => new RingSpec(only, RingEffect.Static, 1, 1, null, []),
        _ => themeRing ?? RingSpec.Default,
    };
}
