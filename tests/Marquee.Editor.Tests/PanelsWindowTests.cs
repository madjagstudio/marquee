using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marquee.Core.Themes;
using Marquee.Editor.Controls;
using Marquee.Editor.Layers;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Session;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;
using Marquee.Editor.Values;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

/// The panels in the window, shown off screen: they fill their slots, the problems list selects a problem's widget,
/// and the whole window renders on example-showcase with every panel populated.
public class PanelsWindowTests
{
    /// Where the render check saves its picture, when set: a folder outside the repository.
    const string SnapshotFolderVariable = "MARQUEE_EDITOR_SNAPSHOT_DIR";

    [Fact]
    public void Background_owns_add_layer_and_the_side_sections_can_be_resized() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var window = Show(environment, environment.AddTheme("mine"));
        try
        {
            var layers = (LayersView)Panel(window, "LayersPanel");
            window.Model.IsLayersOpen = true;
            Sta.Pump();
            var background = Assert.IsType<DockPanel>(layers.FindName("BackgroundSection"));
            Assert.Contains(Descendants(background).OfType<TextBlock>(),
                text => text.Text == Resources.Strings.BackgroundSection);
            Assert.Contains(Descendants(background).OfType<Button>(),
                item => Equals(item.Content, Resources.Strings.AddLayerMenu) && item.Command == window.Model.Layers.BackgroundGalleryCommand);

            var widgetList = (ItemsControl)layers.FindName("WidgetList");
            var backgroundList = (ItemsControl)layers.FindName("BackgroundList");
            Assert.DoesNotContain(widgetList, Descendants(background));
            foreach (var list in new[] { widgetList, backgroundList })
            {
                var scroll = Assert.IsType<ScrollViewer>(list.Parent);
                Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                Assert.True(scroll.ViewportHeight > 0);
            }

            var splitters = Descendants(window).OfType<GridSplitter>().ToArray();
            Assert.Equal(5, splitters.Length);
            Assert.Equal(3, splitters.Count(splitter => splitter.ResizeDirection == GridResizeDirection.Rows));
            foreach (var splitter in splitters)
            {
                Assert.Equal(GridResizeBehavior.PreviousAndNext, splitter.ResizeBehavior);
                var grid = Assert.IsType<Grid>(splitter.Parent);
                var rows = splitter.ResizeDirection == GridResizeDirection.Rows;
                var index = rows ? Grid.GetRow(splitter) : Grid.GetColumn(splitter);
                double Size() => rows ? grid.RowDefinitions[index - 1].ActualHeight : grid.ColumnDefinitions[index - 1].ActualWidth;
                var before = Size();
                if (rows)
                {
                    Assert.True(grid.RowDefinitions[index - 1].MinHeight > 0);
                    Assert.True(grid.RowDefinitions[index + 1].MinHeight > 0);
                }
                else
                {
                    Assert.True(grid.ColumnDefinitions[index - 1].MinWidth > 0);
                    Assert.True(grid.ColumnDefinitions[index + 1].MinWidth > 0);
                }

                // Keyboard resizing exercises the same GridSplitter sizing path as dragging.
                splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window),
                    0, rows ? Key.Up : Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
                Sta.Pump();
                Assert.True(rows ? Size() < before : Size() > before,
                    $"The {splitter.ResizeDirection} splitter at {index} did not resize: {before} -> {Size()}.");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Each_panel_has_its_view_bound_to_its_model() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var window = Show(environment, environment.AddTheme("mine"));

        Assert.Same(window.Model.Layers, ((FrameworkElement)Panel(window, "LayersPanel")).DataContext);
        Assert.Same(window.Model.Styles, ((FrameworkElement)Panel(window, "StylesPanel")).DataContext);
        Assert.Same(window.Model.Properties, ((PropertiesView)Panel(window, "PropertiesPanel")).DataContext);
        Assert.Same(window.Model.TestValues, ((TestValuesView)Panel(window, "TestValuesPanel")).DataContext);
        window.Close();
    });

    [Fact]
    public void A_problem_in_the_list_shows_its_line_and_selects_its_widget() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var window = Show(environment, mine);

        File.WriteAllText(Path.Combine(mine, ThemeLoader.LayoutFileName), TestEnvironment.Layout.Replace("at: [500, 100]", "at: [500]"));
        Assert.True(Sta.PumpUntil(() => window.Model.Problems.Count > 0));
        var menu = (MenuItem)window.FindName("ProblemsMenu");
        var item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
        var line = ((StackPanel)item.Header).Children.OfType<TextBlock>().Last().Text;
        Assert.StartsWith("layout.yaml line 6:", line);

        item.Command.Execute(item.CommandParameter);

        Assert.Equal([P("widgets[1]")], window.Model.Workspace!.Session.Selection);
        window.Close();
    });

    [Fact]
    public void The_colour_picker_follows_its_colour_and_keeps_the_hue_of_a_grey() => Sta.Run(() =>
    {
        var picker = new ColorPicker { Color = "#FF000080" };
        Assert.Equal(new Hsva(0, 1, 1, 128 / 255.0), picker.Current);

        picker.Color = "#00FF00";
        picker.Color = "#808080";

        Assert.Equal(120, picker.Current.H, 3);
        Assert.Equal(0, picker.Current.S, 3);
    });

    [Fact]
    public void The_window_renders_with_every_panel_populated_on_the_showcase() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var showcase = environment.AddExample("example-showcase");
        var window = Show(environment, showcase, height: 1100);
        var model = window.Model;
        var session = model.Workspace!.Session;

        // The clock group's title: a text with a gradient fill and an outline, with its gradient editor open.
        session.Select([P("widgets[0].children[0]")]);
        model.IsLayersOpen = true;
        model.Properties.MoreStyling.Single(section => section.Title == "Text").Expansion.IsExpanded = true;
        var fill = model.Properties.Row("style.text_fill")!;
        fill.IsGradientOpen = true;
        Assert.NotNull(fill.Gradient);
        var key = model.TestValues.Rows.First(row => !row.IsNow);
        key.Number = (key.Min + key.Max) * 0.8;
        key.IsOpen = true;
        Sta.Pump();
        // The properties panel scrolled to the gradient editor, so the picture shows it.
        var editor = Descendants(window).OfType<Gradients.GradientEditor>().Single(view => view.DataContext is not null);
        editor.BringIntoView();
        Sta.Pump();
        model.Canvas.RenderNow();
        Sta.Pump();

        Assert.NotEmpty(model.Layers.Widgets);
        Assert.NotEmpty(model.Layers.Background);
        Assert.NotEmpty(model.Properties.Rows);
        Assert.NotEmpty(model.Properties.Sections);
        Assert.True(model.TestValues.Rows.Count > 3);

        var root = (FrameworkElement)window.Content;
        var picture = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        picture.Render(root);
        Assert.True(picture.PixelWidth > 1000);
        if (Environment.GetEnvironmentVariable(SnapshotFolderVariable) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using var file = File.Create(Path.Combine(folder, "editor-panels-showcase.png"));
            encoder.Save(file);
        }

        window.Close();
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

    static object Panel(MainWindow window, string name) => ((ContentControl)window.FindName(name)).Content;

    static MainWindow Show(TestEnvironment environment, string theme, int height = 860)
    {
        var ui = SynchronizationContext.Current!;
        var workspace = EditorWorkspace.Open(theme, environment.Environment, ui)!;
        var window = new MainWindow(new MainViewModel(environment.Environment, workspace, ui))
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            Width = 1400,
            Height = height,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();
        Sta.Pump();
        return window;
    }
}
