using System.Collections.Concurrent;
using Marquee.Core.Rendering;
using Marquee.Core.Themes;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

public class ThemeWatcherTests
{
    static string Fill(string color) => $"canvas: {{ orientation: landscape }}\nbackground:\n  - fill: \"{color}\"\n";

    const string Broken = "canvas: { orientation: landscape }\nbackground:\n  - fill: \"#00FF00\"\n    blend: sideways\n";

    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// Records callbacks from the watcher's thread so tests can wait for them.
    sealed class Events
    {
        public BlockingCollection<LoadedTheme> Loaded { get; } = [];
        public BlockingCollection<ThemeException> Failed { get; } = [];

        public ThemeWatcher Watch(string folder) => new(folder, Loaded.Add, Failed.Add);
    }

    [Fact]
    public void Reload_reports_a_good_theme()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        var events = new Events();
        using var watcher = events.Watch(folder.Folder);

        Assert.True(watcher.Reload());

        using var theme = Assert.Single(events.Loaded);
        Assert.Empty(events.Failed);
    }

    [Fact]
    public void Load_loads_once_and_turns_every_failure_into_a_ThemeException()
    {
        using var good = new TempTheme(Fill("#FF0000"));
        using var broken = new TempTheme(Broken);

        using (var theme = ThemeWatcher.Load(good.Folder, fpsOverride: 12)) Assert.Equal(good.Folder, theme.Folder);
        Assert.Equal(4, Assert.Throws<ThemeException>(() => ThemeWatcher.Load(broken.Folder)).Line);
        using (File.Open(good.LayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.StartsWith("Could not read the theme: ", Assert.Throws<ThemeException>(() => ThemeWatcher.Load(good.Folder)).Message);
    }

    [Fact]
    public void Reload_reports_a_bad_theme_with_its_line_and_loads_nothing()
    {
        using var folder = new TempTheme(Broken);
        var events = new Events();
        using var watcher = events.Watch(folder.Folder);

        Assert.False(watcher.Reload());

        Assert.Equal(4, Assert.Single(events.Failed).Line);
        Assert.Empty(events.Loaded);
    }

    [Fact]
    public void Reload_reports_a_theme_with_an_unresolvable_image_path_as_loaded()
    {
        using var folder = new TempTheme(Fill("#FF0000") + "  - image: \"a\\0.png\"\n");
        var events = new Events();
        using var watcher = events.Watch(folder.Folder);

        Assert.True(watcher.Reload());

        using var theme = Assert.Single(events.Loaded);
        Assert.Equal(["Image 'a\0.png' is not a valid path and was skipped."], theme.Warnings);
        Assert.Empty(events.Failed);
    }

    [Fact]
    public void Reload_without_retrying_reports_a_locked_layout_as_failed()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        var events = new Events();
        using var watcher = events.Watch(folder.Folder);

        using (File.Open(folder.LayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(watcher.Reload(retryIfLocked: false));

        Assert.StartsWith("Could not read the theme: ", Assert.Single(events.Failed).Message);
        Assert.Empty(events.Loaded);
    }

    [Fact]
    public void Saving_the_layout_reloads_it()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        var events = new Events();
        using var watcher = events.Watch(folder.Folder);
        watcher.Start();

        folder.Write(Fill("#0000FF"));

        Assert.True(events.Loaded.TryTake(out var theme, Timeout), "No reload after saving layout.yaml");
        using (theme)
            Assert.Equal(new SolidPaint(SKColors.Blue), ((FillLayer)theme!.Spec.Background[0]).Paint);
    }

    [Fact]
    public void A_throwing_loaded_callback_is_reported_through_failed_during_a_hot_reload()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        var failed = new BlockingCollection<ThemeException>();
        using var watcher = new ThemeWatcher(folder.Folder, theme =>
        {
            theme.Dispose();
            throw new InvalidOperationException("boom");
        }, failed.Add);
        watcher.Start();

        folder.Write(Fill("#0000FF"));

        Assert.True(failed.TryTake(out var error, Timeout), "No error after the loaded callback threw");
        Assert.StartsWith("Could not apply the theme:", error!.Reason);
    }

    [Fact]
    public void A_broken_save_reports_the_error_and_the_fix_reloads()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        var events = new Events();
        using var watcher = events.Watch(folder.Folder);
        watcher.Start();

        folder.Write(Broken);
        Assert.True(events.Failed.TryTake(out var error, Timeout), "No error after a broken save");
        Assert.Equal("layout.yaml line 4: blend must be one of: normal, multiply, screen, overlay, darken, lighten, add; not 'sideways'", error!.Message);

        folder.Write(Fill("#0000FF"));
        Assert.True(events.Loaded.TryTake(out var theme, Timeout), "No reload after fixing the layout");
        theme!.Dispose();
    }

    [Fact]
    public void Changing_an_asset_reloads_the_theme()
    {
        using var folder = new TempTheme(Fill("#FF0000") + "  - image: sky.png\n");
        var events = new Events();
        using var watcher = events.Watch(folder.Folder);
        watcher.Start();

        folder.AddImage("sky.png", SKColors.Blue);

        Assert.True(events.Loaded.TryTake(out var theme, Timeout), "No reload after adding an image");
        using (theme)
            Assert.NotNull(theme!.Assets.Image("sky.png"));
    }

    [Fact]
    public void The_last_good_layout_keeps_rendering_while_the_file_is_broken()
    {
        using var folder = new TempTheme(Fill("#FF0000"));
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog());
        var errors = new List<ThemeException>();
        using var watcher = new ThemeWatcher(folder.Folder, loop.SetTheme, errors.Add);

        watcher.Reload();
        loop.Step(TimeSpan.Zero);
        Pixels.AssertNear(SKColors.Red, device.LastFrame!.GetPixel(10, 10), 0);

        folder.Write(Broken);
        Assert.False(watcher.Reload());
        loop.Step(TimeSpan.FromSeconds(0.1));
        Pixels.AssertNear(SKColors.Red, device.LastFrame!.GetPixel(10, 10), 0);
        Assert.Equal(4, Assert.Single(errors).Line);

        folder.Write(Fill("#0000FF"));
        Assert.True(watcher.Reload());
        loop.Step(TimeSpan.FromSeconds(0.2));
        Pixels.AssertNear(SKColors.Blue, device.LastFrame!.GetPixel(10, 10), 0);
    }
}
