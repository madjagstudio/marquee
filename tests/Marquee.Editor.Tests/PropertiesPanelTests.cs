using Marquee.Editor.Properties;
using Marquee.Editor.Resources;
using Marquee.Editor.Tests.TestSupport;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

/// The properties panel's controls, driven through their view-models as the views drive them: each kind of key writes
/// its value through PropertyModel into layout.yaml, a slider's drag is one undo step, and the panel shows defaults,
/// mixed values and what can be reset.
public class PropertiesPanelTests
{
    [Theory]
    [InlineData("style.size", 8, 120)]
    [InlineData("style.text_stroke.width", 0, 8)]
    [InlineData("style.stroke.width", 0, 12)]
    [InlineData("style.radius", 0, 40)]
    [InlineData("style.shadow.blur", 0, 30)]
    [InlineData("style.glow.radius", 0, 30)]
    [InlineData("style.letter_spacing", -5, 20)]
    [InlineData("style.line_height", 0.8, 2)]
    [InlineData("rotate", -180, 180)]
    [InlineData("smooth", 0, 2000)]
    public void Sliders_use_the_useful_range(string name, double min, double max) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        Select(editor, "widgets[0]");
        var row = Row(editor, name);
        Assert.Equal(min, row.SliderMin);
        Assert.Equal(max, row.SliderMax);
    });

    [Fact]
    public void Text_size_slider_left_end_writes_eight() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        Select(editor, "widgets[0]");
        var row = Row(editor, "style.size");
        row.SlideTo(row.SliderMin, final: true);
        Assert.Contains("size: 8", Text(editor));
        Assert.Equal("8", row.Text);
    });

    [Theory]
    [InlineData("160")]
    [InlineData("1")]
    [InlineData("1000")]
    public void Typed_text_size_uses_schema_limits_without_expanding_the_slider(string value) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        Select(editor, "widgets[0]");
        var row = Row(editor, "style.size");
        row.Text = value;
        Assert.True(row.Commit());
        Assert.Equal(value, row.Text);
        Assert.Contains($"size: {value}", Text(editor));
        Assert.Equal(8, row.SliderMin);
        Assert.Equal(120, row.SliderMax);
        row.Text = "1001";
        Assert.False(row.Commit());
    });

    /// One widget of each shape the kinds need: a text, a value, a graph with a series, a shape with a gradient, a bare
    /// shape and an image, with a named style to pick.
    public const string Layout =
        "canvas: { orientation: landscape }\n" +
        "styles:\n  meter: { fill: \"#336699\", radius: 3 }\n" +
        "widgets:\n" +
        "  - { type: text, at: [10, 10], size: [300, 60], text: \"CPU {cpu.temp:0}°\", style: { color: \"#FF0000\" } }\n" +
        "  - { type: value, at: [10, 100], size: [200, 60], source: cpu.load, format: \"{0:0}%\" }\n" +
        "  - type: graph\n" +
        "    at: [10, 200]\n" +
        "    size: [400, 120]\n" +
        "    series:\n" +
        "      - { source: cpu.load }\n" +
        "  - { type: shape, shape: rect, at: [500, 10], size: [100, 100], style: { fill: { linear: [\"#000000\", \"#808080\", \"#FFFFFF\"] } } }\n" +
        "  - { type: shape, shape: rect, at: [700, 10], size: [100, 100] }\n" +
        "  - { type: image, at: [900, 10], size: [64, 64], src: images/a.png }\n";

    internal static OpenEditor Open(TestEnvironment environment)
    {
        var folder = environment.AddTheme("kinds", Layout);
        TestEnvironment.WritePng(Path.Combine(folder, "images", "a.png"));
        TestEnvironment.WritePng(Path.Combine(folder, "images", "b.png"));
        return new OpenEditor(environment, folder);
    }

    static PropertiesViewModel Select(OpenEditor editor, params string[] widgets)
    {
        editor.Session.Select(widgets.Select(P));
        return editor.Model.Properties;
    }

    static PropertyRowViewModel Row(OpenEditor editor, string name) =>
        editor.Model.Properties.Row(name) ?? throw new InvalidOperationException($"The panel has no row {name}.");

    static string Text(OpenEditor editor) => editor.Session.Document.Text;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Transparency_uses_percentages_and_preserves_yaml_fractions(bool background) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("percent", TestEnvironment.Layout));
        if (background) editor.Model.Layers.SelectBackground(0);
        else Select(editor, "widgets[0]");
        var row = Row(editor, background ? "opacity" : "style.opacity");
        Assert.Equal("100", row.Text);
        Assert.Contains("100%", row.Help);
        Assert.Equal("%", row.Unit);
        Assert.Equal(0, row.SliderMin);
        Assert.Equal(100, row.SliderMax);
        row.Text = "50";
        Assert.False(row.HasError);
        Assert.True(row.Commit());
        Assert.Contains("opacity: 0.5", Text(editor));
        row.SlideTo(25, final: true);
        Assert.Contains("opacity: 0.25", Text(editor));
        // Recreate the rows from stored YAML, rather than relying on the edited text.
        editor.Session.Select([]);
        if (background) editor.Model.Layers.SelectBackground(0);
        else Select(editor, "widgets[0]");
        row = Row(editor, background ? "opacity" : "style.opacity");
        Assert.Equal("25", row.Text);
        Assert.Equal(25, row.Number);
        Assert.Equal("%", row.Unit);
    });

    [Fact]
    public void Numbers_checkboxes_dropdowns_and_blocks_write_their_keys() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);

        Select(editor, "widgets[3]");
        var radius = Row(editor, "style.radius");
        Assert.Equal(PropertyEditor.Number, radius.Editor);
        Assert.Equal("px", radius.Unit);
        radius.Text = "12";
        Assert.True(radius.Commit());
        Assert.Contains("radius: 12", Text(editor));

        Row(editor, "style.stroke.width").Text = "3";
        Row(editor, "style.stroke.width").Commit();
        Assert.Contains("stroke: { width: 3 }", Text(editor));

        Select(editor, "widgets[4]");
        var hidden = Row(editor, "hidden");
        Assert.Equal(PropertyEditor.Boolean, hidden.Editor);
        Assert.False(hidden.Checked);
        hidden.Checked = true;
        Assert.Contains("size: [100, 100], hidden: true", Text(editor));

        Select(editor, "widgets[0]");
        var align = Row(editor, "style.align");
        Assert.Equal(PropertyEditor.Choice, align.Editor);
        Assert.Contains("right", align.Choices);
        align.Choice = "right";
        Assert.Contains("align: right", Text(editor));
    });

    [Fact]
    public void A_colour_a_paint_and_its_gradient_switch_write_their_keys() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);

        Select(editor, "widgets[0]");
        var color = Row(editor, "style.color");
        Assert.Equal(PropertyEditor.Color, color.Editor);
        color.CommitColor("#00FF0080");
        Assert.Contains("color: \"#00FF0080\"", Text(editor));

        Select(editor, "widgets[4]");
        var fill = Row(editor, "style.fill");
        Assert.Equal(PropertyEditor.Paint, fill.Editor);
        fill.Text = "#123456";
        fill.Commit();
        Assert.Contains("style: { fill: \"#123456\" }", Text(editor));

        Row(editor, "style.fill").UseGradient = true;
        Assert.Contains("fill: { linear: [\"#123456\", \"#123456\"] }", Text(editor));
        Assert.True(Row(editor, "style.fill").IsGradientOpen);

        Row(editor, "style.fill").UseGradient = false;
        Assert.Contains("style: { fill: \"#123456\" }", Text(editor));
    });

    [Fact]
    public void Fonts_and_weights_offer_their_choices_and_write_what_is_chosen() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        Select(editor, "widgets[0]");

        var font = Row(editor, "style.font");
        Assert.Equal(PropertyEditor.Font, font.Editor);
        Assert.Contains("Inter", font.Suggestions);
        Assert.True(font.CanImport);
        font.Text = "Arial";
        font.Commit();
        Assert.Contains("font: Arial", Text(editor));

        var weight = Row(editor, "style.weight");
        Assert.Equal(PropertyEditor.Weight, weight.Editor);
        Assert.Contains("bold", weight.Choices);
        Assert.Contains("900", weight.Choices);
        weight.Text = "1000";
        Assert.NotNull(weight.Error);
        Assert.False(weight.Commit());
        weight.Text = "600";
        weight.Commit();
        Assert.Contains("weight: 600", Text(editor));
    });

    [Fact]
    public void Keys_and_expressions_are_checked_as_they_are_typed() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);

        Select(editor, "widgets[1]");
        var source = Row(editor, "source");
        Assert.Equal(PropertyEditor.ValuePicker, source.Editor);
        Assert.Contains(source.Picker!.Keys, key => key.Key == "cpu.load");
        Assert.Contains(source.Picker.Keys, key => key.Key == "gpu.temp");
        source.Text = "max(cpu.load,";
        Assert.NotNull(source.Error);
        source.Text = "max(cpu.load, gpu.load)";
        Assert.Null(source.Error);
        source.Commit();
        Assert.Contains("max(cpu.load, gpu.load)", Text(editor));

        Select(editor, "widgets[2]");
        var series = Row(editor, "series[0].source");
        series.Text = "cpu.load + 1";
        Assert.NotNull(series.Error);
        series.Text = "gpu.load";
        series.Commit();
        Assert.Contains("- { source: gpu.load }", Text(editor));
    });

    [Fact]
    public void Templates_and_formats_preview_with_the_canvas_values() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);

        Select(editor, "widgets[0]");
        var text = Row(editor, "text");
        Assert.Equal(PropertyEditor.Template, text.Editor);
        // No tray and no sensors: a load's sample is 30.
        text.Text = "GPU {gpu.load:0}%";
        Assert.Equal("GPU 30%", text.Preview);
        text.Text = "GPU {gpu.load:0";
        Assert.Null(text.Preview);
        Assert.NotNull(text.Error);
        text.Text = "GPU {gpu.load:0}%";
        text.Commit();
        Assert.Contains("text: \"GPU {gpu.load:0}%\"", Text(editor));

        Select(editor, "widgets[1]");
        var format = Row(editor, "format");
        format.Text = "{0:0.0} percent";
        Assert.Equal("30.0 percent", format.Preview);
        format.Commit();
        Assert.Contains("{0:0.0} percent", Text(editor));
    });

    [Fact]
    public void Files_points_and_sizes_write_their_keys() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);

        Select(editor, "widgets[5]");
        var src = Row(editor, "src");
        Assert.Equal(PropertyEditor.File, src.Editor);
        Assert.Equal(["images/a.png", "images/b.png"], src.Suggestions);
        src.Text = "images/b.png";
        src.Commit();
        Assert.Contains("src: images/b.png", Text(editor));

        Select(editor, "widgets[0]");
        var at = Row(editor, "at");
        Assert.Equal(PropertyEditor.Pair, at.Editor);
        (at.X, at.Y) = ("20", "30");
        at.CommitPair();
        Assert.Contains("at: [20, 30]", Text(editor));

        var size = Row(editor, "size");
        Assert.Equal(Strings.Width, size.XLabel);
        size.X = "0";
        Assert.NotNull(size.Error);
        (size.X, size.Y) = ("320", "64");
        size.CommitPair();
        Assert.Contains("size: [320, 64]", Text(editor));
    });

    [Fact]
    public void A_named_style_is_picked_and_its_overrides_cleared() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);

        var panel = Select(editor, "widgets[4]");
        Assert.Equal([Strings.NoNamedStyle, "meter"], panel.StyleChoices);
        panel.StyleChoice = "meter";
        Assert.Contains("size: [100, 100], style: meter }", Text(editor));
        Assert.Equal("meter", panel.StyleLabel);
        // The named style's radius shows, greyed, until the widget sets its own.
        Assert.True(Row(editor, "style.radius").IsDefault);
        Assert.Equal("3", Row(editor, "style.radius").Text);

        Row(editor, "style.radius").Text = "9";
        Row(editor, "style.radius").Commit();
        Assert.Contains("style: { extends: meter, radius: 9 }", Text(editor));
        Assert.Equal(Strings.StyleOverrides("meter", "1"), panel.StyleLabel);

        panel.ClearOverridesCommand.Execute(null);
        Assert.Contains("style: meter }", Text(editor));
    });

    [Fact]
    public void A_list_adds_moves_and_removes_items() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        Select(editor, "widgets[2]");

        var series = Row(editor, "series");
        Assert.Equal(PropertyEditor.List, series.Editor);
        Assert.Single(series.Items);
        series.AddItemCommand.Execute(null);
        Assert.Equal(2, Row(editor, "series").Items.Count);

        Row(editor, "series[1].source").Text = "gpu.load";
        Row(editor, "series[1].source").Commit();
        Row(editor, "series").Items[1].MoveUpCommand.Execute(null);
        Assert.Equal("gpu.load", Row(editor, "series[0].source").Text);

        Row(editor, "series").Items[0].RemoveCommand.Execute(null);
        Assert.Single(Row(editor, "series").Items);
        Assert.Equal("cpu.load", Row(editor, "series[0].source").Text);
    });

    [Fact]
    public void Defaults_show_greyed_reset_removes_a_key_and_required_keys_have_no_reset() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        Select(editor, "widgets[3]");

        var opacity = Row(editor, "style.opacity");
        Assert.True(opacity.IsDefault);
        Assert.False(opacity.CanReset);
        Assert.Contains(Strings.DefaultIs("100%"), opacity.Help);
        Assert.False(Row(editor, "at").CanReset);

        opacity.Text = "50";
        opacity.Commit();
        Assert.Contains("opacity: 0.5", Text(editor));
        Assert.True(Row(editor, "style.opacity").CanReset);

        Row(editor, "style.opacity").ResetCommand.Execute(null);
        Assert.DoesNotContain("opacity", Text(editor));
        Assert.True(Row(editor, "style.opacity").IsDefault);
    });

    [Fact]
    public void Several_widgets_show_their_shared_keys_mixed_where_they_differ_and_write_to_all() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        var panel = Select(editor, "widgets[3]", "widgets[4]");

        Assert.Equal("2 widgets", panel.Heading);
        var at = Row(editor, "at");
        Assert.True(at.IsMixed);
        Assert.Equal(Strings.Mixed, at.Placeholder);
        Assert.False(Row(editor, "size").IsMixed);

        Row(editor, "style.radius").Text = "6";
        Row(editor, "style.radius").Commit();
        Assert.Equal(2, Text(editor).Split("radius: 6").Length - 1);
        Assert.Equal(1, editor.Session.History.Count);
    });

    [Fact]
    public void A_slider_drag_is_one_undo_step_and_the_next_drag_another() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);
        Select(editor, "widgets[3]");
        var radius = Row(editor, "style.radius");

        radius.SlideTo(5.2, final: false);
        radius.SlideTo(10, final: false);
        radius.SlideTo(15.4, final: false);
        radius.EndSlide();

        // The same row all along: the panel keeps its rows while their shape is the same.
        Assert.Same(radius, Row(editor, "style.radius"));
        Assert.Contains("radius: 15 ", Text(editor));
        Assert.Equal(1, editor.Session.History.Count);

        radius.SlideTo(20, final: true);
        Assert.Equal(2, editor.Session.History.Count);
        editor.Session.Undo();
        Assert.Contains("radius: 15 ", Text(editor));
        editor.Session.Undo();
        Assert.Equal(Layout, Text(editor));
    });

    /// What saving has done: layout.yaml's text and when it was last written, and whether recovery has a copy.
    internal sealed class Disk(TestEnvironment environment, OpenEditor editor)
    {
        public string Layout => Path.Combine(editor.Session.Folder, Core.Themes.ThemeLoader.LayoutFileName);

        public string Text => File.ReadAllText(Layout);

        public DateTime Written => File.GetLastWriteTimeUtc(Layout);

        public bool HasRecovery => File.Exists(environment.Environment.Folders.RecoveryFile(editor.Session.Folder));
    }

    [Fact]
    public void A_long_resting_slide_is_one_undo_step_and_one_file_write_after_release() => Sta.Run(() =>
    {
        var clock = new FakeClock();
        using var environment = new TestEnvironment(clock);
        using var editor = Open(environment);
        var disk = new Disk(environment, editor);
        var opened = disk.Written;
        Select(editor, "widgets[3]");
        var radius = Row(editor, "style.radius");

        foreach (var value in new[] { 5.0, 10, 15 })
        {
            radius.SlideTo(value, final: false);
            // Held still longer than the undo merge window, autosave's delay and recovery's.
            clock.Advance(TimeSpan.FromSeconds(3));
            Sta.Pump();
        }

        Assert.Contains("radius: 15 ", Text(editor));
        Assert.Equal(Layout, disk.Text);
        Assert.Equal(opened, disk.Written);
        Assert.False(disk.HasRecovery);

        radius.EndSlide();
        clock.Advance(TimeSpan.FromSeconds(1));
        Sta.Pump();

        Assert.Contains("radius: 15 ", disk.Text);
        Assert.True(disk.HasRecovery);
        var saved = disk.Written;
        clock.Advance(TimeSpan.FromSeconds(10));
        Sta.Pump();
        Assert.Equal(saved, disk.Written);
        Assert.Equal(1, editor.Session.History.Count);
        Assert.True(editor.Session.Undo());
        Assert.Equal(Layout, Text(editor));
    });

    [Fact]
    public void A_resting_colour_drag_is_one_undo_step_saved_after_release() => Sta.Run(() =>
    {
        var clock = new FakeClock();
        using var environment = new TestEnvironment(clock);
        using var editor = Open(environment);
        var disk = new Disk(environment, editor);
        Select(editor, "widgets[0]");
        var color = Row(editor, "style.color");

        color.PreviewColor("#00FF00");
        clock.Advance(TimeSpan.FromSeconds(3));
        color.PreviewColor("#0000FF");
        clock.Advance(TimeSpan.FromSeconds(3));
        Sta.Pump();
        Assert.Contains("color: \"#0000FF\"", Text(editor));
        Assert.Equal(Layout, disk.Text);

        color.CommitColor("#0000FF");
        clock.Advance(TimeSpan.FromSeconds(1));
        Sta.Pump();

        Assert.Contains("color: \"#0000FF\"", disk.Text);
        Assert.Equal(1, editor.Session.History.Count);
        Assert.False(editor.Session.IsInteracting);
    });

    [Fact]
    public void Text_becomes_spans_and_rules_read_in_words() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = environment.AddTheme("rules", Layout.Replace(
            "style: { color: \"#FF0000\" } }",
            "style: { color: \"#FFFFFF\" }, source: cpu.temp, when: [ { above: 85, style: { color: \"#FF0000\" } } ] }"));
        using var editor = new OpenEditor(environment, folder);
        var panel = Select(editor, "widgets[0]");

        Assert.Equal(["CPU temperature: turns red above 85"], panel.Rules);
        Assert.True(panel.SplitIntoSpansCommand.CanExecute(null));
        panel.SplitIntoSpansCommand.Execute(null);

        Assert.Contains("spans: [", Text(editor));
        Assert.NotNull(Row(editor, "spans"));
    });

    [Fact]
    public void Copy_and_paste_style_are_buttons_too() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = Open(environment);

        var panel = Select(editor, "widgets[3]");
        panel.CopyStyleCommand.Execute(null);
        Select(editor, "widgets[4]");
        Assert.True(panel.PasteStyleCommand.CanExecute(null));
        panel.PasteStyleCommand.Execute(null);

        Assert.Contains("size: [100, 100], style: { fill: { linear: [\"#000000\", \"#808080\", \"#FFFFFF\"] } } }", Text(editor));
    });
}
