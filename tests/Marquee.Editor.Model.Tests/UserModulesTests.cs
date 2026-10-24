using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Editor.Model.Tests;

public class UserModulesTests
{
    static readonly YamlPath First = YamlPath.Root.Child("widgets").Item(0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_group_resolves_named_and_inherited_styles_and_draws_identically(bool childOnly)
    {
        using var source = TempTheme.With("""
            canvas: { orientation: landscape }
            styles:
              ink: { color: "#FF8844", size: 36 }
              title: { extends: ink, weight: bold }
            widgets:
              - type: group
                at: [0, 0]
                size: [300, 100]
                style: title
                children:
                  - { type: text, at: [0, 0], size: [300, 100], text: Hello, style: { extends: ink, italic: true } }
            """);
        using var target = TempTheme.With("canvas: { orientation: landscape }\nwidgets: []\n");
        using var storage = TempTheme.With("canvas: { orientation: landscape }\n");
        var modules = new UserModules(Path.Combine(storage.Folder, "modules"));
        using var before = source.Open();
        modules.Save(before, childOnly ? First.Child("children").Item(0) : First, "Greeting");
        Assert.DoesNotContain("extends", Assert.Single(modules.All()).Yaml);
        using var after = target.Open();
        modules.Add(after, "Greeting", new SKPoint(0, 0));
        Assert.Empty(after.Theme!.Warnings);
        using var a = before.RenderFrame(TimeSpan.Zero)!;
        using var b = after.RenderFrame(TimeSpan.Zero)!;
        using var ap = SKBitmap.FromImage(a); using var bp = SKBitmap.FromImage(b);
        Assert.Equal(ap.Pixels, bp.Pixels);
        Assert.True(after.Undo()); Assert.Empty(after.Widgets); Assert.False(after.CanUndo);
    }

    [Fact]
    public void An_image_travels_to_another_theme_under_a_free_name()
    {
        using var source = TempTheme.With("canvas: { orientation: landscape }\nwidgets: [{ type: image, at: [0, 0], size: [80, 80], src: picture.png }]\n");
        using var storage = TempTheme.With("canvas: { orientation: landscape }\n");
        using var target = TempTheme.With("canvas: { orientation: landscape }\nwidgets: []\n");
        using var pixels = new SKBitmap(8, 8); pixels.Erase(SKColors.Coral);
        using var image = pixels.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(source.Folder, "picture.png"), image.ToArray());
        var modules = new UserModules(Path.Combine(storage.Folder, "modules"));
        using (var session = source.Open()) modules.Save(session, First, "Picture");
        Assert.Contains("files/", Assert.Single(modules.All()).Yaml);
        File.Delete(Path.Combine(source.Folder, "picture.png"));
        Directory.CreateDirectory(Path.Combine(target.Folder, "images"));
        File.WriteAllText(Path.Combine(target.Folder, "images/picture.png"), "keep this");
        using var other = target.Open();
        modules.Add(other, "Picture", new SKPoint(30, 40));
        Assert.Empty(other.Theme!.Warnings);
        Assert.True(other.Document.TryGetScalar(First.Child("src"), out var path));
        Assert.Equal("images/picture-2.png", path);
        Assert.Equal(image.ToArray(), File.ReadAllBytes(Path.Combine(target.Folder, path)));
        Assert.Equal("keep this", File.ReadAllText(Path.Combine(target.Folder, "images/picture.png")));
    }

    [Fact]
    public void Names_are_checked_and_modules_can_be_renamed_and_deleted()
    {
        using var storage = TempTheme.With("canvas: { orientation: landscape }\nwidgets: [{ type: text, at: [0, 0], size: [100, 50], text: Hi }]\n");
        using var session = storage.Open();
        var modules = new UserModules(Path.Combine(storage.Folder, "modules"));
        Assert.NotEmpty(Assert.Throws<ArgumentException>(() => modules.Save(session, First, "")).Message);
        modules.Save(session, First, "Hello");
        Assert.NotEmpty(Assert.Throws<ArgumentException>(() => modules.Save(session, First, "Hello")).Message);
        Assert.Throws<ArgumentException>(() => modules.Rename("Hello", "../outside"));
        modules.Rename("Hello", "Welcome"); Assert.Equal("Welcome", Assert.Single(modules.All()).Name);
        modules.Delete("Welcome"); Assert.Empty(modules.All());
    }
}
