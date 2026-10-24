using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using System.Runtime.CompilerServices;

namespace Marquee.Editor.Model.Tests;

public class BackgroundLibraryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Choosing_a_background_carries_defaults_and_is_one_undo_step(bool replace)
    {
        const string before = "# keep me\ncanvas: { orientation: landscape }\nbackground:\n  - fill: \"#112233\"\nwidgets: []\n";
        using var folder = TempTheme.With(before);
        using var session = folder.Open();
        var library = new BackgroundLibrary(Folder());
        Assert.Equal(6, library.All.Count);
        var aurora = Assert.Single(library.All, entry => entry.Info.Name == "Aurora");
        BackgroundLibrary.Use(session, aurora.File, replace ? YamlPath.Parse("background[0]") : null);
        var layer = session.SelectedLayer!;
        Assert.True(session.Document.TryGetScalar(layer.Child("shader"), out var path));
        Assert.True(File.Exists(Path.Combine(folder.Folder, path)));
        Assert.True(session.Document.TryGetScalar(layer.Child("uniforms").Child("speed"), out var speed));
        Assert.Equal("1", speed);
        Assert.True(session.Document.TryGetScalar(layer.Child("uniforms").Child("heat"), out var heat));
        Assert.Equal("cpu.load / 100", heat);
        Assert.Equal(3, session.Document.GetNode(layer.Child("uniforms").Child("tint"))!.Count);
        Assert.Empty(session.Theme!.Warnings);
        Assert.True(session.Undo());
        Assert.Equal(before, session.Document.Text);
        Assert.False(session.CanUndo);
    }
    static string Folder([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../themes/backgrounds"));
}
