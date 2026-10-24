using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;
using static Marquee.Editor.Tests.TestSupport.OpenEditor;

namespace Marquee.Editor.Tests;

public class CanvasVisibilityTests
{
    const string Layout = """
        canvas: { orientation: landscape }
        widgets:
          - { type: text, at: [50, 50], size: [100, 40], text: Hello, style: { size: 1 } }
          - { type: text, at: [200, 50], size: [100, 40], text: Visible }
        """;
    const string Warning = "This text is 1px, too small to see";

    [Fact]
    public void Transparency_warning_follows_rules_when_pinned_values_change() => Sta.Run(() =>
    {
        const string layout = """
            canvas: { orientation: landscape }
            styles: { gone: { opacity: 0 } }
            widgets:
              - type: value
                at: [50, 50]
                size: [100, 40]
                source: cpu.temp
                style: gone
                when: [{ above: 50, style: { opacity: 1 } }]
            """;
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", layout));
        var window = Show(editor);
        try
        {
            editor.Session.Select([P("widgets[0]")]);
            var reading = editor.Model.TestValues.Row("cpu.temp")!;
            foreach (var value in new[] { "80", "20", "80" })
            {
                reading.NumberText = value;
                Assert.True(reading.CommitNumber());
                var expected = value == "20" ? "This is fully transparent" : null;
                Assert.True(Sta.PumpUntil(() => editor.Canvas.VisibilityWarning == expected));
                Assert.Equal(expected, Model.Visibility.Problems(editor.Session, P("widgets[0]")).FirstOrDefault());
                Assert.Equal(expected, editor.Model.StatusMessage);
                Assert.Equal(expected is null ? 0 : 2,
                    Descendants(window).OfType<TextBlock>().Count(t => t.Text == "This is fully transparent"));
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Selection_draws_amber_dashes_and_repeats_the_first_warning_in_status() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        var window = Show(editor);
        try
        {
            editor.Session.Select([P("widgets[0]")]);
            Sta.Pump();
            var canvas = Descendants(window).OfType<CanvasView>().Single();
            var overlay = (System.Windows.Controls.Canvas)canvas.FindName("Overlay");
            var outline = Assert.Single(overlay.Children.OfType<Polygon>());
            Assert.NotEmpty(outline.StrokeDashArray);
            Assert.Equal(((SolidColorBrush)window.FindResource("Warning")).Color, ((SolidColorBrush)outline.Stroke).Color);
            Assert.Equal(Warning, Assert.Single(overlay.Children.OfType<TextBlock>()).Text);
            Assert.Equal(2, Descendants(window).OfType<TextBlock>().Count(t => t.Text == Warning));

            Assert.True(editor.Model.Run(new SetValue(P("widgets[0].style.size"), "24")));
            Sta.Pump();
            Assert.Empty(Assert.Single(overlay.Children.OfType<Polygon>()).StrokeDashArray);
            Assert.DoesNotContain(Descendants(window).OfType<TextBlock>(), t => t.Text == Warning);

            editor.Session.Undo();
            Sta.Pump();
            Assert.Equal(2, Descendants(window).OfType<TextBlock>().Count(t => t.Text == Warning));
            editor.Session.Select([P("widgets[1]")]);
            Sta.Pump();
            Assert.DoesNotContain(Descendants(window).OfType<TextBlock>(), t => t.Text == Warning);
            editor.Session.Select([]);
            Sta.Pump();
            Assert.Empty(overlay.Children.OfType<Polygon>());
            Assert.Empty(overlay.Children.OfType<TextBlock>());
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Multiple_selection_marks_only_the_widget_with_a_problem() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", Layout));
        var window = Show(editor);
        try
        {
            editor.Session.Select([P("widgets[0]"), P("widgets[1]")]);
            Sta.Pump();
            var view = Descendants(window).OfType<CanvasView>().Single();
            var overlay = (System.Windows.Controls.Canvas)view.FindName("Overlay");
            Assert.Single(overlay.Children.OfType<Polygon>(), p => p.StrokeDashArray.Count > 0);
            Assert.Single(overlay.Children.OfType<Polygon>(), p => p.StrokeDashArray.Count == 0);
            Assert.Single(overlay.Children.OfType<TextBlock>());
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
