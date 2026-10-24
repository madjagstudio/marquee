using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Editor.Properties;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class BackgroundGalleryTests
{
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
    [InlineData(10, "TooHeavy")]
    public void Cost_bar_uses_the_frame_budget(double cost, string expected) =>
        Assert.Equal(expected, ShaderCostPresentation.State(new CostHint(TimeSpan.FromMilliseconds(cost), TimeSpan.FromMilliseconds(10))));

    static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in Walk(VisualTreeHelper.GetChild(root, i))) yield return node;
    }
}
