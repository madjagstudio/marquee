using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marquee.Core.Sensors;
using Marquee.Editor.Backgrounds;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Modules;
using Marquee.Editor.Start;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

public class GalleryPlaybackTests
{
    [Theory]
    [InlineData("background", "minimize")]
    [InlineData("background", "hide")]
    [InlineData("background", "pane")]
    [InlineData("module", "minimize")]
    [InlineData("module", "hide")]
    [InlineData("module", "pane")]
    [InlineData("start", "minimize")]
    [InlineData("start", "hide")]
    [InlineData("start", "pane")]
    public void Hidden_galleries_stop_requesting_render_values_and_resume_on_show(string kind, string action) => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        environment.AddExample("start-monitor", installed: true);
        var reads = 0;
        var thread = Environment.CurrentManagedThreadId;
        SensorSnapshot Values()
        {
            if (Environment.CurrentManagedThreadId == thread) reads++;
            return SensorSnapshot.Of(DateTimeOffset.UtcNow, new Dictionary<string, SensorEntry>());
        }
        Window window = kind switch
        {
            "background" => new BackgroundGalleryView(new BackgroundGalleryViewModel(new BackgroundLibrary(BackgroundGalleryTests.Folder()), Values)),
            "module" => new ModuleGalleryView(new ModuleGalleryViewModel(environment.Root, Values), _ => { }),
            _ => new StartView(new StartViewModel(environment.Environment with
            {
                ValueSources = () => new ValueSources(new NoTray(), new CountingReadings(Values)),
            })),
        };
        window.Left = -10000; window.Top = -10000; window.ShowActivated = false; window.ShowInTaskbar = false;
        window.Show(); Sta.Pump();
        try
        {
            var before = reads;
            Assert.True(Sta.PumpUntil(() => reads > before, TimeSpan.FromSeconds(3)));
            if (action == "minimize") window.WindowState = WindowState.Minimized;
            else if (action == "hide") window.Hide();
            else ((UIElement)window.Content).Visibility = System.Windows.Visibility.Collapsed;
            Sta.Pump();
            before = reads;
            var wait = System.Diagnostics.Stopwatch.StartNew();
            Assert.True(Sta.PumpUntil(() => wait.Elapsed >= TimeSpan.FromMilliseconds(1200), TimeSpan.FromSeconds(3)));
            Assert.Equal(before, reads);
            if (action == "minimize") window.WindowState = WindowState.Normal;
            else if (action == "hide") window.Show();
            else ((UIElement)window.Content).Visibility = System.Windows.Visibility.Visible;
            Assert.True(Sta.PumpUntil(() => reads > before, TimeSpan.FromSeconds(3)));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void Switching_category_renders_only_that_category() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        using var editor = new OpenEditor(environment, environment.AddTheme("blank", "canvas: { orientation: landscape }\nwidgets: []\n"));
        using var gallery = editor.Model.CreateModuleGallery()!;
        var window = new ModuleGalleryView(gallery, _ => { }) { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); Sta.Pump();
        try
        {
            var tabs = Walk(window).OfType<TabControl>().Single();
            var numbers = gallery.Categories.First();
            Assert.All(numbers.Tiles, tile => Assert.NotNull(tile.Bitmap));
            Assert.All(gallery.Categories.Skip(1).SelectMany(c => c.Tiles), tile => Assert.Null(tile.Bitmap));
            var before = numbers.Tiles.Select(tile => Pixels(tile.Bitmap!)).ToArray();
            tabs.SelectedItem = gallery.Categories.Single(c => c.Category == ModuleCategory.Graphs);
            Sta.Pump();
            editor.Workspace.Values.TestValues.Pin("cpu.temp", TestPin.Of(99));
            gallery.Refresh();
            Assert.All(((ModuleCategoryView)tabs.SelectedItem).Tiles, tile => Assert.NotNull(tile.Bitmap));
            for (var i = 0; i < numbers.Tiles.Count; i++) Assert.Equal(before[i], Pixels(numbers.Tiles[i].Bitmap!));
            Assert.All(gallery.Categories.Where(c => c.Category is not (ModuleCategory.Numbers or ModuleCategory.Graphs)).SelectMany(c => c.Tiles), tile => Assert.Null(tile.Bitmap));
        }
        finally { window.Close(); }
    });

    internal static int[] Pixels(System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var node in Walk(VisualTreeHelper.GetChild(root, i))) yield return node;
    }

    sealed class CountingReadings(Func<SensorSnapshot> snapshot) : ILocalReadings
    {
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public SensorSnapshot Snapshot() => snapshot();
        public void Dispose() => Stop();
    }
}
