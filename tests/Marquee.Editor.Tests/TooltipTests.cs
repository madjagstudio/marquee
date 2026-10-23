using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using Marquee.Editor.Styles;
using Marquee.Editor.Dialogs;
using Marquee.Editor.Controls;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class TooltipTests
{
    [Theory]
    [InlineData("landscape", 1920, 480)]
    [InlineData("portrait", 480, 1920)]
    public void Size_sliders_follow_canvas_axes_and_preserve_typed_sizes(string orientation, double width, double height) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("sizes",
            PropertiesPanelTests.Layout.Replace("landscape", orientation).Replace("type: image", "type: shape, shape: rect").Replace(", src: images/a.png", "")));
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var before = editor.Session.Document.Text;
        var window = new MainWindow(editor.Model);
        Show(window);
        try
        {
            Inspect(window, []);
            Assert.Equal(before, editor.Session.Document.Text);
            var row = editor.Model.Properties.Row("size")!;
            var sliders = Walk(window).Select(item => item.Node).OfType<Slider>()
                .Where(slider => slider.IsVisible && !PropertiesView.GetRawLabels(slider) && ReferenceEquals(slider.DataContext, row)).ToList();
            Assert.Equal(2, sliders.Count);
            Assert.All(sliders, slider => Assert.Equal(8, slider.Minimum));
            Assert.Equal(width, sliders[0].Maximum);
            Assert.Equal(height, sliders[1].Maximum);
            sliders[0].Value = 8;
            Sta.Pump();
            Assert.Equal("8", row.X);
            Assert.Equal("60", row.Y);
            sliders[1].Value = height;
            Sta.Pump();
            Assert.Equal(height.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Y);
            row.X = "2400";
            row.Y = "2";
            Assert.True(row.CommitPair());
            Sta.Pump();
            Assert.Equal("2400", row.X);
            Assert.Equal("2", row.Y);
            Assert.Contains("size: [2400, 2]", editor.Session.Document.Text);
            Assert.Equal(width, sliders[0].Maximum);
            Assert.Equal(height, sliders[1].Maximum);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("1")]
    [InlineData("160")]
    public void Rendering_a_text_size_outside_the_slider_range_does_not_rewrite_it(string value) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        editor.Session.Select([OpenEditor.P("widgets[0]")]);
        var row = editor.Model.Properties.Row("style.size")!;
        row.Text = value;
        Assert.True(row.Commit());
        var before = editor.Session.Document.Text;
        var window = new MainWindow(editor.Model);
        Show(window);
        try
        {
            Inspect(window, []);
            var slider = Walk(window).Select(item => item.Node).OfType<Slider>()
                .First(slider => slider.IsVisible && ReferenceEquals(slider.DataContext, row));
            Assert.Equal(8, slider.Minimum);
            Assert.Equal(120, slider.Maximum);
            Assert.Equal(before, editor.Session.Document.Text);
            row.Text = value == "1" ? "160" : "1";
            Assert.True(row.Commit());
            Sta.Pump();
            Assert.Equal(value == "1" ? "160" : "1", row.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Add_widget_menu_explains_every_type_with_a_distinct_tooltip() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        var window = new MainWindow(editor.Model);
        Show(window);
        try
        {
            var menu = (MenuItem)window.FindName("AddWidgetMenu");
            menu.IsSubmenuOpen = true;
            Sta.Pump();
            var items = menu.Items.OfType<MenuItem>().ToList();
            var tips = items.Select(item => item.ToolTip?.ToString()).ToList();
            Assert.Equal(tips.Count, tips.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var type in Marquee.Core.Themes.Schema.ThemeSchema.Widgets.Names)
            {
                var item = Assert.Single(items, item => Equals(item.CommandParameter, type));
                var tip = Assert.IsType<string>(item.ToolTip);
                Assert.StartsWith(type + ":", tip, StringComparison.OrdinalIgnoreCase);
                Assert.EndsWith(".", tip);
            }
            Assert.Contains("Value: a live reading such as a temperature, with its unit.", tips);
            Assert.Contains("Gauge: a dial or arc that fills with a reading.", tips);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Every_visible_control_in_the_editor_has_a_tooltip() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        environment.AddTheme("second-theme");
        environment.AddTheme("first-example", installed: true);
        environment.AddTheme("second-example", installed: true);
        using var editor = PropertiesPanelTests.Open(environment);
        var window = new MainWindow(editor.Model);
        var missing = new HashSet<string>();
        Show(window);
        try
        {
            // Exercise all property templates, including controls initially inside collapsed sections.
            foreach (var value in editor.Model.TestValues.Rows) value.IsOpen = true;
            for (var index = 0; index < 6; index++)
            {
                editor.Session.Select([OpenEditor.P($"widgets[{index}]")]);
                if (editor.Model.Properties.Row("style.fill") is { IsGradient: true } fill) fill.IsGradientOpen = true;
                if (editor.Model.Properties.RulesList is { } rules) rules.AddCommand.Execute(null);
                Inspect(window, missing);
            }

            InspectMenus(window, missing);
            editor.Model.Styles.BeginRename(editor.Model.Styles.Items[0]);
            Inspect(window, missing);
            editor.Model.Styles.CancelRename(editor.Model.Styles.Items[0]);
            editor.Session.Select([]);
            Inspect(window, missing);
            var date = Walk(window).Select(item => item.Node).OfType<DatePicker>().Single(picker => picker.IsVisible);
            date.IsDropDownOpen = true;
            Sta.Pump();
            Inspect(window, missing);
            date.IsDropDownOpen = false;
        }
        finally { window.Close(); }
        Assert.True(missing.Count == 0, "Controls without tooltips:\n" + string.Join("\n", missing.Order()));
    });

    [Fact]
    public void Background_ring_and_group_controls_have_tooltips() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("background",
            TestEnvironment.Layout.Replace("widgets:\n", "ring: { effect: static }\nstyles: { hot: { color: '#FF0000' } }\nwidgets:\n")));
        var window = new MainWindow(editor.Model);
        var missing = new HashSet<string>();
        Show(window);
        try
        {
            editor.Model.Layers.SelectBackground(0);
            Inspect(window, missing);
            InspectMenus(window, missing);
            editor.Session.Select([OpenEditor.P("widgets[2]")]);
            editor.Model.Layers.RenameCommand.Execute(null);
            Inspect(window, missing);
            editor.Session.Select([]);
            Inspect(window, missing);
        }
        finally { window.Close(); }
        Assert.True(missing.Count == 0, "Controls without tooltips:\n" + string.Join("\n", missing.Order()));
    });

    [Fact]
    public void Every_value_picker_mode_and_colour_popup_has_tooltips() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = PropertiesPanelTests.Open(environment);
        var model = new ValuePickerViewModel(editor.Workspace.Values, "cpu.load", showRange: true);
        var window = new Window { Content = new ValuePickerControl { DataContext = model }, Width = 400, Height = 700 };
        var missing = new HashSet<string>();
        Show(window);
        try
        {
            foreach (var mode in Enum.GetValues<ValuePickerMode>())
            {
                model.Mode = mode;
                Inspect(window, missing);
            }
            window.Content = new ColorPicker();
            Inspect(window, missing);
        }
        finally { window.Close(); }

        var main = new MainWindow(editor.Model);
        Show(main);
        try
        {
            editor.Session.Select([OpenEditor.P("widgets[0]")]);
            Inspect(main, missing);
            var swatch = Walk(main).Select(item => item.Node).OfType<ToggleButton>()
                .First(button => button.Name == "SwatchButton" && button.IsVisible);
            swatch.IsChecked = true;
            Sta.Pump();
            Assert.Contains(Walk(main), item => item.Node is Popup { IsOpen: true });
            Inspect(main, missing);
            swatch.IsChecked = false;
        }
        finally { main.Close(); }
        Assert.True(missing.Count == 0, "Controls without tooltips:\n" + string.Join("\n", missing.Order()));
    });

    [Theory]
    [InlineData("Name")]
    [InlineData("Orientation")]
    [InlineData("CleanUp")]
    public void Every_visible_dialog_control_has_a_tooltip(string dialog) => Sta.Run(() =>
    {
        Window window = dialog switch
        {
            "Name" => new NameDialog("Create theme", "Name", "mine", _ => null),
            "Orientation" => new OrientationDialog("portrait"),
            "CleanUp" => new CleanUpDialog("mine", new CleanUpViewModel([new UnusedFile("unused.png", 128)])),
            _ => throw new ArgumentOutOfRangeException(nameof(dialog)),
        };
        var missing = new HashSet<string>();
        Show(window);
        try { Inspect(window, missing); }
        finally { window.Close(); }
        Assert.True(missing.Count == 0, "Controls without tooltips:\n" + string.Join("\n", missing.Order()));
    });

    static void InspectMenus(Window window, HashSet<string> missing)
    {
        // Styles create their context menu on right-click, as an actual user opens it.
        foreach (var row in Walk(window).Select(item => item.Node).OfType<Border>()
                     .Where(row => row.Name == "Row" && row.DataContext is StyleItem).ToList())
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = UIElement.MouseRightButtonDownEvent });
        foreach (var menu in Walk(window).Select(item => item.Node).OfType<Menu>().ToList())
            InspectItems(menu, missing, "Menu");
        var contexts = Walk(window).Select(item => item.Node).OfType<FrameworkElement>()
            .Where(element => element.ContextMenu is not null).ToList();
        Assert.True(contexts.Count >= 3, "Expected Background, Widgets and Styles context menus.");
        foreach (var owner in contexts)
        {
            var menu = owner.ContextMenu!;
            menu.PlacementTarget = owner;
            menu.Placement = PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = menu.VerticalOffset = -20000;
            menu.IsOpen = true;
            Sta.Pump();
            try { InspectItems(menu, missing, $"ContextMenu[{owner.Name}]"); }
            finally { menu.IsOpen = false; }
        }
    }

    static void InspectItems(ItemsControl menu, HashSet<string> missing, string path)
    {
        var duplicates = menu.Items.OfType<MenuItem>().GroupBy(item => item.ToolTip?.ToString(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => $"{path}: {group.Key}").ToList();
        Assert.True(duplicates.Count == 0, "Menu items share tooltips:\n" + string.Join("\n", duplicates));
        foreach (var item in menu.Items.OfType<MenuItem>().ToList())
        {
            var name = item.Header is TextBlock text ? text.Text : item.Header?.ToString() ?? item.Name;
            var itemPath = $"{path}/{name}";
            if (item.ToolTip is null || string.IsNullOrWhiteSpace(item.ToolTip.ToString())) missing.Add(itemPath);
            if (!item.HasItems) continue;
            item.IsSubmenuOpen = true;
            Sta.Pump();
            try { InspectItems(item, missing, itemPath); }
            finally { item.IsSubmenuOpen = false; }
        }
    }

    static void Show(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -20000;
        window.ShowActivated = window.ShowInTaskbar = false;
        window.Show();
        Sta.Pump();
    }

    static void Inspect(Window window, HashSet<string> missing)
    {
        // Expanding can instantiate further templates; keep going until every section is open.
        for (var pass = 0; pass < 10; pass++)
        {
            Sta.Pump();
            var closed = Walk(window).Select(item => item.Node).OfType<Expander>().Where(e => !e.IsExpanded).ToList();
            if (closed.Count == 0) break;
            foreach (var expander in closed) expander.IsExpanded = true;
        }
        Sta.Pump();
        var controls = Walk(window).Where(item => item.Node is Control { IsVisible: true } &&
            item.Node is ButtonBase or Slider or ComboBox or TextBox or Expander).ToList();
        Assert.NotEmpty(controls); // An unrendered window must never pass vacuously.
        foreach (var (node, path) in controls)
        {
            var tip = ((Control)node).ToolTip;
            if (tip is ToolTip tooltip) tip = tooltip.Content;
            if (tip is TextBlock text) tip = text.Text;
            if (tip is null || string.IsNullOrWhiteSpace(tip.ToString())) missing.Add(path);
        }
    }

    static IEnumerable<(DependencyObject Node, string Path)> Walk(DependencyObject node, string parent = "")
    {
        var element = node as FrameworkElement;
        var name = string.IsNullOrEmpty(element?.Name) ? "" : $"#{element.Name}";
        var context = element?.DataContext is Marquee.Editor.Properties.PropertyRowViewModel row ? $"[{row.Name}]" : "";
        var path = $"{parent}/{node.GetType().Name}{name}{context}";
        yield return (node, path);
        // Popups own a separate presentation source and are not visual children of their owner.
        if (node is Popup { IsOpen: true, Child: { } content })
            foreach (var child in Walk(content, path + "/PopupChild")) yield return child;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(node, index), $"{path}[{index}]")) yield return child;
    }
}
