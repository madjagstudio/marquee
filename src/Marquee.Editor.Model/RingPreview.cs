using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using Marquee.Core.Themes.Parsing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// A snapshot of the theme's ring, rendered with the same colors and timing as the device.
public sealed class RingPreview(LayoutDocument document)
{
    public RingSpec Spec { get; } = LayoutParser.Parse(document.Text).Ring ?? RingSpec.Default;

    public SKColor[] Colors(SensorSnapshot values, TimeSpan time)
    {
        var colors = new SKColor[RingRenderer.LedCount];
        RingRenderer.Render(Spec, values, time, colors);
        return colors;
    }
}
