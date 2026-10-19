using Marquee.Core.Expressions;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The LED ring's 60 colors for a ring spec, the sensors and the time.
public class RingRendererTests
{
    static readonly SensorSnapshot NoSensors = TestSensors.Of();

    static RingSpec Spec(SKColor color, RingEffect effect = RingEffect.Static, float speed = 1, float brightness = 1,
        string? source = null, params RingRule[] when) =>
        new(color, effect, speed, brightness, source is null ? null : Expression.Parse(source), when);

    static SKColor[] Render(RingSpec spec, double seconds, SensorSnapshot? sensors = null)
    {
        var leds = new SKColor[RingRenderer.LedCount];
        RingRenderer.Render(spec, sensors ?? NoSensors, TimeSpan.FromSeconds(seconds), leds);
        return leds;
    }

    static RingRule Above(double number, SKColor? color = null, RingEffect? effect = null, float? speed = null, float? brightness = null) =>
        new(WhenCondition.Above, number, null, color, effect, speed, brightness);

    static float Hue(SKColor color)
    {
        color.ToHsv(out var hue, out _, out _);
        return hue;
    }

    static void AssertHue(float expected, SKColor color)
    {
        var difference = Math.Abs(Hue(color) - expected) % 360;
        Assert.True(Math.Min(difference, 360 - difference) <= 2, $"Expected hue about {expected}, got {Hue(color)} ({color})");
    }

    [Fact]
    public void The_default_ring_is_a_soft_static_warm_white()
    {
        Assert.Equal(new SKColor(0xFF, 0xF1, 0xE0), RingSpec.Default.Color);
        Assert.Equal(RingEffect.Static, RingSpec.Default.Effect);
        Assert.Equal(1f, RingSpec.Default.Speed);
        Assert.Equal(0.4f, RingSpec.Default.Brightness);
        Assert.Null(RingSpec.Default.Source);
        Assert.Empty(RingSpec.Default.When);

        var leds = Render(RingSpec.Default, 12.3);

        Assert.All(leds, led => Assert.Equal(new SKColor(102, 96, 90), led));
    }

    [Fact]
    public void Static_is_the_color_on_every_led_at_any_time()
    {
        var color = new SKColor(0x3A, 0x7B, 0xFF);

        Assert.All(Render(Spec(color), 0), led => Assert.Equal(color, led));
        Assert.All(Render(Spec(color), 4.56), led => Assert.Equal(color, led));
    }

    [Fact]
    public void Brightness_is_baked_into_the_rgb_and_alpha_is_opaque()
    {
        var leds = Render(Spec(new SKColor(200, 100, 40), brightness: 0.25f), 0);

        Assert.All(leds, led => Assert.Equal(new SKColor(50, 25, 10, 255), led));
    }

    [Fact]
    public void A_translucent_color_dims_the_ring()
    {
        var leds = Render(Spec(new SKColor(255, 0, 0, 0x80)), 0);

        Assert.All(leds, led => Assert.Equal(new SKColor(128, 0, 0), led));
    }

    [Fact]
    public void Brightness_zero_is_off()
    {
        Assert.All(Render(Spec(SKColors.White, RingEffect.Rainbow, brightness: 0), 1), led => Assert.Equal(SKColors.Black, led));
    }

    [Theory]
    [InlineData(1.0, 0.0, 255)]    // a breath starts at full brightness
    [InlineData(1.0, 1.5, 51)]     // and is at 20% halfway through its 3 s
    [InlineData(1.0, 3.0, 255)]    // and back at the end
    [InlineData(1.0, 0.75, 153)]   // a quarter of the way: halfway between
    [InlineData(2.0, 0.75, 51)]    // twice as fast: 1.5 s a breath
    [InlineData(0.5, 3.0, 51)]     // half as fast: 6 s a breath
    public void Breathe_eases_between_a_fifth_and_full_brightness(double speed, double seconds, int level)
    {
        var leds = Render(Spec(SKColors.White, RingEffect.Breathe, (float)speed), seconds);

        Assert.All(leds, led => Pixels.AssertNear(new SKColor((byte)level, (byte)level, (byte)level), led, tolerance: 1));
    }

