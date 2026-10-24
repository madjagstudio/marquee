using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Marquee.Editor.Dialogs;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class ThemeTests
{
    [Fact]
    public void Xaml_colours_live_only_in_the_theme_dictionary()
    {
        var named = typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var files = Directory.GetFiles(EditorSource(), "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files.Where(file => Path.GetRelativePath(EditorSource(), file) != Path.Combine("Theme", "MadJag.xaml")))
        {
            foreach (var element in XDocument.Load(file).Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    var property = attribute.Name.LocalName == "Value"
                        ? (string?)element.Attribute("Property") ?? "" : attribute.Name.LocalName;
                    if (IsLiteral(attribute.Value, property)) failures.Add($"{Path.GetFileName(file)}: {attribute}");
                }
                // Property-element syntax and Color resource text must not bypass the guard.
                if (!element.HasElements && IsLiteral(element.Value.Trim(), element.Name.LocalName))
                    failures.Add($"{Path.GetFileName(file)}: {element}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        bool IsLiteral(string value, string property) =>
            Regex.IsMatch(value, "^#(?:[0-9a-f]{3}|[0-9a-f]{4}|[0-9a-f]{6}|[0-9a-f]{8})$", RegexOptions.IgnoreCase) ||
            (Regex.IsMatch(property, "(?:Background|Foreground|BorderBrush|Brush|Fill|Stroke|Color|SolidColorBrush)$") && named.Contains(value));
    }

    [Fact]
    public void Fonts_render_from_the_embedded_families_without_fallback() => Sta.Run(() =>
    {
        var window = OffScreen(new NameDialog("Theme", "Name", "mine", _ => null));
        try
        {
            var panel = new StackPanel();
            foreach (var (key, familyName, folder) in new[]
            {
                ("HeadFont", "Rajdhani", "Rajdhani"),
                ("BodyFont", "Inter", "Inter"),
                ("LabelFont", "JetBrains Mono", "JetBrainsMono"),
            })
            {
                var family = Assert.IsType<FontFamily>(window.TryFindResource(key));
                var faces = family.GetTypefaces();
                Assert.NotEmpty(faces);
                Assert.Contains(faces, face => face.TryGetGlyphTypeface(out var glyph) &&
                    glyph.CharacterToGlyphMap.ContainsKey('A') &&
                    glyph.FamilyNames.Values.Contains(familyName) &&
                    glyph.FontUri.AbsoluteUri.Contains($"MarqueeEditor;component/Fonts/{folder}/", StringComparison.OrdinalIgnoreCase));
                panel.Children.Add(new TextBlock { Text = "A", FontFamily = family });
            }
            window.Content = panel;
            window.Show();
            Sta.Pump();
            var picture = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            picture.Render(panel);
            Assert.True(panel.ActualHeight > 0);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Every_window_merges_the_theme_without_an_application() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        Func<Window>[] create =
        [
            () => new MainWindow(editor.Model),
            () => new NameDialog("Theme", "Name", "mine", _ => null),
            () => new OrientationDialog("portrait"),
            () => new CleanUpDialog("mine", new CleanUpViewModel([])),
            () => new Backgrounds.BackgroundGalleryView(new Backgrounds.BackgroundGalleryViewModel(
                new Model.BackgroundLibrary(environment.InstalledThemes), () => Marquee.Core.Sensors.SensorSnapshot.Of(DateTimeOffset.Now, new Dictionary<string, Marquee.Core.Sensors.SensorEntry>()))),
        ];
        var covered = new List<Type>();
        foreach (var factory in create)
        {
            var window = factory();
            try
            {
                covered.Add(window.GetType());
                Assert.IsType<SolidColorBrush>(window.Resources["Ink"]);
                Assert.Contains(window.Resources.MergedDictionaries,
                    dictionary => dictionary.Source.OriginalString.EndsWith("Theme/MadJag.xaml", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        }
        Assert.Equal(typeof(MainWindow).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(Window).IsAssignableFrom(type))
            .OrderBy(type => type.FullName), covered.OrderBy(type => type.FullName));
    });

    [Fact]
    public void Themed_controls_render_with_focus_and_disabled_states_and_working_content() => Sta.Run(() =>
    {
        var window = OffScreen(new NameDialog("Theme", "Name", "mine", _ => null));
        var combo = new ComboBox { IsEditable = true, ItemsSource = new[] { "First", "Second" }, SelectedIndex = 0 };
        var treeItem = new TreeViewItem { Header = "Parent", Items = { new TreeViewItem { Header = "Child" } }, IsExpanded = true };
        var tree = new TreeView { Items = { treeItem } };
        var tab = new TabControl { Items = { new TabItem { Header = "Tab", Content = "Tab content" } } };
        var list = new ListBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 1 };
        var expander = new Expander { Header = "Details", Content = "Expanded content", IsExpanded = true };
        var menuItem = new MenuItem { Header = "Menu", Items = { new MenuItem { Header = "Child", IsCheckable = true } } };
        var menu = new Menu { IsMainMenu = false, Items = { menuItem } };
        var context = new ContextMenu { Items = { new MenuItem { Header = "Context child" } } };
        var tooltip = new ToolTip { Content = "Help" };
        var button = new Button { Content = "Action", ContextMenu = context, ToolTip = tooltip };
        Control[] controls =
        [
            button, new Button { Content = "Primary", Style = (Style)window.FindResource("PrimaryButton") },
            new ToggleButton { Content = "Toggle", IsChecked = true }, new CheckBox { Content = "Check", IsChecked = true },
            new RadioButton { Content = "Radio", IsChecked = true }, new TextBox { Text = "Editable" }, combo,
            new Slider { Value = 5 }, new Slider { Orientation = Orientation.Vertical, Height = 40, Value = 5 },
            new ScrollBar { Maximum = 100, ViewportSize = 10, Height = 40 },
            new ScrollViewer { Content = new Border { Height = 100 }, Height = 30, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            expander, new GridSplitter { Height = 4 }, list, tree, tab, menu, new Separator(),
        ];
        try
        {
            var panel = new StackPanel();
            foreach (var control in controls) panel.Children.Add(control);
            window.Content = panel;
            window.Width = 600;
            window.Show();
            Sta.Pump();
            combo.IsDropDownOpen = true;
            Sta.Pump();

            Assert.Equal("First", Assert.IsType<TextBox>(combo.Template.FindName("PART_EditableTextBox", combo)).Text);
            Assert.True(((TreeViewItem)treeItem.Items[0]).IsVisible);
            Assert.Equal("Tab content", tab.SelectedContent);
            Assert.Equal("Second", list.SelectedItem);
            Assert.True(((FrameworkElement)expander.Template.FindName("Body", expander)).IsVisible);
            Assert.True(((Popup)combo.Template.FindName("PART_Popup", combo)).IsOpen);
            combo.IsDropDownOpen = false;
            Sta.Pump();
            // Native menus also close immediately without an active window. Lay out the popup's contents directly.
            var menuContent = ((Popup)menuItem.Template.FindName("PART_Popup", menuItem)).Child;
            menuContent.Measure(new Size(300, 300));
            menuContent.Arrange(new Rect(menuContent.DesiredSize));
            Assert.True(menuContent.DesiredSize.Height > 0);
            context.PlacementTarget = tooltip.PlacementTarget = button;
            context.IsOpen = tooltip.IsOpen = true;
            Sta.Pump();

            var items = new Control[] { context, tooltip, menuItem, treeItem, (TabItem)tab.Items[0],
                (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(0), (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0) };
            foreach (var control in controls.Concat(items))
            {
                Assert.NotNull(control.Template);
                Assert.NotNull(control.FocusVisualStyle);
                control.IsEnabled = false;
                Assert.InRange(control.Opacity, 0.1, 0.6);
                control.IsEnabled = true;
            }
            var picture = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            picture.Render(panel);
        }
        finally
        {
            combo.IsDropDownOpen = menuItem.IsSubmenuOpen = context.IsOpen = tooltip.IsOpen = false;
            window.Close();
        }
    });

    static Window OffScreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = window.Top = -20000;
        window.ShowActivated = window.ShowInTaskbar = false;
        return window;
    }

    static string EditorSource([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "src", "Marquee.Editor"));
}
