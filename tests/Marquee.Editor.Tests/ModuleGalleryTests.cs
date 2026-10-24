using Marquee.Editor.Modules;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class ModuleGalleryTests
{
    [Fact]
    public void A_broken_saved_module_can_be_removed_without_losing_the_builtin_gallery() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine"));
        var folder = environment.Environment.Modules.Folder("Broken"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "module.yaml"), "type: not-a-widget\nsize: [80, 80]\n");
        using var gallery = editor.Model.CreateModuleGallery()!;
        Assert.NotNull(gallery);
        Assert.Equal(16, gallery.Categories.Where(c => c.Category != Model.ModuleCategory.MyModules).Sum(c => c.Tiles.Count));
        var mine = gallery.Categories.Single(c => c.Category == Model.ModuleCategory.MyModules);
        var broken = Assert.Single(mine.Tiles); Assert.NotNull(broken.Bitmap);
        var before = editor.Session.Document.Text;
        editor.Model.AddGalleryModule((Model.Module)broken.Item!);
        Assert.Single(environment.Dialogs.Errors); Assert.Equal(before, editor.Session.Document.Text);
        gallery.Delete("Broken"); Assert.Empty(mine.Tiles); Assert.False(Directory.Exists(folder));
    });
    [Fact]
    public void Saved_selection_appears_in_my_modules_and_can_be_renamed_deleted_and_added() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("mine", "canvas: { orientation: landscape }\nwidgets: [{ type: text, at: [0, 0], size: [200, 80], text: Hello }]\n"));
        editor.Session.Select([editor.Session.Widgets[0].Path]);
        editor.Model.SaveSelectedModule("Greeting");
        using var gallery = editor.Model.CreateModuleGallery()!;
        var mine = gallery.Categories.Single(c => c.Category == Model.ModuleCategory.MyModules);
        var tile = Assert.Single(mine.Tiles); Assert.NotNull(tile.Bitmap);
        editor.Model.AddGalleryModule((Model.Module)tile.Item!);
        Assert.Equal(2, editor.Session.Widgets.Count);
        gallery.Rename("Greeting", "Welcome");
        Assert.Equal("Welcome", Assert.Single(mine.Tiles).Name);
        gallery.Delete("Welcome"); Assert.Empty(mine.Tiles);
    });
    [Fact]
    public void Gallery_can_read_a_sensor_not_yet_used_in_the_theme() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("empty", "canvas: { orientation: landscape }\nwidgets: []\n"));
        Assert.True(editor.Workspace.Values.Snapshot(["cpu.temp"]).TryGet("cpu.temp", out var value));
        Assert.Equal(52, value.Number);
        Assert.Empty(editor.Session.Widgets);
    });
    [Fact]
    public void Gallery_has_plain_named_modules_and_all_categories() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("modules"));
        using var gallery = new ModuleGalleryViewModel(editor.Session.Folder, editor.Session.Values);
        Assert.Equal(new[] { "Numbers", "Gauges", "Graphs", "Time", "Pictures", "Shapes", "My modules", "Blank widgets" }, gallery.Categories.Select(c => c.Name));
        Assert.Contains(gallery.Categories.SelectMany(c => c.Tiles), tile => tile.Name == "Temperature dial");
        gallery.Refresh();
        Assert.All(gallery.Categories.SelectMany(c => c.Tiles), tile => Assert.NotNull(tile.Bitmap));
    });
}
