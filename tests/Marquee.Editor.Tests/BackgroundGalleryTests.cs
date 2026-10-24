using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Controls;
using Marquee.Editor.Resources;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class BackgroundGalleryTests
{
    [Fact]
    public void An_unfinished_reaction_survives_a_values_refresh_and_shows_unknown() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("draft"));
        BackgroundLibrary.Use(editor.Session, Path.Combine(Folder(), "aurora.sksl"), null);
        var panel = editor.Model.Properties.Shader!;
        var view = new ShaderControlsView { DataContext = panel };
        var window = new Window { Content = view, Width = 400, Height = 800, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); Sta.Pump();
        try
        {
            panel.Uniforms.Single(row => row.Name == "heat").Components[0].Text = "cpu.load /";
            editor.Workspace.Values.TestValues.Pin("cpu.load", TestPin.Of(80));
            Sta.Pump();
            var meter = Walk(view).OfType<ProgressBar>().First();
            Assert.True(meter.IsIndeterminate);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("#FF0000")]
    [InlineData("#FF000080")]
    public void Shader_colour_picker_writes_red_RGB_without_a_transparency_control(string hex) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("colour"));
        BackgroundLibrary.Use(editor.Session, Path.Combine(Folder(), "aurora.sksl"), null);
        var panel = editor.Model.Properties.Shader!;
        var view = new ShaderControlsView { DataContext = panel };
        var window = new Window { Content = view, Width = 400, Height = 800, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); Sta.Pump();
        try
        {
            Walk(view).OfType<Button>().Single(b => b.Name == "ColourSwatch").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Sta.Pump();
            var picker = PresentationSource.CurrentSources.Cast<PresentationSource>().Where(s => s.RootVisual is not null)
                .SelectMany(s => Walk(s.RootVisual)).OfType<ColorPicker>().Single(c => c.IsVisible);
            var alphaVisible = ((FrameworkElement)picker.FindName("AlphaStrip")).IsVisible;
            var box = (TextBox)picker.FindName("HexBox");
            box.Text = hex;
            box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Assert.Equal(new[] { "1", "0", "0" }, panel.Uniforms.Single(r => r.Name == "tint").Components.Select(c => c.Text));
            Assert.False(alphaVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_background_copy_failure_is_reported_without_changing_the_theme() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("copy-failure"));
        var before = editor.Session.Document.Text;
        File.WriteAllText(Path.Combine(editor.Session.Folder, "shaders"), "This file prevents creating the shader directory.");
        var entry = new BackgroundEntry(Path.Combine(Folder(), "aurora.sksl"), Marquee.Core.Themes.ShaderControls.Read(File.ReadAllText(Path.Combine(Folder(), "aurora.sksl"))));
        editor.Model.ChooseBackground(entry, null, false);
        Assert.Single(environment.Dialogs.Errors); Assert.Equal(before, editor.Session.Document.Text);
    });

    [Theory]
    [InlineData("speed")]
    [InlineData("strength")]
    [InlineData("tint")]
    public void Annotated_controls_refuse_sensor_bindings_even_in_raw_settings(string name) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("constant"));
        BackgroundLibrary.Use(editor.Session, Path.Combine(Folder(), "aurora.sksl"), null);
        var panel = editor.Model.Properties.Shader!;
        var component = panel.Uniforms.Single(row => row.Name == name).Components[0];
        var before = editor.Session.Document.Text;
        component.Text = "cpu.load";
        Assert.False(component.Commit());
        Assert.NotNull(component.Error); Assert.Equal(before, editor.Session.Document.Text);
        Assert.DoesNotContain(component.Picker.Modes, mode => mode.Mode == ValuePickerMode.Sensor);
        var heat = panel.Uniforms.Single(row => row.Name == "heat").Components[0];
        heat.Text = "gpu.load / 100"; Assert.True(heat.Commit());
    });

    [Theory]
    [InlineData("SpeedSlider")]
    [InlineData("StrengthSlider")]
    [InlineData("QualitySlider")]
    public void One_shader_slider_drag_is_one_undo_step(string name) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("slider"));
        BackgroundLibrary.Use(editor.Session, Path.Combine(Folder(), "aurora.sksl"), null);
        var view = new ShaderControlsView { DataContext = editor.Model.Properties.Shader };
        var window = new Window { Content = view, Width = 400, Height = 800, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); Sta.Pump();
        try
        {
            var before = editor.Session.Document.Text;
            var slider = Walk(view).OfType<Slider>().Single(s => s.Name == name);
            slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            slider.Value = .3; slider.Value = .6; slider.Value = .9;
            slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Assert.True(editor.Session.Undo()); Assert.Equal(before, editor.Session.Document.Text);
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reaction_picker_offers_sensors_for_the_default_and_after_nothing(bool nothingFirst) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("reaction"));
        BackgroundLibrary.Use(editor.Session, Path.Combine(Folder(), "aurora.sksl"), null);
        var view = new ShaderControlsView { DataContext = editor.Model.Properties.Shader };
        var window = new Window { Content = view, Width = 400, Height = 800, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); Sta.Pump();
        try
        {
            var chip = Walk(view).OfType<Button>().Single(b => Equals(b.ToolTip, Strings.ShaderReactTip));
            chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Sta.Pump();
            if (nothingFirst)
            {
                PopupNodes().OfType<Button>().Single(b => Equals(b.Content, Strings.ShaderNothing)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Sta.Pump(); chip.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Sta.Pump();
            }
            var control = PopupNodes().OfType<ValuePickerControl>().Single(c => c.IsVisible);
            var picker = Assert.IsType<ValuePickerViewModel>(control.DataContext);
            Assert.True(picker.IsSensor);
            Assert.Contains(picker.Keys, key => key.Key == "gpu.temp");
            picker.SelectedKey = "gpu.temp"; Sta.Pump();
            Assert.Contains("gpu.temp", editor.Session.Document.Text);
            Assert.DoesNotContain("speed: gpu.temp", editor.Session.Document.Text);
        }
        finally { window.Close(); }
        static IEnumerable<DependencyObject> PopupNodes() => PresentationSource.CurrentSources.Cast<PresentationSource>()
            .Where(source => source.RootVisual is not null).SelectMany(source => Walk(source.RootVisual));
    });
    internal static string Folder([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../themes/backgrounds"));

    [Fact]
    public void Gallery_tiles_draw_and_stop_when_disposed() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("gallery"));
        using var gallery = new BackgroundGalleryViewModel(new BackgroundLibrary(Folder()), editor.Session.Values);
        Assert.Equal(6, gallery.Tiles.Count);
        Assert.Contains(gallery.Tiles, tile => tile.Name == "Nebula");
        gallery.Refresh();
        var bitmap = gallery.Tiles[0].Bitmap!;
        Assert.Equal(240, bitmap.PixelWidth);
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert.True(pixels.Distinct().Count() > 1);
        gallery.Start();
        Assert.True(gallery.IsRunning);
        gallery.Dispose();
        Assert.False(gallery.IsRunning);
    });

    [Fact]
    public void Annotated_panel_shows_visual_controls_and_cpu_chip_without_raw_heat() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("controls"));
        BackgroundLibrary.Use(editor.Session, Path.Combine(Folder(), "aurora.sksl"), null);
        var view = new ShaderControlsView { DataContext = editor.Model.Properties.Shader };
        var window = new Window { Content = view, Width = 400, Height = 800, Left = -10000, Top = -10000, ShowInTaskbar = false };
        window.Show(); Sta.Pump();
        try
        {
            var nodes = Walk(view).ToList();
            Assert.Contains(nodes.OfType<Button>(), button => button.Name == "ColourSwatch");
            Assert.Contains(nodes.OfType<Slider>(), slider => slider.Name == "SpeedSlider");
            Assert.Contains(nodes.OfType<Slider>(), slider => slider.Name == "StrengthSlider");
            Assert.Contains(nodes.OfType<Button>(), button => button.Content?.ToString()?.Contains("CPU load") == true);
            Assert.DoesNotContain(nodes.OfType<TextBlock>(), text => text.Text.Contains("heat"));
            Assert.All(nodes.OfType<Slider>(), slider => Assert.NotNull(slider.ToolTip));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(4, "Light")]
    [InlineData(7, "Heavy")]
    [InlineData(9.5, "Heavy")]
    [InlineData(10, "Heavy")]
    [InlineData(10.5, "TooHeavy")]
    public void Cost_bar_uses_the_frame_budget(double cost, string expected) =>
        Assert.Equal(expected, ShaderCostPresentation.State(new CostHint(TimeSpan.FromMilliseconds(cost), TimeSpan.FromMilliseconds(10))));

    static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in Walk(VisualTreeHelper.GetChild(root, i))) yield return node;
    }
}