    [Fact]
    public void Breathe_scales_with_brightness()
    {
        var leds = Render(Spec(SKColors.White, RingEffect.Breathe, brightness: 0.5f), 1.5);

        Assert.All(leds, led => Pixels.AssertNear(new SKColor(26, 26, 26), led, tolerance: 1));
    }

    [Theory]
    [InlineData(1.0, 0.0, 0)]
    [InlineData(1.0, 0.5, 15)]     // a turn takes 2 s, so a quarter turn
    [InlineData(1.0, 1.5, 45)]
    [InlineData(1.0, 2.0, 0)]      // a full turn
    [InlineData(2.0, 1.5, 30)]     // twice as fast: 1 s a turn
    [InlineData(0.5, 1.0, 15)]     // half as fast: 4 s a turn
    public void Spin_moves_the_brightest_led_round_the_ring(double speed, double seconds, int brightest)
    {
        var leds = Render(Spec(SKColors.Red, RingEffect.Spin, (float)speed), seconds);

        var index = Array.IndexOf(leds, leds.MaxBy(led => led.Red));
        Assert.Equal(brightest, index);
        Assert.Equal(SKColors.Red, leds[index]);
    }

    [Fact]
    public void Spin_leaves_a_fading_tail_and_the_rest_of_the_ring_dark()
    {
        var leds = Render(Spec(SKColors.Red, RingEffect.Spin), 0.5);

        Assert.True(leds[14].Red > leds[10].Red, $"{leds[14]} vs {leds[10]}");
        Assert.True(leds[10].Red > leds[5].Red, $"{leds[10]} vs {leds[5]}");
        Assert.True(leds[5].Red > 0);
        Assert.Equal(SKColors.Black, leds[45]);
        Assert.Equal(SKColors.Black, leds[16]);
        Assert.All(leds, led => Assert.Equal(0, led.Green + led.Blue));
    }

