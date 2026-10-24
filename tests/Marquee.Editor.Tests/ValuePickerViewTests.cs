using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Marquee.Editor.Controls;
using Marquee.Editor.Properties;
using Marquee.Editor.Resources;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class ValuePickerViewTests
{
    [Fact]
    public void An_unset_ring_source_opens_in_sensor_mode_without_changing_the_document() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        const string layout = "canvas: { orientation: landscape }\nring: { effect: static }\nwidgets: []\n";
        using var editor = new OpenEditor(environment, environment.AddTheme("ring", layout));
        var picker = editor.Model.Properties.Ring!.Properties.Row("source")!.Picker!;

        Assert.Equal(ValuePickerMode.Sensor, picker.Mode);
        Assert.Null(picker.SelectedKey);
        Assert.Null(picker.Problem);
        Assert.False(picker.HasProblem);
        Assert.Null(picker.Text);
        Assert.False(picker.Commit());
        Assert.Equal(layout, editor.Session.Document.Text);
        Assert.False(editor.Session.Document.Exists(OpenEditor.P("ring.source")));

        picker.Mode = ValuePickerMode.Advanced;
        picker.AdvancedText = " \t";
        Assert.Null(picker.Problem);
        Assert.False(picker.HasProblem);
        Assert.Null(picker.Text);
        Assert.False(picker.Commit());
        Assert.Equal(layout, editor.Session.Document.Text);
    });

    [Fact]
    public void Picking_cpu_load_for_heat_writes_a_ranged_expression_without_typing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = environment.AddTheme("glow", "canvas: { orientation: landscape }\nbackground:\n  - shader: shaders/aurora.sksl\n    uniforms: { heat: \"cpu.temp / 100\" }\nwidgets: []\n");
        Directory.CreateDirectory(Path.Combine(folder, "shaders"));
        File.Copy(Path.Combine(Repository(), "themes", "example-agents", "shaders", "aurora.sksl"), Path.Combine(folder, "shaders", "aurora.sksl"));
        using var editor = new OpenEditor(environment, folder);
        editor.Model.Layers.SelectBackground(0);
        var row = editor.Model.Properties.Shader!.Uniforms.Single(u => u.Name == "heat");
        var picker = row.Components[0].Picker;
        Assert.Contains(picker.Keys, key => key.Key == "cpu.load");
        Assert.DoesNotContain(picker.Keys, key => key.Key.Contains('*'));
        Assert.Equal("0 calm … 1 busy: warmer, brighter, taller bands", row.Hint);

        picker.Mode = ValuePickerMode.Sensor;
        picker.SelectedKey = "cpu.load";

        Assert.Equal("clamp(cpu.load / 100, 0, 1)", picker.Text);
        Assert.Contains("heat: \"clamp(cpu.load / 100, 0, 1)\"", editor.Session.Document.Text);
        Assert.True(editor.Session.Undo());
        Assert.Equal("cpu.temp / 100", picker.AdvancedText);
        Assert.Equal(ValuePickerMode.Advanced, picker.Mode);
        Assert.True(editor.Session.Redo());
        Assert.Equal("clamp(cpu.load / 100, 0, 1)", picker.Text);
    });

    [Fact]
    public void A_widgets_source_opens_in_sensor_mode_with_its_key_selected() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("meter",
            "canvas: { orientation: landscape }\nwidgets:\n  - { type: value, at: [0, 0], size: [200, 60], source: cpu.temp }\n"));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var picker = editor.Model.Properties.Row("source")!.Picker!;

        Assert.Equal(ValuePickerMode.Sensor, picker.Mode);
        Assert.Equal("cpu.temp", picker.SelectedKey);
        Assert.False(picker.ShowRange);
        Assert.Equal("cpu.temp", picker.Text);

        picker.SelectedKey = "cpu.load";
        Assert.True(editor.Session.Document.TryGetScalar(OpenEditor.P("widgets[0].source"), out var source));
        Assert.Equal("cpu.load", source);
        Assert.True(editor.Session.Undo());
        Assert.Equal("cpu.temp", picker.SelectedKey);
    });

    [Fact]
    public void Advanced_drafts_survive_refresh_and_only_valid_text_can_commit() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        editor.Session.Select([OpenEditor.P("widgets[1]")]);
        var picker = editor.Model.Properties.Row("source")!.Picker!;
        var before = editor.Session.Document.Text;
        picker.Mode = ValuePickerMode.Advanced;
        Assert.Equal("cpu.load", picker.AdvancedText);
        picker.AdvancedText = "cpu.load +";
        Assert.NotNull(picker.Problem);
        Assert.Null(picker.Text);
        Assert.False(picker.Commit());
        Assert.Equal(before, editor.Session.Document.Text);
        editor.Model.Properties.Row("format")!.Text = "{0:0.0}";
        editor.Model.Properties.Row("format")!.Commit();
        Assert.Equal("cpu.load +", picker.AdvancedText);
        picker.AdvancedText = "cpu.load + 10";
        Assert.Null(picker.Problem);
        Assert.True(picker.Commit());
        Assert.True(editor.Session.Document.TryGetScalar(OpenEditor.P("widgets[1].source"), out var source));
        Assert.Equal("cpu.load + 10", source);
    });

    [Fact]
    public void Sensor_search_shows_snapshot_values_and_missing_readings() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        editor.Session.Select([OpenEditor.P("widgets[1]")]);
        var picker = editor.Model.Properties.Row("source")!.Picker!;
        picker.Search = "CPU.LOAD";
        Assert.Equal("cpu.load", Assert.Single(picker.Keys).Key);
        editor.Model.TestValues.Row("cpu.load")!.Number = 73;
        Assert.True(Sta.PumpUntil(() => Assert.Single(picker.Keys).ValueText == "73 %"));
        Assert.Equal("73 %", Assert.Single(picker.Keys).ValueText);
        editor.Model.TestValues.Row("cpu.load")!.MissingCommand.Execute(null);
        Sta.Pump();
        Assert.Equal(Strings.PickerNoReading, Assert.Single(picker.Keys).ValueText);
    });

    [Fact]
    public void Ranges_and_numbers_are_validated_before_committing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        var picker = new ValuePickerViewModel(editor.Workspace.Values, "clamp(cpu.load / 100, 0, 1)", true);
        Assert.Equal(ValuePickerMode.Sensor, picker.Mode);
        Assert.Equal("100", picker.FromHigh);
        var commits = 0;
        picker.Committed += (_, _) => commits++;
        picker.SelectedKey = "cpu.temp";
        Assert.Equal(("30", "90", "0", "1"), (picker.FromLow, picker.FromHigh, picker.ToLow, picker.ToHigh));
        Assert.Equal("clamp((cpu.temp - 30) / 60, 0, 1)", picker.Text);
        Assert.Equal(1, commits);
        picker.FromHigh = "30";
        Assert.Null(picker.Text);
        Assert.False(picker.Commit());
        picker.Mode = ValuePickerMode.Number;
        picker.Number = "NaN";
        Assert.False(picker.Commit());
        picker.Number = "0.75";
        Assert.Equal("0.75", picker.Text);
        Assert.Equal(1, commits);
        Assert.True(picker.Commit());
        Assert.Equal(2, commits);
    });

    [Fact]
    public void The_control_filters_picks_and_commits_on_enter_and_focus_loss() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        editor.Session.Select([OpenEditor.P("widgets[1]")]);
        var picker = editor.Model.Properties.Row("source")!.Picker!;
        var control = new ValuePickerControl { DataContext = picker };
        var window = new Window { Content = control, Width = 340, Height = 500, Left = -20000, Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            Sta.Pump();
            var list = Descendants(control).OfType<ListBox>().Single();
            var groups = Descendants(list).OfType<Expander>().ToList();
            Assert.Equal(["CPU", "GPU", "Memory", "Gaming", "Claude Code", "Codex", "Time and date"], groups.Select(group => group.Header));
            Assert.All(groups, group => Assert.False(group.IsExpanded));
            groups[0].IsExpanded = true;
            var search = (TextBox)control.FindName("SearchBox");
            search.Text = "CPU temperature";
            Sta.Pump();
            Assert.Single(list.Items);
            Assert.Contains(Descendants(list).OfType<TextBlock>(), block => block.Text == "CPU temperature");
            Assert.DoesNotContain(Descendants(list).OfType<TextBlock>(), block => block.Text == "cpu.temp");
            Assert.Contains(Descendants(list).OfType<Expander>(), group => group.IsExpanded);
            Assert.True(list.ActualHeight >= 240);
            var missing = Assert.IsType<ValuePickerKey>(list.Items[0]);
            Assert.False(missing.HasReading);
            Assert.Equal("no reading here", missing.ValueText);
            var missingItem = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(((SolidColorBrush)control.FindResource("TextMuted")).Color, ((SolidColorBrush)missingItem.Foreground).Color);
            list.SelectedIndex = 0;
            Sta.Pump();
            Assert.Equal("cpu.temp", picker.SelectedKey);
            editor.Model.TestValues.Row("cpu.temp")!.Number = 62;
            Sta.Pump();
            Assert.Equal("cpu.temp", list.SelectedValue);
            Assert.Equal("62 °C", Assert.IsType<ValuePickerKey>(list.SelectedItem).ValueText);
            Assert.True(Assert.IsType<ValuePickerKey>(list.SelectedItem).HasReading);
            picker.Mode = ValuePickerMode.Advanced;
            Sta.Pump();
            Assert.DoesNotContain(Descendants(control).OfType<TextBox>(), box => box.IsVisible && box.Text == "cpu.temp");
            var raw = Descendants(control).OfType<Expander>().Single(group => group.Name == "RawExpression");
            Assert.False(raw.IsExpanded);
            raw.IsExpanded = true;
            Sta.Pump();
            var advanced = Descendants(control).OfType<TextBox>().Single(box => box.IsVisible);
            advanced.Text = "cpu.temp +";
            Sta.Pump();
            Assert.Equal(((SolidColorBrush)control.FindResource("Warning")).Color, ((SolidColorBrush)advanced.Foreground).Color);
            var before = editor.Session.Document.Text;
            advanced.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, advanced, search)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.Equal(before, editor.Session.Document.Text);
            advanced.Text = "cpu.temp + 5";
            advanced.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control), 0, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Contains("cpu.temp + 5", editor.Session.Document.Text);
            advanced.Text = "cpu.temp + 6";
            advanced.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, advanced, search)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.Contains("cpu.temp + 6", editor.Session.Document.Text);
        }
        finally { window.Close(); }
    });

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var inner in Descendants(child)) yield return inner;
        }
    }

    static string Repository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
