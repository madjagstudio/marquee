using System.Runtime.CompilerServices;
using Marquee.Core.Themes;
using Marquee.Editor.Start;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class StartScreenTests
{
    [Fact]
    public void Monitor_preview_draws_a_trace_when_sensors_have_no_history() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        environment.AddExample("start-monitor", installed: true);
        using var start = new StartViewModel(environment.Environment);
        var bitmap = Assert.Single(start.Tiles).Bitmap!;
        var pixels = GalleryPlaybackTests.Pixels(bitmap);
        // Interior of the Load graph at [780, 130], size [320, 140], on the 1920 x 480 canvas.
        var trace = 0;
        for (var y = 19; y < 36; y++)
            for (var x = 109; x < 150; x++)
            {
                var p = pixels[y * bitmap.PixelWidth + x];
                // The 2px stroke is subpixel at thumbnail size: antialiasing blends cyan with the track.
                if (((p >> 16) & 255) < 100 && ((p >> 8) & 255) > 120 && (p & 255) > 120) trace++;
            }
        Assert.True(trace > 10, "The monitor's graph must contain cyan trace pixels, not just its dark track.");
    });

    [Theory]
    [InlineData("start-blank")]
    [InlineData("start-gaming")]
    [InlineData("start-monitor")]
    public void Every_start_loads_clean(string name)
    {
        using var theme = ThemeLoader.Load(Folder(name));
        Assert.Empty(theme.Warnings);
    }

    [Theory]
    [InlineData("landscape", ThemeOrientation.Landscape)]
    [InlineData("portrait", ThemeOrientation.Portrait)]
    public void Choosing_gaming_copies_the_named_theme_in_the_screens_orientation(string orientation, ThemeOrientation expected) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        environment.AddExample("start-gaming", installed: true);
        File.WriteAllText(environment.ConfigFile, "orientation: " + orientation + "\n");
        using var start = new StartViewModel(environment.Environment);
        var folder = start.Create("start-gaming", "My game screen");
        using var editor = new OpenEditor(environment, folder);
        Assert.Equal("My game screen", editor.Model.ThemeName);
        Assert.Equal(expected, editor.Session.Theme!.Spec.Canvas.Orientation);
        Assert.Equal(3, editor.Session.Widgets.Count);
        Assert.Empty(editor.Session.Theme.Warnings);
        Assert.StartsWith(environment.UserThemes, folder);
        using var original = ThemeLoader.Load(Path.Combine(environment.InstalledThemes, "start-gaming"));
        Assert.Equal(ThemeOrientation.Landscape, original.Spec.Canvas.Orientation);
    });

    static string Folder(string name, [CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../themes", name));
}
