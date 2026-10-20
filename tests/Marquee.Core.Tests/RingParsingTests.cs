using Marquee.Core.Sensors;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Themes;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The `ring:` block in layout.yaml: the LED ring's color, effect, speed, brightness and `when` rules.
public class RingParsingTests
{
    const string Canvas = "canvas: { orientation: landscape }\n";

    static RingSpec Ring(string ring) => Layouts.Parse(Canvas + ring).Ring!;

    /// The error for a layout whose `ring:` block starts on line 2.
    static ThemeException Error(string ring) => Layouts.Error(Canvas + ring);

    [Fact]
    public void A_layout_without_a_ring_block_leaves_the_ring_to_the_engine()
    {
        Assert.Null(Layouts.Parse(Canvas).Ring);
    }

    [Fact]
    public void Every_field_is_read()
    {
        var ring = Ring("""
            ring:
              color: "#3A7BFF"
              effect: breathe
              speed: 1.5
              brightness: 80
              source: cpu.temp
              when:
                - { above: 85, color: "#FF3A2E", effect: static }
                - { below: 30, speed: 2, brightness: 50 }
                - { equals: idle, color: "#00FF00" }
                - { stale: true, effect: spin }
            """);

        Assert.Equal(new SKColor(0x3A, 0x7B, 0xFF), ring.Color);
        Assert.Equal(RingEffect.Breathe, ring.Effect);
        Assert.Equal(1.5f, ring.Speed);
        Assert.Equal(0.8f, ring.Brightness);
        Assert.Equal(SensorValue.Of(90), ring.Source!.Evaluate(TestSensors.Of(("cpu.temp", 90))));

        Assert.Equal(
            [WhenCondition.Above, WhenCondition.Below, WhenCondition.Equals, WhenCondition.Stale],
            ring.When.Select(r => r.Condition));
        Assert.Equal(new RingRule(WhenCondition.Above, 85, null, new SKColor(0xFF, 0x3A, 0x2E), RingEffect.Static, null, null), ring.When[0]);
        Assert.Equal(new RingRule(WhenCondition.Below, 30, null, null, null, 2f, 0.5f), ring.When[1]);
        Assert.Equal("idle", ring.When[2].Text);
        Assert.Equal(SKColors.Lime, ring.When[2].Color);
        Assert.Equal(RingEffect.Spin, ring.When[3].Effect);
    }

    [Theory]
    [InlineData("static", RingEffect.Static)]
    [InlineData("breathe", RingEffect.Breathe)]
    [InlineData("spin", RingEffect.Spin)]
    [InlineData("rainbow", RingEffect.Rainbow)]
    public void Each_effect_has_a_name(string name, RingEffect effect) =>
        Assert.Equal(effect, Ring($"ring: {{ effect: {name} }}").Effect);

    [Fact]
    public void What_a_ring_block_leaves_out_is_static_white_at_full_brightness()
    {
        var ring = Ring("ring: { effect: spin }");

        Assert.Equal(new RingSpec(SKColors.White, RingEffect.Spin, 1, 1, null, ring.When), ring);
        Assert.Empty(ring.When);
        Assert.Null(ring.Source);
    }

    /// A theme author who writes a color expects to see it, not the default ring's 40% glow.
    [Fact]
    public void A_ring_block_with_only_a_color_shows_it_at_full_brightness()
    {
        var ring = Ring("ring: { color: \"#3A7BFF\" }");

        Assert.Equal(new RingSpec(new SKColor(0x3A, 0x7B, 0xFF), RingEffect.Static, 1, 1, null, ring.When), ring);
    }

    [Fact]
    public void An_empty_ring_block_is_static_white_at_full_brightness_not_the_default_ring()
    {
        var ring = Ring("ring: {}");

        Assert.Equal(new RingSpec(SKColors.White, RingEffect.Static, 1, 1, null, ring.When), ring);
        Assert.NotEqual(RingSpec.Default.Brightness, ring.Brightness);
    }

