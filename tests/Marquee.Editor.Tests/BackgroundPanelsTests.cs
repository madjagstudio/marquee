using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class BackgroundPanelsTests
{
    [Fact]
    public void Selecting_a_background_layer_shows_its_fill_in_properties() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Model.Layers.SelectBackground(0);
        Assert.Equal(PropertyTargetKind.Layer, editor.Model.Properties.Target);
        Assert.Contains(editor.Model.Properties.Rows, row => row.Name == "fill");
    });

    [Fact]
    public void A_hand_edit_removing_the_selected_layers_kind_keeps_common_properties() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        const string layout = "canvas: { orientation: landscape }\nbackground: [{ name: Sky, fill: '#102030' }]\nwidgets: []\n";
        var folder = environment.AddTheme("mine", layout);
        using var editor = new OpenEditor(environment, folder);
        editor.Model.Layers.SelectBackground(0);
        Assert.NotNull(editor.Model.Properties.Row("fill"));

        var handEdit = layout.Replace(", fill: '#102030'", "");
        File.WriteAllText(editor.Session.LayoutPath, handEdit);
        Assert.True(Sta.PumpUntil(() => editor.Session.Document.Text == handEdit));

        Assert.Equal(OpenEditor.P("background[0]"), editor.Session.SelectedLayer);
        Assert.Equal(PropertyTargetKind.Layer, editor.Model.Properties.Target);
        Assert.Null(editor.Model.Properties.Model!.Type);
        Assert.Null(editor.Model.Properties.Kind);
        Assert.Equal("Background: Layer", editor.Model.Properties.Heading);
        Assert.Equal(["opacity", "blend", "hidden", "locked", "name"], editor.Model.Properties.Rows.Select(row => row.Name));
        Assert.Null(editor.Model.Properties.Shader);
    });

    [Fact]
    public void Add_layer_fill_adds_one_above_the_selected_and_delete_removes_it() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Model.Layers.SelectBackground(0);
        var before = editor.Session.Document.Text;
        editor.Model.Layers.AddFillLayerCommand.Execute(null);
        Assert.Equal(OpenEditor.P("background[1]"), editor.Session.SelectedLayer);
        editor.Model.KeyDown(System.Windows.Input.Key.Delete, System.Windows.Input.ModifierKeys.None);
        Assert.Equal(before, editor.Session.Document.Text);
    });

    [Fact]
    public void Changing_orientation_asks_and_cancel_changes_nothing() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        environment.Dialogs.RearrangeAnswer = null;
        var before = editor.Session.Document.Text;
        editor.Model.Properties.SetChoice("canvas.orientation", "portrait");
        Assert.Equal(["portrait"], environment.Dialogs.RearrangeQuestions);
        Assert.Equal(before, editor.Session.Document.Text);
    });

    [Fact]
    public void The_window_renders_a_shader_layers_uniform_list() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = ShaderTheme(environment);
        using var editor = new OpenEditor(environment, folder);
        var window = new MainWindow(editor.Model)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = -20000, Width = 1400, Height = 1100,
            ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            editor.Model.Layers.SelectBackground(0);
            Sta.Pump();
            var shader = Assert.IsType<ShaderPanelViewModel>(editor.Model.Properties.Shader);
            Assert.Null(shader.Problem);
            Assert.Contains(shader.Uniforms, row => row.Name == "heat" && row.Components[0].Text == "cpu.load / 100");
            Assert.Contains(Descendants(window).OfType<TextBlock>(), block => block.Text == "CPU usage / 100");
            var raw = Descendants(window).OfType<Expander>().Single(group => group.Name == "RawExpression"
                && group.DataContext is ValuePickerViewModel picker && picker.AdvancedText == "cpu.load / 100");
            raw.IsExpanded = true;
            Sta.Pump();
            var heat = Descendants(window).OfType<TextBox>().Single(box =>
                box.DataContext is ValuePickerViewModel picker && picker.IsAdvanced && box.Text == "cpu.load / 100");
            heat.BringIntoView();
            Sta.Pump();
            var root = (FrameworkElement)window.Content;
            var picture = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            picture.Render(root);
            var panel = (PropertiesView)((ContentControl)window.FindName("PropertiesPanel")).Content;
            var origin = panel.TranslatePoint(new Point(), root);
            var area = new Int32Rect((int)origin.X, (int)origin.Y, (int)panel.ActualWidth, (int)panel.ActualHeight);
            var pixels = new byte[area.Width * area.Height * 4];
            picture.CopyPixels(area, pixels, area.Width * 4, 0);
            Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 0 && pixels[i * 4] < 180) > 100);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Orientation_answers_change_the_canvas_in_one_undo_step(bool rearrange) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        environment.Dialogs.RearrangeAnswer = rearrange;
        var before = editor.Session.Document.Text;
        editor.Model.Properties.SetChoice("canvas.orientation", "portrait");
        Assert.Contains("orientation: portrait", editor.Session.Document.Text);
        Assert.Equal(1, editor.Session.History.Count);
        Assert.Equal(rearrange ? new SkiaSharp.SKPoint(50, 150) : new SkiaSharp.SKPoint(100, 100), editor.Session.Widgets[0].LocalBounds.Location);
        var count = SetOrientation.PartlyOffCanvas(editor.Session);
        if (rearrange && count > 0) Assert.Equal(Resources.Strings.PartlyOffCanvas(count), editor.Model.Message);
        editor.Model.KeyDown(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.Control);
        Assert.Equal(before, editor.Session.Document.Text);
    });

    [Fact]
    public void Rearranging_reports_widgets_that_no_longer_fit() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("wide",
            "canvas: { orientation: landscape }\nwidgets:\n  - { type: shape, shape: rect, at: [0, 0], size: [600, 100] }\n"));
        environment.Dialogs.RearrangeAnswer = true;
        editor.Model.Properties.SetChoice("canvas.orientation", "portrait");
        Assert.Equal(Resources.Strings.PartlyOffCanvas(1), editor.Model.Message);
    });

    [Fact]
    public void Background_drag_stays_in_its_section_and_locked_delete_is_refused() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var layers = editor.Model.Layers;
        layers.AddFillLayerCommand.Execute(null);
        editor.Model.Properties.Row("fill")!.Text = "#112233";
        Assert.True(editor.Model.Properties.Row("fill")!.Commit());
        var before = editor.Session.Document.Text;
        var bottom = layers.Find(OpenEditor.P("background[0]"))!;
        var top = layers.Find(OpenEditor.P("background[1]"))!;
        Assert.Null(layers.DropFor(bottom, layers.Widgets[0], DropPlace.Above));
        Assert.Null(layers.DropFor(layers.Widgets[0], bottom, DropPlace.Above));
        Assert.True(layers.Drop(bottom, top, DropPlace.Above));
        Assert.Equal(OpenEditor.P("background[1]"), editor.Session.SelectedLayer);
        editor.Model.KeyDown(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.Control);
        Assert.Equal(before, editor.Session.Document.Text);
        layers.SelectBackground(0);
        layers.SetLocked(layers.Find(OpenEditor.P("background[0]"))!, true);
        before = editor.Session.Document.Text;
        layers.DeleteCommand.Execute(null);
        Assert.Equal(before, editor.Session.Document.Text);
        Assert.Contains("locked", editor.Model.Message);
        Assert.Null(layers.DropFor(layers.Find(OpenEditor.P("background[0]"))!, layers.Find(OpenEditor.P("background[1]"))!, DropPlace.Above));
    });

    [Fact]
    public void Kind_switch_imports_a_file_and_cancel_and_refusal_keep_the_layer() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        editor.Model.Layers.SelectBackground(0);
        var before = editor.Session.Document.Text;
        editor.Model.Properties.Kind = LayerKindChoice.Image;
        Assert.Equal(before, editor.Session.Document.Text);
        environment.Dialogs.LayerFileAnswers[LayerKindChoice.Image] = Path.Combine(environment.Root, "missing.png");
        editor.Model.Properties.Kind = LayerKindChoice.Image;
        Assert.Single(environment.Dialogs.Errors);
        Assert.Equal(before, editor.Session.Document.Text);
        var image = Path.Combine(environment.Root, "picture.png");
        TestEnvironment.WritePng(image);
        environment.Dialogs.LayerFileAnswers[LayerKindChoice.Image] = image;
        editor.Model.Properties.Kind = LayerKindChoice.Image;
        Assert.Equal(LayerKindChoice.Image, editor.Model.Properties.Kind);
        Assert.Contains("image: images/picture.png", editor.Session.Document.Text);
        Assert.Contains(editor.Model.Properties.Rows, row => row.Name == "fit");
        Assert.Equal(1, editor.Session.History.Count);
        editor.Model.KeyDown(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.Control);
        Assert.Equal(before, editor.Session.Document.Text);
        editor.Model.Layers.AddImageLayerCommand.Execute(null);
        Assert.Equal(OpenEditor.P("background[1]"), editor.Session.SelectedLayer);
        Assert.Equal(LayerKindChoice.Image, editor.Model.Properties.Kind);
        editor.Model.Layers.SelectBackground(0);
        editor.Model.Layers.SetLocked(editor.Model.Layers.Find(OpenEditor.P("background[0]"))!, true);
        before = editor.Session.Document.Text;
        var refused = Path.Combine(environment.Root, "locked.png");
        TestEnvironment.WritePng(refused);
        environment.Dialogs.LayerFileAnswers[LayerKindChoice.Image] = refused;
        editor.Model.Properties.Kind = LayerKindChoice.Image;
        Assert.Equal(before, editor.Session.Document.Text);
        Assert.False(File.Exists(Path.Combine(editor.Session.Folder, "images", "locked.png")));
    });

    [Fact]
    public void Uniforms_validate_every_component_and_keep_their_boxes_across_writes() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = ShaderTheme(environment);
        File.WriteAllText(Path.Combine(folder, "shaders", "aurora.sksl"), "uniform float2 heat; half4 main(float2 p) { return half4(heat.x, heat.y, 0, 1); }");
        using var editor = new OpenEditor(environment, folder);
        editor.Model.Layers.SelectBackground(0);
        var shader = editor.Model.Properties.Shader!;
        var heat = Assert.Single(shader.Uniforms);
        Assert.True(heat.IsEditable);
        var before = editor.Session.Document.Text;
        heat.Components[0].Text = "cpu.load /";
        heat.Components[1].Text = "0.5";
        Assert.True(heat.Components[0].HasError);
        Assert.False(heat.Components[1].Commit());
        Assert.Equal(before, editor.Session.Document.Text);
        heat.Components[0].Text = "cpu.load / 100";
        Assert.True(heat.Components[0].Commit());
        Assert.Same(heat, Assert.Single(shader.Uniforms));
        Assert.Contains("[\"cpu.load / 100\", 0.5]", editor.Session.Document.Text);
        Assert.Equal(1, editor.Session.History.Count);
        Assert.True(heat.Components[1].Commit());
        Assert.Equal(1, editor.Session.History.Count);
        editor.Model.KeyDown(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.Control);
        Assert.Equal(before, editor.Session.Document.Text);
    });

    [Fact]
    public void A_read_only_shader_shows_its_bindings_without_writing_them() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var source = ShaderTheme(environment);
        var folder = environment.AddTheme("locked", File.ReadAllText(Path.Combine(source, "layout.yaml")), installed: true);
        Directory.CreateDirectory(Path.Combine(folder, "shaders"));
        File.Copy(Path.Combine(source, "shaders", "aurora.sksl"), Path.Combine(folder, "shaders", "aurora.sksl"));
        environment.Dialogs.CopyAnswer = Session.CopyChoice.ViewOnly;
        using var editor = new OpenEditor(environment, folder);
        editor.Model.Layers.SelectBackground(0);
        var before = editor.Session.Document.Text;
        var heat = Assert.Single(editor.Model.Properties.Shader!.Uniforms, row => row.Name == "heat");
        Assert.False(heat.IsEditable);
        heat.Components[0].Text = "0.5";
        Assert.False(heat.Commit());
        editor.Model.Layers.AddFillLayerCommand.Execute(null);
        Assert.Equal(before, editor.Session.Document.Text);
    });

    [Fact]
    public void Unused_bindings_can_be_removed_and_cost_is_posted_after_debounce() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var folder = ShaderTheme(environment);
        File.WriteAllText(Path.Combine(folder, "shaders", "aurora.sksl"), "half4 main(float2 p) { return half4(1); }");
        using var editor = new OpenEditor(environment, folder);
        editor.Model.Layers.SelectBackground(0);
        var shader = editor.Model.Properties.Shader!;
        var unused = Assert.Single(shader.Uniforms);
        Assert.True(unused.IsUnused);
        Assert.Null(shader.Cost);
        Assert.True(Sta.PumpUntil(() => shader.Cost is not null));
        Assert.Equal(shader.Cost!.PerFrame > shader.Cost.Budget, shader.OverBudget);
        editor.Model.Properties.Row("scale")!.Text = "0.5";
        Assert.True(editor.Model.Properties.Row("scale")!.Commit());
        Assert.Null(shader.Cost);
        Assert.True(Sta.PumpUntil(() => shader.Cost is not null));
        unused.RemoveCommand.Execute(null);
        Assert.Empty(shader.Uniforms);
        Assert.False(editor.Session.Document.Exists(OpenEditor.P("background[0].uniforms.heat")));
    });

    static string ShaderTheme(TestEnvironment environment)
    {
        var folder = environment.AddTheme("glow", "canvas: { orientation: landscape }\nbackground:\n  - shader: shaders/aurora.sksl\n    uniforms: { heat: \"cpu.load / 100\" }\nwidgets: []\n");
        Directory.CreateDirectory(Path.Combine(folder, "shaders"));
        File.Copy(Path.Combine(Repository(), "themes", "example-agents", "shaders", "aurora.sksl"), Path.Combine(folder, "shaders", "aurora.sksl"));
        return folder;
    }

    static string Repository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var inner in Descendants(child)) yield return inner;
        }
    }
}
