using Marquee.Core.Sensors;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

public class RuleEditingTests
{
    const string Layout =
        "canvas: { orientation: landscape }\n" +
        "styles:\n  hot: { color: \"#FF0000\" }\n" +
        "ring: { color: \"#3A7BFF\", source: cpu.temp }\n" +
        "widgets:\n  - { type: value, at: [0, 0], size: [100, 40], source: cpu.temp } # temp\n";

    static readonly YamlPath Widget = YamlPath.Parse("widgets[0]");
    static readonly YamlPath Rule = Widget.Child("when").Item(0);
    static readonly YamlPath Ring = YamlPath.Root.Child("ring");

    [Fact]
    public void A_rule_is_added_changed_moved_and_removed_and_each_undoes()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        var states = new List<string> { session.Document.Text };
        EditorCommand[] commands = [new AddRule(Widget, "above", "80"), new AddRule(Widget, "below", "20"),
            new SetCondition(Rule, "below", "75"), new MoveRule(Widget.Child("when").Item(1), 0),
            new RemoveRule(Rule)];
        foreach (var command in commands)
        {
            session.Apply(command);
            Assert.Null(session.LoadError);
            states.Add(session.Document.Text);
        }
        Assert.Equal(new YamlText("75"), session.Document.ValueAt(Rule.Child("below")));
        Assert.Equal(new YamlText("hot"), session.Document.ValueAt(Rule.Child("style")));
        Assert.Contains("# temp", session.Document.Text);
        for (var i = commands.Length - 1; i >= 0; i--)
        {
            session.Undo();
            Assert.Equal(states[i], session.Document.Text);
        }
    }

    [Fact]
    public void A_ring_rule_changes_ring_keys_not_a_style()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Apply(new AddRule(Ring, "above", "80"));
        Assert.Contains("above: 80", session.Document.Text);
        Assert.Equal(new YamlText("#FF0000"), session.Document.ValueAt(Ring.Child("when").Item(0).Child("color")));
        Assert.False(session.Document.Exists(Ring.Child("when").Item(0).Child("style")));
        Assert.Null(session.LoadError);
    }

    [Fact]
    public void Without_named_styles_a_widget_rule_starts_with_an_empty_style()
    {
        using var theme = TempTheme.With(Layout.Replace("styles:\n  hot: { color: \"#FF0000\" }\n", ""));
        using var session = theme.Open();
        session.Apply(new AddRule(Widget, "stale", "false"));
        Assert.Empty(Assert.IsType<YamlMap>(session.Document.ValueAt(Rule.Child("style"))).Entries);
        Assert.Equal(new YamlText("true"), session.Document.ValueAt(Rule.Child("stale")));
    }

    [Fact]
    public void Changing_condition_keeps_a_number_and_stale_always_takes_true()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Apply(new AddRule(Widget, "above", "80"));
        foreach (var condition in new[] { "below", "equals", "above" })
        {
            session.Apply(new SetCondition(Rule, condition, null));
            Assert.Equal(new YamlText("80"), session.Document.ValueAt(Rule.Child(condition)));
            Assert.Equal(2, session.Document.GetNode(Rule)!.Count);
        }
        session.Apply(new SetCondition(Rule, "stale", "false"));
        Assert.Equal(new YamlText("true"), session.Document.ValueAt(Rule.Child("stale")));
        session.Apply(new SetCondition(Rule, "above", "45"));
        Assert.Equal(new YamlText("45"), session.Document.ValueAt(Rule.Child("above")));
    }

    [Theory]
    [InlineData("widgets[0]")]
    [InlineData("ring")]
    public void Which_rules_match_follows_the_values_and_staleness(string path)
    {
        using var theme = TempTheme.With(Layout.Replace("source: cpu.temp", "source: cpu.temp * 2"));
        using var session = theme.Open();
        var owner = YamlPath.Parse(path);
        session.Apply(new AddRule(owner, "above", "80"));
        session.Apply(new AddRule(owner, "stale", "true"));
        var store = new SensorStore(() => DateTimeOffset.UnixEpoch);
        Assert.Equal([false, true], RuleSummaries.Matching(session.Document, owner, store.Snapshot()));
        store.Set("cpu.temp", 45);
        Assert.Equal([true, false], RuleSummaries.Matching(session.Document, owner, store.Snapshot()));
        store.Set("cpu.temp", 35);
        Assert.Equal([false, false], RuleSummaries.Matching(session.Document, owner, store.Snapshot()));
    }

    [Fact]
    public void Rule_properties_show_only_the_set_condition_and_edit_the_rule_style_in_place()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Apply(new AddRule(Widget, "above", "80"));
        var model = PropertyModel.ForRule(session.Document, Rule);
        Assert.Equal(PropertyTargetKind.Rule, model.Target);
        Assert.Equal(["above", "style"], model.Rows.Select(row => row.Name));
        Assert.False(model.Row("above")!.CanReset);
        Assert.Contains("hot", model.Row("style")!.Choices);
        Assert.NotEmpty(model.Style);
        session.Apply(model.Set(model.Row("style.color")!, "#00FF00"));
        Assert.Equal(new YamlText("#00FF00"), session.Document.ValueAt(Rule.Child("style").Child("color")));
        Assert.False(session.Document.Exists(Widget.Child("style")));
        session.Undo();
        Assert.Equal(new YamlText("hot"), session.Document.ValueAt(Rule.Child("style")));
    }

    [Fact]
    public void Ring_properties_have_defaults_and_ring_rules_have_only_overrides()
    {
        using var theme = TempTheme.With("canvas: { orientation: landscape }\nwidgets: []\n");
        using var session = theme.Open();
        var model = PropertyModel.ForRing(session.Document);
        Assert.Equal(PropertyTargetKind.Ring, model.Target);
        Assert.Equal(["#FFFFFF", "static", "1", "100"],
            new[] { "color", "effect", "speed", "brightness" }.Select(name => model.Row(name)!.Default));
        session.Apply(model.Set(model.Row("color")!, "#123456"));
        Assert.Equal(new YamlText("#123456"), session.Document.ValueAt(Ring.Child("color")));
        model = PropertyModel.ForRing(session.Document);
        session.Apply(model.Set(model.Row("source")!, "cpu.temp"));
        session.Apply(new AddRule(Ring, "below", "20"));
        var rule = PropertyModel.ForRule(session.Document, Ring.Child("when").Item(0));
        Assert.Equal(["below", "color", "effect", "speed", "brightness"], rule.Rows.Select(row => row.Name));
        Assert.Empty(rule.Style);
        Assert.All(rule.Rows, row => Assert.Null(row.Default));
        session.Apply(rule.Set(rule.Row("brightness")!, "50"));
        Assert.Equal(new YamlText("50"), session.Document.ValueAt(Ring.Child("when").Item(0).Child("brightness")));
    }

    [Fact]
    public void Resetting_the_last_inline_rule_style_key_keeps_the_required_empty_style()
    {
        using var theme = TempTheme.With(Layout.Replace("styles:\n  hot: { color: \"#FF0000\" }\n", ""));
        using var session = theme.Open();
        session.Apply(new AddRule(Widget, "above", "80"));
        var model = PropertyModel.ForRule(session.Document, Rule);
        session.Apply(model.Set(model.Row("style.color")!, "#123456"));
        model = PropertyModel.ForRule(session.Document, Rule);
        session.Apply(model.Reset(model.Row("style.color")!));
        Assert.Empty(Assert.IsType<YamlMap>(session.Document.ValueAt(Rule.Child("style"))).Entries);
    }

    [Theory]
    [InlineData("text: '{cpu.temp}'")]
    [InlineData("spans: [{ text: '{cpu.temp}' }]")]
    public void Nested_text_rules_match_when_their_templates_are_stale(string content)
    {
        var document = LayoutDocument.Open("canvas: { orientation: landscape }\nwidgets:\n" +
            "  - type: group\n    at: [0, 0]\n    size: [100, 100]\n    children:\n" +
            "      - { type: text, at: [0, 0], size: [100, 40], " + content +
            ", when: [{ stale: true, style: {} }] }\n");
        var store = new SensorStore(() => DateTimeOffset.UnixEpoch);
        var owner = YamlPath.Parse("widgets[0].children[0]");
        Assert.Equal([true], RuleSummaries.Matching(document, owner, store.Snapshot()));
        store.Set("cpu.temp", 45);
        Assert.Equal([false], RuleSummaries.Matching(document, owner, store.Snapshot()));
    }

    [Fact]
    public void A_text_equals_condition_is_written_as_one_scalar_and_matches()
    {
        using var theme = TempTheme.With(Layout);
        using var session = theme.Open();
        session.Apply(new AddRule(Widget, "equals", "idle: waiting, #ready"));
        var store = new SensorStore(() => DateTimeOffset.UnixEpoch);
        store.Set("cpu.temp", "idle: waiting, #ready");
        Assert.Equal([true], RuleSummaries.Matching(session.Document, Widget, store.Snapshot()));
        session.Apply(new SetCondition(Rule, "equals", "busy"));
        Assert.Equal([false], RuleSummaries.Matching(session.Document, Widget, store.Snapshot()));
    }
}