    [Fact]
    public void Spin_wraps_its_tail_past_led_zero()
    {
        var leds = Render(Spec(SKColors.Red, RingEffect.Spin), 0.05); // the head at LED 1.5

        Assert.True(leds[59].Red > leds[50].Red, $"{leds[59]} vs {leds[50]}");
        Assert.True(leds[50].Red > 0);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0f, 180f)]
    [InlineData(1.0, 1.5, 270f, 90f)]  // a turn takes 6 s, so the colors have moved a quarter turn on
    [InlineData(1.0, 6.0, 0f, 180f)]
    [InlineData(2.0, 1.5, 180f, 0f)]   // twice as fast: half a turn
    public void Rainbow_spreads_the_hues_round_the_ring_and_turns_them(double speed, double seconds, float hue0, float hue30)
    {
        var leds = Render(Spec(SKColors.Blue, RingEffect.Rainbow, (float)speed), seconds);

        AssertHue(hue0, leds[0]);
        AssertHue(hue30, leds[30]);
        AssertHue((hue0 + 90) % 360, leds[15]);
    }

    [Fact]
    public void Rainbow_ignores_the_color_and_uses_full_saturation()
    {
        var leds = Render(Spec(SKColors.Blue, RingEffect.Rainbow), 0);

        Assert.Equal(SKColors.Red, leds[0]);
        Assert.Equal(SKColors.Cyan, leds[30]);
    }

    [Fact]
    public void Rendering_is_the_same_for_the_same_time()
    {
        foreach (var effect in Enum.GetValues<RingEffect>())
            Assert.Equal(Render(Spec(SKColors.Orange, effect), 1.234), Render(Spec(SKColors.Orange, effect), 1.234));
    }

    [Fact]
    public void Leds_of_another_length_are_filled_round_that_ring()
    {
        var leds = new SKColor[12];

        RingRenderer.Render(Spec(SKColors.Blue, RingEffect.Rainbow), NoSensors, TimeSpan.Zero, leds);

        AssertHue(0, leds[0]);
        AssertHue(180, leds[6]);
    }

    [Fact]
    public void A_matching_rule_changes_what_the_ring_shows()
    {
        var spec = Spec(SKColors.Blue, RingEffect.Breathe, source: "cpu.temp",
            when: Above(85, SKColors.Red, RingEffect.Static, brightness: 0.5f));

        Assert.All(Render(spec, 0, TestSensors.Of(("cpu.temp", 90))), led => Assert.Equal(new SKColor(128, 0, 0), led));
        Assert.All(Render(spec, 0, TestSensors.Of(("cpu.temp", 80))), led => Assert.Equal(SKColors.Blue, led));
    }

    [Theory]
    [InlineData(WhenCondition.Above, 85.0, null, 90.0, true)]
    [InlineData(WhenCondition.Above, 85.0, null, 85.0, false)]
    [InlineData(WhenCondition.Below, 30.0, null, 20.0, true)]
    [InlineData(WhenCondition.Below, 30.0, null, 40.0, false)]
    [InlineData(WhenCondition.Equals, 2.0, "2", 2.0, true)]
    [InlineData(WhenCondition.Equals, 2.0, "2", 3.0, false)]
    public void Numeric_conditions_compare_the_source(WhenCondition condition, double number, string? text, double value, bool matches)
    {
        var spec = Spec(SKColors.Blue, source: "x", when: new RingRule(condition, number, text, SKColors.Red, null, null, null));

        var effective = RingRenderer.Effective(spec, TestSensors.Of(("x", value)));

        Assert.Equal(matches ? SKColors.Red : SKColors.Blue, effective.Color);
    }

    [Fact]
    public void Equals_compares_text_values()
    {
        var spec = Spec(SKColors.Blue, source: "game.state",
            when: new RingRule(WhenCondition.Equals, double.NaN, "idle", SKColors.Gray, null, null, null));

        Assert.Equal(SKColors.Gray, RingRenderer.Effective(spec, TestSensors.Of(("game.state", "idle"))).Color);
        Assert.Equal(SKColors.Blue, RingRenderer.Effective(spec, TestSensors.Of(("game.state", "busy"))).Color);
    }

    [Fact]
    public void Stale_matches_only_when_the_source_has_no_value()
    {
        var spec = Spec(SKColors.Blue, source: "cpu.temp", when:
        [
            new RingRule(WhenCondition.Stale, double.NaN, null, SKColors.Gray, null, null, null),
            Above(0, SKColors.Red),
        ]);

        Assert.Equal(SKColors.Gray, RingRenderer.Effective(spec, NoSensors).Color);
        Assert.Equal(SKColors.Red, RingRenderer.Effective(spec, TestSensors.Of(("cpu.temp", 50))).Color);
    }

    [Fact]
    public void Later_rules_win_field_by_field()
    {
        var spec = Spec(SKColors.Blue, RingEffect.Breathe, 1, 1, "cpu.temp",
            Above(50, SKColors.Yellow, RingEffect.Spin, speed: 2),
            Above(80, SKColors.Red, brightness: 0.3f),
            Above(95, effect: RingEffect.Rainbow));

        var warm = RingRenderer.Effective(spec, TestSensors.Of(("cpu.temp", 60)));
        var hot = RingRenderer.Effective(spec, TestSensors.Of(("cpu.temp", 90)));

        Assert.Equal((SKColors.Yellow, RingEffect.Spin, 2f, 1f), (warm.Color, warm.Effect, warm.Speed, warm.Brightness));
        Assert.Equal((SKColors.Red, RingEffect.Spin, 2f, 0.3f), (hot.Color, hot.Effect, hot.Speed, hot.Brightness));
    }

    [Fact]
    public void Without_a_source_no_rule_matches()
    {
        var spec = Spec(SKColors.Blue, when: new RingRule(WhenCondition.Stale, double.NaN, null, SKColors.Red, null, null, null));

        Assert.Equal(SKColors.Blue, RingRenderer.Effective(spec, NoSensors).Color);
    }
}
