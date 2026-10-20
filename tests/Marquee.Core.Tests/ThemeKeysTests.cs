using System.Runtime.CompilerServices;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;

namespace Marquee.Core.Tests;

/// Every sensor key a theme reads, which the theme editor's test values panel lists: in the order layout.yaml first
/// uses each (background, then widgets through their groups, then the ring), once each.
public class ThemeKeysTests
{
    static string ThemeFolder(string name, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "themes", name));

    static IReadOnlyList<string> Keys(string yaml) => ThemeKeys.Of(Layouts.Parse(yaml));

    [Fact]
    public void A_theme_that_reads_nothing_lists_nothing() =>
        Assert.Empty(Keys("""
            canvas: { orientation: landscape }
            background:
              - fill: "#000000"
            widgets:
              - { type: text, at: [0, 0], size: [100, 40], text: "CPU {{not a key}} {now|time:HH:mm}" }
              - { type: shape, shape: rect, at: [0, 0], size: [10, 10] }
            """));

    /// `now` isn't a key, but a theme that reads it changes each second with no key changing, so the editor redraws it.
    [Theory]
    [InlineData("{ type: text, at: [0, 0], size: [100, 40], text: \"{now|time:HH:mm:ss}\" }", true)]
    [InlineData("{ type: text, at: [0, 0], size: [100, 40], spans: [{ text: \"{cpu.temp}\" }, { text: \"{claude.resets_at - now|duration}\" }] }", true)]
    [InlineData("{ type: bar, at: [0, 0], size: [100, 40], source: \"max(0, x.until - now)\" }", true)]
    [InlineData("{ type: group, at: [0, 0], size: [100, 40], children: [{ type: value, at: [0, 0], size: [10, 10], source: cpu.load, spans: [{ text: \"{0} {now|time:ss}\" }] }] }", true)]
    [InlineData("{ type: text, at: [0, 0], size: [100, 40], text: \"{cpu.temp} {{now}}\" }", false)]
    [InlineData("{ type: bar, at: [0, 0], size: [100, 40], source: nowhere }", false)]
    public void Reading_the_clock_is_noticed_wherever_it_is_read(string widget, bool readsClock)
    {
        var spec = Layouts.Parse($"canvas: {{ orientation: landscape }}\nwidgets:\n  - {widget}\n");
        Assert.Equal(readsClock, ThemeKeys.ReadsClock(spec));
        Assert.DoesNotContain("now", ThemeKeys.Of(spec));
    }

    [Fact]
    public void Every_place_a_key_is_read_is_listed_in_order_of_first_use()
    {
        var keys = Keys("""
            canvas: { orientation: landscape }
            background:
              - shader: a.sksl
                uniforms:
                  heat: "max(u.one, u.two) / 100"
                  pair: [u.three, "u.one * 2"]
            widgets:
              - { type: text, at: [0, 0], size: [100, 40], text: "{text.a} and {text.b:0}", source: text.source }
              - type: text
                at: [0, 50]
                size: [100, 40]
                spans:
                  - { text: "{span.a:0}" }
                  - { text: "{span.b|bytes} °C" }
              - type: value
                at: [0, 100]
                size: [100, 40]
                source: value.source
                spans:
                  - { text: "{0:0}" }
                  - { text: " of {value.span}" }
              - type: group
                at: [200, 0]
                size: [400, 400]
                source: group.source
                children:
                  - { type: gauge, at: [0, 0], size: [100, 100], source: "gauge.source - 20", when: [{ above: 50, style: { color: "#FF0000" } }] }
                  - type: graph
                    at: [0, 100]
                    size: [200, 100]
                    series:
                      - { source: graph.one }
                      - { source: graph.two }
                  - { type: graph, at: [0, 200], size: [200, 100], source: graph.single }
              - type: donut
                at: [700, 0]
                size: [200, 200]
                slices:
                  - { source: donut.used }
                  - { source: 100 - donut.used }
                  - { source: donut.other, label: "{not.a.key}" }
              - { type: bar, at: [900, 0], size: [100, 20], source: bar.source, hidden: true }
            ring:
              source: ring.source
              when:
                - { above: 80, color: "#FF0000" }
            """);

        Assert.Equal(
            [
                "u.one", "u.two", "u.three",
                "text.source", "text.a", "text.b",
                "span.a", "span.b",
                "value.source", "value.span",
                "group.source", "gauge.source", "graph.one", "graph.two", "graph.single",
                "donut.used", "donut.other",
                "bar.source",
                "ring.source",
            ],
            keys);
    }

    [Fact]
    public void A_key_read_in_many_places_is_listed_once_where_it_is_first_read() =>
        Assert.Equal(["cpu.load", "gpu.load", "cpu.temp"], Keys("""
            canvas: { orientation: landscape }
            background:
              - shader: a.sksl
                uniforms:
                  heat: "cpu.load + gpu.load"
            widgets:
              - { type: bar, at: [0, 0], size: [100, 20], source: gpu.load }
              - { type: text, at: [0, 0], size: [100, 20], text: "{cpu.temp} {cpu.load}", source: cpu.temp }
              - { type: graph, at: [0, 0], size: [100, 20], series: [{ source: cpu.temp }, { source: gpu.load }] }
            ring: { source: cpu.load }
            """));

    [Fact]
    public void The_ring_alone_is_read()
    {
        Assert.Equal(["cpu.temp"], Keys("canvas: { orientation: landscape }\nring: { source: cpu.temp }\n"));
        Assert.Empty(Keys("canvas: { orientation: landscape }\nring: { color: \"#FF0000\" }\n"));
    }

    // The examples' lists, checked by hand against their layout.yaml.

    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    public void The_landscape_and_portrait_examples_read_the_pc_vitals(string name) =>
        Assert.Equal(
            ["cpu.temp", "cpu.load", "cpu.clock", "gpu.temp", "gpu.load", "gpu.vram_used", "ram.used_pct"],
            Of(name));

    [Fact]
    public void The_showcase_reads_the_vitals_and_the_frame_rate() =>
        Assert.Equal(
            [
                "cpu.temp", "cpu.load", "cpu.clock", "gpu.temp", "gpu.load", "gpu.vram_used", "ram.used_pct", "ram.used",
                "fps", "fps.1pct_low",
            ],
            Of("example-showcase"));

    [Fact]
    public void The_agents_example_reads_its_shader_the_vitals_and_both_agents() =>
        Assert.Equal(
            [
                "cpu.load", "gpu.load", // the aurora's heat uniform
                "custom.status", "cpu.temp", "gpu.temp", "ram.used_pct",
                "claude.sessions.busy", "claude.sessions.idle",
                "claude.five_hour.pct", "claude.five_hour.resets_at",
                "claude.seven_day.pct", "claude.seven_day.resets_at",
                "claude.seven_day_model.pct", "claude.seven_day_model.name", "claude.seven_day_model.resets_at",
                "claude.context.pct", "claude.updated_at",
                "codex.sessions.busy", "codex.sessions.idle",
                "codex.primary.window_min", "codex.primary.pct", "codex.primary.resets_at",
                "codex.secondary.window_min", "codex.secondary.resets_at", "codex.secondary.pct",
            ],
            Of("example-agents"));

    static IReadOnlyList<string> Of(string name) =>
        ThemeKeys.Of(Layouts.Parse(File.ReadAllText(Path.Combine(ThemeFolder(name), ThemeLoader.LayoutFileName))));
}