    [Theory]
    [InlineData("ring: blue", 2, "ring must be a mapping of keys to values")]
    [InlineData("ring: { colour: \"#FF0000\" }", 2, "Unknown key 'colour' in ring. Expected one of: color, effect, speed, brightness, source, when")]
    [InlineData("ring:\n  effect: strobe", 3, "ring.effect must be one of: static, breathe, spin, rainbow; not 'strobe'")]
    [InlineData("ring:\n  color: blue", 3, "ring.color: 'blue' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("ring:\n  color: #FF0000", 3, "ring.color is empty. Quote colors, e.g. \"#FF0000\": an unquoted # starts a YAML comment")]
    [InlineData("ring:\n  speed: 0", 3, "ring.speed must be between 0.1 and 10")]
    [InlineData("ring:\n  speed: 11", 3, "ring.speed must be between 0.1 and 10")]
    [InlineData("ring:\n  speed: fast", 3, "ring.speed must be a number, not 'fast'")]
    [InlineData("ring:\n  brightness: 120", 3, "ring.brightness must be between 0 and 100")]
    [InlineData("ring:\n  brightness: -1", 3, "ring.brightness must be between 0 and 100")]
    [InlineData("ring:\n  when: { above: 85 }", 3, "ring.when must be a list")]
    public void Errors_name_the_line_and_the_problem(string ring, int line, string reason)
    {
        var ex = Error(ring);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(line, ex.Line);
    }

    [Fact]
    public void A_bad_source_is_an_error_on_its_line()
    {
        var ex = Error("ring:\n  color: \"#FF0000\"\n  source: cpu.temp +");

        Assert.StartsWith("source: ", ex.Reason);
        Assert.Equal(4, ex.Line);
    }

    [Theory]
    [InlineData("- { above: 85 }", "ring when rule needs at least one of: color, effect, speed, brightness")]
    [InlineData("- { color: \"#FF0000\" }", "ring when rule needs one of: above, below, equals, stale")]
    [InlineData("- { above: 85, below: 10, color: \"#FF0000\" }", "ring when rule can only have one of: above, below")]
    [InlineData("- { above: 85, style: hot }", "Unknown key 'style' in ring when rule. Expected one of: above, below, equals, stale, color, effect, speed, brightness")]
    [InlineData("- { above: warm, color: \"#FF0000\" }", "above must be a number, not 'warm'")]
    [InlineData("- { stale: false, color: \"#FF0000\" }", "stale only takes true; remove the rule instead of setting it to false")]
    [InlineData("- { above: 85, color: nope }", "ring.when.color: 'nope' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"")]
    [InlineData("- { above: 85, effect: strobe }", "ring.when.effect must be one of: static, breathe, spin, rainbow; not 'strobe'")]
    [InlineData("- { above: 85, speed: 20 }", "ring.when.speed must be between 0.1 and 10")]
    [InlineData("- { above: 85, brightness: 101 }", "ring.when.brightness must be between 0 and 100")]
    public void When_rule_errors(string rule, string reason)
    {
        var ex = Error($"""
            ring:
              source: cpu.temp
              when:
                {rule}
            """);

        Assert.Equal(reason, ex.Reason);
        Assert.Equal(5, ex.Line);
    }

    [Theory]
    [InlineData("above: 85")]
    [InlineData("stale: true")]
    public void Rules_need_a_source_to_watch(string condition)
    {
        var ex = Error($$"""
            ring:
              when:
                - { {{condition}}, color: "#FF0000" }
            """);

        Assert.Equal(condition.StartsWith("stale")
            ? "when 'stale' watches the ring's source, but this ring has no source"
            : "when 'above' compares the ring's source, but this ring has no source", ex.Reason);
        Assert.Equal(4, ex.Line);
    }

    [Fact]
    public void Collecting_reports_every_ring_error_and_still_checks_the_rest()
    {
        var (spec, errors) = Layouts.Collect("""
            canvas: { orientation: landscape }
            ring:
              color: "#GG0000"
              effect: strobe
              colour: red
              source: cpu.temp
              when:
                - { above: 85 }
                - { below: 10, color: nope }
                - { equals: idle, effect: spin }
            widgets:
              - { type: value, at: [0, 0], size: [10, 10] }
            """);

        Assert.Equal(
            [
                "3: ring.color: '#GG0000' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"",
                "4: ring.effect must be one of: static, breathe, spin, rainbow; not 'strobe'",
                "5: Unknown key 'colour' in ring. Expected one of: color, effect, speed, brightness, source, when",
                "8: ring when rule needs at least one of: color, effect, speed, brightness",
                "9: ring.when.color: 'nope' is not a color. Use \"#RRGGBB\", \"#RRGGBBAA\" or \"transparent\"",
                "12: value widget is missing 'source'",
            ],
            Layouts.Lines(errors));

        // The partial ring keeps the valid rule.
        Assert.Equal(RingEffect.Spin, Assert.Single(spec.Ring!.When).Effect);
    }

    [Fact]
    public void A_valid_ring_collects_to_the_same_spec_as_Parse()
    {
        const string yaml = Canvas + """
            ring:
              color: "#3A7BFF"
              effect: rainbow
              speed: 0.5
              brightness: 60
              source: gpu.load
              when:
                - { above: 90, color: "#FF0000", effect: static }
            """;

        var (spec, errors) = Layouts.Collect(yaml);

        Assert.Empty(errors);
        Structural.Equal(Layouts.Parse(yaml), spec);
    }
}
