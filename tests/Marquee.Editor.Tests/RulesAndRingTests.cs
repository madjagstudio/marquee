using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marquee.Editor.Controls;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Properties;
using Marquee.Editor.Resources;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class RulesAndRingTests
{
    const string Layout = "canvas: { orientation: landscape }\nstyles: { hot: { color: '#FF0000' } }\n" +
        "widgets:\n  - { type: value, at: [0, 0], size: [200, 60], source: cpu.temp }\n";

    [Fact]
    public void Added_rule_waits_for_style_then_matches_pinned_values_and_undoes() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var rules = editor.Model.Properties.RulesList!;
        rules.AddCommand.Execute(null);
        var row = Assert.Single(rules.Rows);
        Assert.Null(row.SelectedStyle);
        Assert.False(row.IsCustom);
        Assert.Equal(Layout, editor.Session.Document.Text);
        row.Value = "80";
        Assert.True(row.CommitCondition());
        row.StyleChoice = "hot";
        Assert.Equal(1, editor.Session.History.Count);
        editor.Workspace.Values.TestValues.Pin("cpu.temp", TestPin.Of(90));
        Sta.Pump();
        Assert.True(Assert.Single(rules.Rows).IsMatching);
        editor.Workspace.Values.TestValues.Pin("cpu.temp", TestPin.Of(70));
        Sta.Pump();
        Assert.False(Assert.Single(rules.Rows).IsMatching);
        editor.Session.Undo();
        Assert.Empty(rules.Rows);
        Assert.Equal(Layout, editor.Session.Document.Text);
    });

    [Fact]
    public void Custom_style_conditions_reorder_and_delete_write_to_the_rule_only() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var rules = editor.Model.Properties.RulesList!;
        rules.AddCommand.Execute(null);
        var first = rules.Rows[0];
        first.SelectedStyle = first.StyleOptions.Single(option => option.Name is null);
        Assert.True(first.IsCustom);
        var color = first.Properties.Row("style.color")!;
        color.Text = "#00FF00";
        Assert.True(color.Commit(), color.Error ?? editor.Model.Message);
        Assert.Equal("#00FF00", Scalar(editor, "widgets[0].when[0].style.color"));
        Assert.False(editor.Session.Document.Exists(OpenEditor.P("widgets[0].style")));
        first.Condition = "equals";
        first.Value = "ready";
        Assert.True(first.CommitCondition());
        first.Condition = "above";
        first.Value = "bad";
        Assert.False(first.CommitCondition());
        Assert.NotNull(first.Error);
        first.Value = "80";
        Assert.True(first.CommitCondition());
        rules.AddCommand.Execute(null);
        rules.Rows[1].Condition = "stale";
        rules.Rows[1].StyleChoice = "hot";
        rules.Move(rules.Rows[1], 0);
        Assert.Equal("true", Scalar(editor, "widgets[0].when[0].stale"));
        rules.Rows[0].DeleteCommand.Execute(null);
        Assert.Single(rules.Rows);
        Assert.Equal("80", Scalar(editor, "widgets[0].when[0].above"));
    });

    [Fact]
    public void Ring_has_defaults_picker_rules_and_the_renderers_colors() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        File.WriteAllText(environment.ConfigFile, "ring: off\n");
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        var ring = Assert.IsType<RingViewModel>(editor.Model.Properties.Ring);
        Assert.True(ring.IsOverridden);
        Assert.Equal("static", ring.Properties.Row("effect")!.Choice);
        var picker = ring.Properties.Row("source")!.Picker!;
        picker.Mode = ValuePickerMode.Sensor;
        picker.SelectedKey = "cpu.temp";
        ring.Rules.AddCommand.Execute(null);
        var rule = Assert.Single(ring.Rules.Rows);
        Assert.False(editor.Session.Document.Exists(OpenEditor.P("ring.when")));
        rule.Value = "80";
        rule.CommitCondition();
        var color = rule.Properties.Row("color")!;
        color.Text = "#FF0000";
        Assert.True(color.Commit(), color.Error ?? editor.Model.Message);
        editor.Workspace.Values.TestValues.Pin("cpu.temp", TestPin.Of(90));
        Sta.Pump();
        Assert.True(ring.Rules.Rows[0].IsMatching);
        var elapsed = TimeSpan.FromSeconds(1.25);
        var strip = new RingStrip { DataContext = ring };
        strip.RenderAt(elapsed);
        Assert.Equal(new RingPreview(editor.Session.Document).Colors(editor.Workspace.Values.Snapshot(), elapsed), strip.Colors);
        Assert.Equal(60, strip.Colors.Count);
        Assert.All(strip.Colors, value => Assert.Equal(SkiaSharp.SKColors.Red, value));
    });

    [Fact]
    public void An_anchored_rule_list_cannot_add_a_draft() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var layout = Layout.Replace("source: cpu.temp", "source: cpu.temp, when: &rules [{ above: 80, style: hot }]");
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        Assert.False(editor.Model.Properties.RulesList!.AddCommand.CanExecute(null));
    });

    [Fact]
    public void Refused_condition_edits_restore_the_document_values_and_notify_bindings() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var layout = Layout.Replace("source: cpu.temp", "source: cpu.temp, when: &rules [{ above: 80, style: hot }]");
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var row = Assert.Single(editor.Model.Properties.RulesList!.Rows);
        var notifications = new List<string?>();
        row.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        row.Value = "90";
        notifications.Clear();
        Assert.False(row.CommitCondition());
        Assert.Equal("80", row.Value);
        Assert.Contains(nameof(row.Value), notifications);
        row.Condition = "stale";
        Assert.Equal("above", row.Condition);
        Assert.Equal("80", row.Value);
        Assert.True(row.NeedsValue);
        Assert.Contains(nameof(row.Condition), notifications);
        Assert.Contains(nameof(row.NeedsValue), notifications);
        Assert.Null(row.Error);
        Assert.Equal(layout, editor.Session.Document.Text);
        Assert.Equal(0, editor.Session.History.Count);
        Assert.NotNull(editor.Model.Message);
    });

    [Fact]
    public void Missing_widget_owner_cannot_add_a_draft() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var rules = editor.Model.Properties.RulesList!;
        editor.Session.Apply(new ReplaceDocument("canvas: { orientation: landscape }\nwidgets: []\n", "Hand edit"));
        Assert.False(rules.AddCommand.CanExecute(null));
        Assert.False(editor.Session.Document.Exists(OpenEditor.P("ring")));
    });

    [Fact]
    public void Dropping_a_rule_onto_itself_does_not_add_undo_history() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var layout = Layout.Replace("source: cpu.temp", "source: cpu.temp, when: [{ above: 80, style: hot }]");
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var rules = editor.Model.Properties.RulesList!;
        rules.Move(Assert.Single(rules.Rows), 0);
        Assert.Equal(0, editor.Session.History.Count);
        Assert.Equal(layout, editor.Session.Document.Text);
    });

    [Fact]
    public void Anchored_rule_style_refuses_a_choice_without_throwing_or_changing_yaml() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var layout = Layout.Replace("source: cpu.temp", "source: cpu.temp, when: [{ above: 80, style: &inline { color: '#00FF00' } }]");
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        editor.Model.Properties.RulesList!.Rows[0].StyleChoice = "hot";
        Assert.Equal(layout, editor.Session.Document.Text);
        Assert.NotNull(editor.Model.Message);
    });

    [Fact]
    public void A_named_style_called_Custom_can_be_picked_separately_from_inline_style() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout.Replace("hot:", "Custom:")));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var rules = editor.Model.Properties.RulesList!;
        rules.AddCommand.Execute(null);
        var row = rules.Rows[0];
        row.StyleChoice = "Custom";
        Assert.Equal("Custom", Scalar(editor, "widgets[0].when[0].style"));
        Assert.False(row.IsCustom);
        row.SelectedStyle = row.StyleOptions.Single(option => option.Name is null);
        Assert.True(row.IsCustom);
        Assert.Null(Scalar(editor, "widgets[0].when[0].style"));
    });

    [Fact]
    public void Multiple_drafts_keep_file_order_and_do_not_write_unfinished_rows() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var rules = editor.Model.Properties.RulesList!;
        rules.AddCommand.Execute(null);
        rules.AddCommand.Execute(null);
        var unfinished = rules.Rows[0];
        rules.Rows[1].StyleChoice = "hot";
        Assert.False(rules.Rows[0].IsDraft);
        Assert.Same(unfinished, rules.Rows[1]);
        Assert.Equal(1, editor.Session.Document.GetNode(OpenEditor.P("widgets[0].when"))!.Count);
        unfinished.DeleteCommand.Execute(null);
        Assert.Single(rules.Rows);
        Assert.Equal(1, editor.Session.History.Count);
    });

    [Fact]
    public void Broken_hand_edit_pauses_ring_without_throwing_and_valid_reload_recovers() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout + "ring: { effect: rainbow }\n"));
        var strip = new RingStrip { DataContext = editor.Model.Properties.Ring };
        strip.RenderAt(TimeSpan.Zero);
        Assert.Contains(strip.Colors, color => color.Alpha != 0);
        editor.Session.Apply(new ReplaceDocument(Layout + "ring: { effect: wrong }\n", "Hand edit"));
        Assert.True(editor.Session.IsPaused);
        strip.RenderAt(TimeSpan.FromSeconds(1));
        Assert.All(strip.Colors, color => Assert.Equal(default, color));
        strip.RenderAt(TimeSpan.FromSeconds(2));
        Assert.All(strip.Colors, color => Assert.Equal(default, color));
        Assert.False(editor.Model.Properties.Ring!.Rules.AddCommand.CanExecute(null));
        editor.Session.Apply(new ReplaceDocument(Layout + "ring: { color: '#00FF00' }\n", "Hand edit"));
        Assert.False(editor.Session.IsPaused);
        Assert.Equal("#00FF00", editor.Model.Properties.Ring!.Properties.Row("color")!.Text);
        strip.RenderAt(TimeSpan.FromSeconds(3));
        Assert.All(strip.Colors, color => Assert.Equal(SkiaSharp.SKColors.Lime, color));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        editor.Session.Apply(new ReplaceDocument(Layout.Replace("source: cpu.temp", "source: cpu.temp, when: [{ style: hot }]"), "Hand edit"));
        Assert.True(editor.Session.IsPaused);
        Assert.False(editor.Model.Properties.RulesList!.AddCommand.CanExecute(null));
    });

    [Fact]
    public void Ring_controls_stay_current_after_adding_rules_and_undoing_fields() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout + "ring: { source: cpu.temp }\n"));
        var ring = editor.Model.Properties.Ring!;
        ring.Rules.AddCommand.Execute(null);
        var field = ring.Rules.Rows[0].Properties.Row("color")!;
        field.Text = "#FF0000";
        Assert.True(field.Commit());
        var speed = ring.Rows.Single(row => row.Name == "speed");
        speed.Text = "2";
        Assert.True(speed.Commit());
        Assert.Equal("2", ring.Rows.Single(row => row.Name == "speed").Text);
        editor.Session.Undo();
        Assert.Equal("1", ring.Rows.Single(row => row.Name == "speed").Text);
    });

    [Fact]
    public void Window_renders_rules_and_ring_offscreen_and_stops_hidden_or_minimized_strip() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        var window = new MainWindow(editor.Model)
        {
            Left = -20000, Top = -20000, Width = 1400, Height = 1100,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            Sta.Pump();
            var strip = Descendants(window).OfType<RingStrip>().Single();
            Assert.True(strip.IsVisible);
            Assert.True(strip.IsAnimating);
            var section = Descendants(window).OfType<Expander>().Single(expander => Equals(expander.Header, Strings.RingSection));
            section.IsExpanded = false;
            Sta.Pump();
            Assert.False(strip.IsAnimating);
            section.IsExpanded = true;
            Sta.Pump();
            Assert.True(strip.IsAnimating);
            window.WindowState = WindowState.Minimized;
            Sta.Pump();
            Assert.False(strip.IsAnimating);
            window.WindowState = WindowState.Normal;
            Sta.Pump();
            Assert.True(strip.IsAnimating);
            editor.Session.Select([OpenEditor.P("widgets[0]")]);
            editor.Model.Properties.RulesList!.AddCommand.Execute(null);
            Sta.Pump();
            Assert.False(strip.IsAnimating);
            Assert.Contains(Descendants(window).OfType<RulesView>(), view => view.IsVisible);
            Assert.Contains(Descendants(window).OfType<Button>(), button => button.IsVisible && Equals(button.Content, Strings.AddRule));
        }
        finally { window.Close(); }
    });

    static string? Scalar(OpenEditor editor, string path) => editor.Session.Document.TryGetScalar(OpenEditor.P(path), out var value) ? value : null;

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
