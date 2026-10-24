using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// A widget's `when` rules, read-only in plain words; editing anything else never alters them.
public class RuleSummaryTests
{
    [Fact]
    public void Rule_summaries_name_the_reading()
    {
        using var folder = TempTheme.With("canvas: { orientation: landscape }\nwidgets:\n  - { type: bar, at: [0, 0], size: [100, 20], source: claude.seven_day.pct, when: [{ above: 80, style: { color: '#FF0000' } }] }\n");
        using var session = folder.Open();
        Assert.Equal("Claude Code: weekly limit used: turns red above 80", Assert.Single(RuleSummaries.Of(session.Document, YamlPath.Parse("widgets[0]"))).Text);
    }

    [Fact]
    public void Rules_read_in_plain_words()
    {
        const string layout =
            "canvas: { orientation: landscape }\n" +
            "styles:\n" +
            "  hot: { color: \"#FF5A36\", glow: { color: \"#FF5A36\", radius: 18 }, pulse: { property: glow } }\n" +
            "  stale: { color: \"#5A6275\" }\n" +
            "widgets:\n" +
            "  - type: value\n" +
            "    at: [10, 10]\n" +
            "    size: [200, 40]\n" +
            "    source: cpu.temp\n" +
            "    when:\n" +
            "      - { above: 85, style: { color: \"#FF0000\" } }\n" +
            "      - { above: 95, style: hot }\n" +
            "      - { below: 20.5, style: { fill: { linear: [\"#000000\", \"#0000FF\"] }, opacity: 0.4 } }\n" +
            "      - { equals: 300, style: { opacity: 1 } }\n" +
            "      - { equals: idle, style: { weight: bold, size: 30 } }\n" +
            "      - { stale: true, style: { opacity: 0 } }\n" +
            "      - { stale: true, style: stale }\n" +
            "      - { below: 0, style: { letter_spacing: 2, stroke: { width: 2 } } }\n";
        using var folder = TempTheme.With(layout);
        using var session = folder.Open();
        session.Select([YamlPath.Parse("widgets[0]")]);

        var rules = PropertyModel.For(session).Rules;

        Assert.Equal(
        new[] {
            "turns red above 85",
            "turns red, glows red and pulses above 95",
            "fills with a gradient and fades to 40% below 20.5",
            "shows at 300",
            "sizes its text to 30 px and turns bold when it is idle",
            "hides while stale",
            "turns grey while stale",
            "changes its stroke and changes its letter spacing below 0",
        }.Select(effect => "CPU temperature: " + effect), rules.Select(r => r.Text));
        Assert.Equal("hot", rules[1].StyleName);
        Assert.Null(rules[0].StyleName);
        Assert.Equal(YamlPath.Parse("widgets[0].when[6]"), rules[6].Path);
    }

    [Fact]
    public void Every_rule_in_the_examples_has_a_summary_and_edits_leave_them_alone()
    {
        foreach (var example in TempTheme.Examples)
        {
            using var folder = TempTheme.Example(example);
            using var session = folder.Open();
            foreach (var widget in session.Widgets.SelectMany(w => w.Children.Prepend(w)).Where(w => session.Document.Exists(w.Path.Child("when"))))
            {
                var model = PropertyModel.Build(session.Document, [widget]);
                Assert.NotEmpty(model.Rules);
                Assert.All(model.Rules, rule => Assert.DoesNotContain("can't", rule.Text));

                var rules = YamlWriter.Flow(session.Document.ValueAt(widget.Path.Child("when"))!);
                session.Select([widget.Path]);
                var style = model.Style.SelectMany(s => s.Rows).First(r => r.Kind == Core.Themes.Schema.SchemaKind.Number && r.IsEditable);
                session.Apply(model.Set(style, "1"));
                Assert.Equal(rules, YamlWriter.Flow(session.Document.ValueAt(widget.Path.Child("when"))!));
            }
        }
    }

    [Fact]
    public void Colors_have_plain_names()
    {
        Assert.Equal("red", ColorNames.Of("#FF5A36"));
        Assert.Equal("grey", ColorNames.Of("#5A6275"));
        Assert.Equal("cyan", ColorNames.Of("#36D1DC"));
        Assert.Equal("blue", ColorNames.Of("#5B86E5"));
        Assert.Equal("white", ColorNames.Of("#FFF"));
        Assert.Equal("black", ColorNames.Of("#05070D"));
        Assert.Equal("transparent", ColorNames.Of("#36D1DC00"));
        Assert.Equal("transparent", ColorNames.Of("transparent"));
        Assert.Equal("orange", ColorNames.Of("#F2A65A"));
        Assert.Equal("purple", ColorNames.Of("#9F7AEA"));
        Assert.Equal("green", ColorNames.Of("#00C060"));
        Assert.Equal("not a color", ColorNames.Of("not a color"));
    }
}
