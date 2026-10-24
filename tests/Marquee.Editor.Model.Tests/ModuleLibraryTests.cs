using System.Runtime.CompilerServices;
using Marquee.Core.Sensors;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

public class ModuleLibraryTests
{
    [Fact]
    public void Every_module_loads_without_warnings_and_draws()
    {
        Assert.Equal(16, ModuleLibrary.All.Count);
        foreach (var module in ModuleLibrary.All)
        {
            using var theme = TempTheme.With("canvas: { orientation: landscape }\nwidgets: []\n");
            Directory.CreateDirectory(Path.Combine(theme.Folder, "images"));
            using (var bitmap = new SKBitmap(8, 8))
            {
                bitmap.Erase(SKColors.Coral);
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(theme.Folder, "images/picture.png"), data.ToArray());
            }
            Directory.CreateDirectory(Path.Combine(theme.Folder, "video"));
            File.Copy(Video(), Path.Combine(theme.Folder, "video/clip.mp4"));
            using var session = theme.Open();
            session.Apply(new AddModule(module, new SKPoint(50, 50), null));
            Assert.Single(session.Widgets);
            Assert.Empty(session.Theme!.Warnings);
            var store = new SensorStore();
            foreach (var key in new[] { "cpu.temp", "cpu.load", "gpu.load", "ram.used_pct" }) store.Set(key, 55);
            session.Values = store.Snapshot;
            using var frame = session.RenderFrame(TimeSpan.FromSeconds(1))!;
            using var pixels = SKBitmap.FromImage(frame);
            Assert.True(pixels.Pixels.Distinct().Take(2).Count() > 1, module.Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Adding_preserves_other_bytes_selects_places_and_undoes_once(bool drop)
    {
        const string before = "# preserve this\r\ncanvas: { orientation: landscape } # screen\r\nwidgets: []\r\n";
        using var theme = TempTheme.With(before);
        using var session = theme.Open();
        session.Apply(new AddModule(ModuleLibrary.All.Single(m => m.Id == "big-number"), drop ? new SKPoint(123, 87) : null, null));
        var widget = Assert.Single(session.Widgets);
        Assert.Equal(widget.Path, Assert.Single(session.Selection));
        Assert.Contains("# preserve this\r\ncanvas: { orientation: landscape } # screen\r\n", session.Document.Text);
        Assert.Equal(drop ? 123 : (1920 - widget.LocalBounds.Width) / 2, widget.LocalBounds.Left);
        Assert.Equal(drop ? 87 : (480 - widget.LocalBounds.Height) / 2, widget.LocalBounds.Top);
        Assert.True(session.Undo()); Assert.Equal(before, session.Document.Text); Assert.False(session.CanUndo);
    }

    static string Video([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../themes/example-showcase/video/radar.mp4"));
}
