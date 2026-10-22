using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class LayoutTests
{
    [Fact]
    public void Minimum_window_width_keeps_the_properties_panel_inside_the_window() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var window = Show(editor);
        try
        {
            window.Width = window.MinWidth;
            Sta.Pump();
            var properties = (FrameworkElement)window.FindName("PropertiesPanel");
            var root = (FrameworkElement)window.Content;
            var right = properties.TranslatePoint(new Point(properties.ActualWidth, 0), root).X;
            Assert.True(right <= root.ActualWidth, $"Properties extends to {right}, beyond the window width {root.ActualWidth}.");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Layers_toggle_reclaims_canvas_space_and_remembers_state_across_selection() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var window = Show(editor);
        try
        {
            var toggle = Assert.IsType<ToggleButton>(window.FindName("LayersToggle"));
            var panel = Assert.IsType<Grid>(window.FindName("LeftPanel"));
            var canvas = (FrameworkElement)window.FindName("CanvasArea");
            Assert.True(toggle.IsChecked);
            var width = canvas.ActualWidth;
            toggle.IsChecked = false;
            editor.Session.Select([OpenEditor.P("widgets[0]")]);
            Sta.Pump();
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.True(canvas.ActualWidth > width);
            toggle.IsChecked = true;
            Sta.Pump();
            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Equal(width, canvas.ActualWidth, 1);
            Assert.Contains((DependencyObject)window.FindName("LayersPanel"), Descendants(panel));
            Assert.Contains((DependencyObject)window.FindName("StylesPanel"), Descendants(panel));
            var splitter = Descendants(window).OfType<GridSplitter>().Single(s => s.ResizeDirection == GridResizeDirection.Columns && Grid.GetColumn(s) == 1);
            splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Right)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Sta.Pump();
            var resizedWidth = panel.ActualWidth;
            Assert.True(resizedWidth > 260);
            width = canvas.ActualWidth;
            toggle.IsChecked = false;
            Sta.Pump();
            Assert.True(canvas.ActualWidth > width + resizedWidth - 1);
            toggle.IsChecked = true;
            Sta.Pump();
            Assert.Equal(resizedWidth, panel.ActualWidth, 1);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Screen_defaults_to_landscape_and_refreshes_after_config_changes() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", "canvas: { orientation: portrait }\nwidgets: []\n"));
        Assert.Equal("Screen: landscape · 1920 × 480", editor.Model.ScreenTag);
        Assert.True(editor.Model.ScreenMismatch);
        File.WriteAllText(environment.ConfigFile, "orientation: portrait-flipped\n");
        editor.Model.RefreshScreen();
        Assert.Equal("Screen: portrait-flipped · 480 × 1920", editor.Model.ScreenTag);
        Assert.False(editor.Model.ScreenMismatch);
    });

    [Fact]
    public void Selection_box_and_handles_use_the_red_theme_brush() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var window = Show(editor);
        try
        {
            editor.Session.Select([OpenEditor.P("widgets[0]")]);
            Sta.Pump();
            var canvas = (FrameworkElement)window.FindName("CanvasArea");
            var red = ((SolidColorBrush)canvas.FindResource("Red")).Color;
            var shapes = Descendants(canvas).OfType<Shape>().Where(s => s.Stroke is SolidColorBrush b && b.Color == red).ToArray();
            Assert.Contains(shapes, s => s is Polygon);
            var handles = shapes.Where(s => s is Rectangle or Ellipse).ToArray();
            Assert.NotEmpty(handles);
            Assert.All(handles, s => Assert.Equal(red, Assert.IsType<SolidColorBrush>(s.Fill).Color));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Properties_show_plain_heading_basics_then_styling_and_collapsed_raw_keys() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine",
            "canvas: { orientation: landscape }\nwidgets: [{ type: value, source: gpu.temp, at: [0, 0], size: [200, 60] }]"));
        var window = Show(editor);
        try
        {
            editor.Session.Select([OpenEditor.P("widgets[0]")]);
            Sta.Pump();
            var view = (PropertiesView)((ContentControl)window.FindName("PropertiesPanel")).Content;
            var heading = Assert.IsType<TextBlock>(view.FindName("PlainHeading"));
            Assert.Equal("Value: GPU temperature", heading.Text);
            var basics = Assert.IsType<ItemsControl>(view.FindName("BasicsRows"));
            var styling = Assert.IsType<ItemsControl>(view.FindName("MoreStylingSections"));
            var raw = Assert.IsType<Expander>(view.FindName("RawSection"));
            Assert.NotEmpty(basics.Items);
            Assert.True(basics.TranslatePoint(new Point(), view).Y < styling.TranslatePoint(new Point(), view).Y);
            Assert.False(raw.IsExpanded);
            Assert.Equal("Every setting as written in layout.yaml", raw.Header);
            var sections = Descendants(styling).OfType<Expander>().Where(e => e.DataContext is PropertySectionViewModel).ToArray();
            Assert.NotEmpty(sections);
            foreach (var section in sections) Assert.Equal(Equals(section.Header, "Text"), section.IsExpanded);
            var text = sections.Single(e => Equals(e.Header, "Text"));
            text.IsExpanded = false;
            editor.Session.Select([]);
            editor.Session.Select([OpenEditor.P("widgets[0]")]);
            Sta.Pump();
            Assert.False(Descendants(styling).OfType<Expander>().Single(e => Equals(e.Header, "Text")).IsExpanded);
            var source = editor.Model.Properties.Row("source")!;
            Assert.Contains(Descendants(basics).OfType<TextBlock>(), t => t.Text == source.Row.PlainLabel);
            raw.IsExpanded = true;
            Sta.Pump();
            Assert.Contains(Descendants(raw).OfType<TextBlock>(), t => t.Text == "source");
            var opacity = editor.Model.Properties.Row("style.opacity")!;
            Assert.Contains(Descendants(raw).OfType<TextBlock>(), t => t.Text == "opacity" &&
                t.ToolTip is string tip && tip.Contains(opacity.Row.Tooltip) && tip.Contains("Default: 1"));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("landscape", "portrait", "1920 × 480")]
    [InlineData("portrait", "landscape", "480 × 1920")]
    public void Screen_tag_uses_temp_config_and_warns_until_theme_orientation_matches(string screen, string theme, string size) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        File.WriteAllText(environment.ConfigFile, $"orientation: {screen}\n");
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", $"canvas: {{ orientation: {theme} }}\nwidgets: []\n"));
        var window = Show(editor);
        try
        {
            var tag = Assert.IsType<TextBlock>(window.FindName("ScreenTag"));
            Assert.Equal($"Screen: {screen} · {size}", tag.Text);
            Assert.Equal(((SolidColorBrush)window.FindResource("Warning")).Color, ((SolidColorBrush)tag.Foreground).Color);
            Assert.Equal($"This theme is {theme} but your screen is set to {screen} in the tray, so it will show small", tag.ToolTip);
            Assert.True(editor.Model.Run(new SetOrientation(screen, false)));
            Sta.Pump();
            Assert.Equal(((SolidColorBrush)window.FindResource("TextMuted")).Color, ((SolidColorBrush)tag.Foreground).Color);
        }
        finally { window.Close(); }
    });

    static MainWindow Show(OpenEditor editor)
    {
        var window = new MainWindow(editor.Model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false,
        };
        window.Show();
        Sta.Pump();
        return window;
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
