using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marquee.Editor.Controls;
using Marquee.Editor.Properties;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class VisualControlsTests
{
    const string TextTheme = "canvas: { orientation: landscape }\nwidgets: [{ type: text, at: [0, 0], size: [300, 100], text: Hello }]\n";

    [Fact]
    public void Rule_and_size_sliders_show_their_current_values_and_units_beside_them() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("value", "canvas: { orientation: landscape }\nwidgets: [{ type: value, at: [0, 0], size: [300, 100], source: cpu.load, format: '{0:0}%' }]\n"));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var window = Show(editor.Model.Properties);
        try
        {
            var rules = editor.Model.Properties.RulesList!; rules.TryNumber = 73; Sta.Pump();
            Assert.Contains(Walk(window).OfType<TextBlock>(), b => b.IsVisible && b.Text == "73 %");
            var size = editor.Model.Properties.Row("size")!;
            var width = Walk(window).OfType<Slider>().First(s => Equals(s.Tag, "Width") && ReferenceEquals(s.DataContext, size));
            width.Value = 80; Sta.Pump();
            Assert.Contains(Walk(window).OfType<TextBlock>(), b => b.IsVisible && b.Text == "80 px");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Text_choices_are_icon_toggles_with_tooltips_and_write_undoable_values() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("text", TextTheme));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var window = Show(editor.Model.Properties);
        try
        {
            foreach (var (name, option, expected) in new[] { ("style.align", "center", "center"), ("style.case", "upper", "upper"),
                ("style.weight", "bold", "bold"), ("style.italic", "true", "true"), ("style.underline", "true", "true"), ("style.strike", "true", "true") })
            {
                var button = Assert.Single(Buttons(window, name), b => Equals(b.Tag, option));
                Assert.NotNull(button.Content); Assert.False(string.IsNullOrWhiteSpace(button.ToolTip?.ToString()));
                var before = editor.Session.Document.Text;
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Sta.Pump();
                Assert.Equal(expected, editor.Model.Properties.Row(name)!.Text);
                Assert.True(editor.Session.Undo()); Assert.Equal(before, editor.Session.Document.Text); Sta.Pump();
            }
            Assert.Equal(3, Buttons(window, "style.align").Count());
            Assert.Equal(3, Buttons(window, "style.case").Count());
            Assert.DoesNotContain(Walk(window).OfType<TextBlock>(), block => block.IsVisible && block.Text == "Basics");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Swatch_opens_the_picker_and_hex_is_hidden_until_it_opens() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("text", TextTheme));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var window = Show(editor.Model.Properties);
        try
        {
            var row = editor.Model.Properties.Row("style.color")!;
            Assert.DoesNotContain(Walk(window).OfType<TextBox>(), b => b.IsVisible && ReferenceEquals(b.DataContext, row));
            var swatch = Assert.Single(Buttons(window, "style.color"));
            swatch.IsChecked = true; Sta.Pump();
            var popup = Assert.Single(Walk(window).OfType<Popup>(), p => p.IsOpen);
            Assert.Contains(Walk(popup.Child), n => n is ColorPicker);
            popup.IsOpen = false;
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Swatches_fill_their_button_with_the_colour_for_widgets_the_ring_and_its_rules() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("ring", TextTheme.Replace("text: Hello", "text: Hello, style: { color: '#336699' }") +
            "ring: { color: '#36D1DC', source: cpu.temp, when: [{ above: 80, color: '#FF5A36' }] }\n"));
        var window = Show(editor.Model.Properties);
        try
        {
            Assert.Equal(["#FF36D1DC", "#FFFF5A36"], SwatchColours(window));
            window.Close();
            editor.Session.Select([OpenEditor.P("widgets[0]")]);
            window = Show(editor.Model.Properties);
            Assert.Contains("#FF336699", SwatchColours(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_group_holding_a_clock_changes_its_time_and_date_formats_in_one_step() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("clock", "canvas: { orientation: landscape }\nwidgets: []\n"));
        editor.Session.Apply(new Model.AddModule(Model.ModuleLibrary.All.Single(module => module.Name == "Clock and date")));
        Assert.Equal([OpenEditor.P("widgets[0]")], editor.Session.Selection);
        var window = Show(editor.Model.Properties);
        try
        {
            var time = ClockChoice(window, "Time format");
            Assert.Equal("HH:mm", time.SelectedValue);
            var before = editor.Session.Document.Text;
            time.SelectedItem = time.Items.Cast<FormatChoice>().Single(choice => choice.Pattern == "h:mm tt");
            Sta.Pump();
            Assert.True(editor.Session.Document.TryGetScalar(OpenEditor.P("widgets[0].children[0].text"), out var text));
            Assert.Equal("{now|time:h:mm tt}", text);
            Assert.Equal("h:mm tt", ClockChoice(window, "Time format").SelectedValue);
            var date = ClockChoice(window, "Date format");
            date.SelectedItem = date.Items.Cast<FormatChoice>().Single(choice => choice.Pattern == "D");
            Sta.Pump();
            Assert.True(editor.Session.Document.TryGetScalar(OpenEditor.P("widgets[0].children[1].text"), out text));
            Assert.Equal("{now|date:D}", text);
            Assert.True(editor.Session.Undo());
            Assert.True(editor.Session.Undo());
            Assert.Equal(before, editor.Session.Document.Text);
            editor.Session.Select([OpenEditor.P("widgets[0].children[0]")]);
            Sta.Pump();
            Assert.DoesNotContain(Walk(window).OfType<ComboBox>(), box => box.IsVisible && box.Name == "GroupTimeFormat");
        }
        finally { window.Close(); }
    });

    static ComboBox ClockChoice(Window window, string name) => Walk(window).OfType<ComboBox>()
        .Single(box => box.IsVisible && System.Windows.Automation.AutomationProperties.GetName(box) == name && box.Name.StartsWith("Group", StringComparison.Ordinal));

    // The colours the swatch buttons draw, each over the whole button rather than collapsed to nothing.
    static List<string> SwatchColours(Window window) => Walk(window).OfType<ToggleButton>()
        .Where(b => b.IsVisible && b.Name == "SwatchButton" && !PropertiesView.GetRawLabels(b))
        .Select(b => Walk(b).OfType<System.Windows.Shapes.Rectangle>().Single())
        .Where(r => r.ActualWidth >= 20 && r.ActualHeight >= 12)
        .Select(r => ((SolidColorBrush)r.Fill).Color.ToString())
        .ToList();

    [Fact]
    public void Gauge_forms_have_pictures_and_change_in_one_undo_step() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("gauge", "canvas: { orientation: landscape }\nwidgets: [{ type: gauge, at: [0, 0], size: [200, 200], source: cpu.load, min: 0, max: 100 }]\n"));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var window = Show(editor.Model.Properties);
        try
        {
            var buttons = Walk(window).OfType<ToggleButton>().Where(b => b.Tag is string s && new[] { "dial", "arc", "ring" }.Contains(s)).ToList();
            Assert.Equal(3, buttons.Count); Assert.All(buttons, b => Assert.IsType<Image>(b.Content));
            var before = editor.Session.Document.Text;
            buttons.Single(b => Equals(b.Tag, "dial")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Sta.Pump();
            Assert.True(editor.Session.Document.Exists(OpenEditor.P("widgets[0].needle")));
            Assert.Empty(editor.Session.Theme!.Warnings);
            Assert.True(editor.Session.Undo()); Assert.Equal(before, editor.Session.Document.Text);
            buttons.Single(b => Equals(b.Tag, "ring")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Sta.Pump();
            Assert.Equal("360", editor.Model.Properties.Row("sweep")!.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Graph_and_blend_choices_show_distinct_thumbnails_and_write_the_choice() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("graph", "canvas: { orientation: landscape }\nwidgets: [{ type: graph, at: [0, 0], size: [300, 100], source: cpu.load }]\n"));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var window = Show(editor.Model.Properties);
        try
        {
            foreach (var (name, count, choice) in new[] { ("kind", 3, "area"), ("style.blend", 7, "multiply") })
            {
                var buttons = Buttons(window, name).ToList(); Assert.Equal(count, buttons.Count);
                Assert.All(buttons, b => Assert.IsType<Image>(b.Content));
                var pictures = buttons.Select(b => ((Image)b.Content).Source).Cast<System.Windows.Media.Imaging.BitmapSource>().Select(bitmap =>
                {
                    var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return Convert.ToBase64String(bytes);
                }).ToList();
                Assert.Equal(count, pictures.Distinct().Count());
                buttons.Single(b => Equals(b.Tag, choice)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Sta.Pump();
                Assert.Equal(choice, editor.Model.Properties.Row(name)!.Text);
            }
        }
        finally { window.Close(); }
    });

    static IEnumerable<ToggleButton> Buttons(Window window, string row) => Walk(window).OfType<ToggleButton>()
        .Where(b => b.IsVisible && !PropertiesView.GetRawLabels(b) && b.DataContext is PropertyRowViewModel r && r.Name == row);
    static Window Show(PropertiesViewModel model)
    {
        var window = new Window { Content = new PropertiesView { DataContext = model }, Width = 650, Height = 1100,
            Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); Sta.Pump();
        foreach (var expander in Walk(window).OfType<Expander>().Where(e => !PropertiesView.GetRawLabels(e)).ToList()) expander.IsExpanded = true;
        Sta.Pump(); return window;
    }
    static IEnumerable<DependencyObject> Walk(DependencyObject node)
    {
        yield return node;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
}
