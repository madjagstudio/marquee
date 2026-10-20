using Marquee.Core.Devices.LedRing;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// The LED ring's colors for one instant. It depends only on the spec, the sensors and the time, so the same frame
/// always gets the same colors. LED 0 is where effects start, and spin and rainbow travel towards higher indices.
public static class RingRenderer
{
    /// The Universal Screen 8.8" ring's LEDs, as many as the device takes (one number, so the two can't drift apart).
    public const int LedCount = LedRingPackets.LedCount;

    /// At speed 1: one breath every 3 s, one spin turn every 2 s, one rainbow turn every 6 s.
    const double BreatheSeconds = 3, SpinSeconds = 2, RainbowSeconds = 6;

    /// A breath fades to this fraction of the brightness, not to dark, so the ring never looks switched off.
    const float BreatheLow = 0.2f;

    /// How much of the ring the spinning segment's tail covers.
    const double SpinTail = 0.25;

    /// Fills `leds` (normally LedCount long; any length is spread round the ring). The output is opaque, with the
    /// brightness, and any translucency in the color, baked into the RGB, since the ring has no alpha of its own.
    public static void Render(RingSpec spec, SensorSnapshot sensors, TimeSpan time, Span<SKColor> leds)
    {
        var ring = Effective(spec, sensors);
        var count = leds.Length;
        var level = ring.Brightness * ring.Color.Alpha / 255.0;

        switch (ring.Effect)
        {
            case RingEffect.Static:
                leds.Fill(Scale(ring.Color, level));
                break;

            case RingEffect.Breathe:
                var pulse = new PulseSpec(PulseTarget.Opacity, (float)(BreatheSeconds * 1000 / ring.Speed), BreatheLow, 1);
                leds.Fill(Scale(ring.Color, level * StyleEvaluator.PulseFactor(pulse, time)));
                break;

            case RingEffect.Spin:
                var head = Phase(time, SpinSeconds, ring.Speed) * count;
                for (var i = 0; i < count; i++)
                    leds[i] = Scale(ring.Color, level * SpinLevel(head, i, count));
                break;

            default:
                var turn = Phase(time, RainbowSeconds, ring.Speed);
                for (var i = 0; i < count; i++)
                {
                    var hue = Wrap((double)i / count - turn) * 360;
                    leds[i] = Scale(SKColor.FromHsv((float)hue, 100, 100), ring.Brightness);
                }

                break;
        }
    }

    /// The spec with each matching `when` rule applied, in order, as a widget's style is.
    public static RingSpec Effective(RingSpec spec, SensorSnapshot sensors)
    {
        if (spec.Source is null || spec.When.Count == 0) return spec;

        var subject = spec.Source.Evaluate(sensors);
        var stale = subject is null;
        var ring = spec;
        foreach (var rule in spec.When)
            if (rule.Matches(subject, stale))
                ring = ring with
                {
                    Color = rule.Color ?? ring.Color,
                    Effect = rule.Effect ?? ring.Effect,
                    Speed = rule.Speed ?? ring.Speed,
                    Brightness = rule.Brightness ?? ring.Brightness,
                };
        return ring;
    }

    /// The spinning segment at LED `i`: full at the head, fading over the tail behind it, and partly lit on the LED the
    /// head is about to reach, so the movement looks smooth rather than jumping a whole LED at a time.
    static double SpinLevel(double head, int i, int count)
    {
        var behind = Wrap((head - i) / count) * count;
        var ahead = count - behind;
        if (ahead < 1) return 1 - ahead;

        var tail = SpinTail * count;
        if (behind >= tail) return 0;
        var fade = 1 - behind / tail;
        return fade * fade;
    }

    /// How far through its current turn (or breath) an effect is, from 0 up to 1.
    static double Phase(TimeSpan time, double secondsAtSpeed1, float speed) => Wrap(time.TotalSeconds * speed / secondsAtSpeed1);

    static double Wrap(double turns) => turns - Math.Floor(turns);

    static SKColor Scale(SKColor color, double level) => new(Channel(color.Red, level), Channel(color.Green, level), Channel(color.Blue, level));

    static byte Channel(byte value, double level) => (byte)Math.Clamp(Math.Round(value * level), 0, 255);
}
