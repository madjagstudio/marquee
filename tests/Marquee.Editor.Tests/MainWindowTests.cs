using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marquee.Editor.Model;
using Marquee.Editor.Session;
using Marquee.Editor.Shell;
using Marquee.Editor.Tests.TestSupport;

namespace Marquee.Editor.Tests;

/// The window itself, shown off screen against a temp theme with no tray and no sensors: it opens, draws a frame into
/// its bitmap, and closes cleanly. The shell holds no logic worth testing beyond that and the view-models.
public class MainWindowTests
{
    /// Where the layout check saves its picture, when set: a folder outside the repository. Fonts and DPI vary from PC
    /// to PC, so the picture is for looking at, not a snapshot to compare.
    const string SnapshotFolderVariable = "MARQUEE_EDITOR_SNAPSHOT_DIR";

    [Fact]
    public void The_window_opens_draws_a_frame_and_closes_cleanly() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var mine = environment.AddTheme("mine");
        var ui = SynchronizationContext.Current!;
        var workspace = EditorWorkspace.Open(mine, environment.Environment, ui)!;
        var window = OffScreen(new MainWindow(new MainViewModel(environment.Environment, workspace, ui)));

        window.Show();
        Sta.Pump();
        window.Model.Canvas.RenderNow();

        Assert.Equal("mine – Marquee Theme Editor", window.Title);
        var frame = window.Model.Canvas.Frame!;
        Assert.Equal((1920, 480), (frame.PixelWidth, frame.PixelHeight));
        Assert.Equal(Colors.Red, PixelAt(frame, 150, 150));
        Assert.Equal(Colors.Blue, PixelAt(frame, 550, 150));
        Assert.Equal(Colors.Black, PixelAt(frame, 50, 50));

        window.Close();
        Assert.Null(window.Model.Workspace);
        using (var themeLock = ThemeOpening.TryLock(mine)) Assert.NotNull(themeLock);
    });

    [Fact]
    public void The_layout_renders() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var ui = SynchronizationContext.Current!;
        var workspace = EditorWorkspace.Open(environment.AddTheme("mine"), environment.Environment, ui)!;
        var window = OffScreen(new MainWindow(new MainViewModel(environment.Environment, workspace, ui)));
        window.Show();
        workspace.Session.Select([OpenEditor.P("widgets[0]")]);
        Sta.Pump();
        window.Model.Canvas.RenderNow();
        Sta.Pump();

        var root = (FrameworkElement)window.Content;
        var picture = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        picture.Render(root);

        Assert.True(root.ActualWidth > 0 && root.ActualHeight > 0);
        // The canvas's dark surround is drawn, and the theme's red box shows in it.
        Assert.Contains(Colors.Red, Sample(picture));
        if (Environment.GetEnvironmentVariable(SnapshotFolderVariable) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            using var file = File.Create(Path.Combine(folder, "editor-main-window.png"));
            encoder.Save(file);
        }

        window.Close();
    });

    [Fact]
    public void Minimising_stops_asking_for_values_and_restoring_starts_again() => Sta.Run(() =>
    {
        using var environment = new TestEnvironment();
        var ui = SynchronizationContext.Current!;
        var workspace = EditorWorkspace.Open(environment.AddTheme("mine"), environment.Environment, ui)!;
        var window = OffScreen(new MainWindow(new MainViewModel(environment.Environment, workspace, ui)));
        window.Show();
        Sta.Pump();
        Assert.True(workspace.Values.IsActive);

        window.WindowState = WindowState.Minimized;
        Sta.Pump();

        Assert.False(workspace.Values.IsActive);

        window.WindowState = WindowState.Normal;
        Sta.Pump();

        Assert.True(workspace.Values.IsActive);
        window.Close();
    });

    static MainWindow OffScreen(MainWindow window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = 1400;
        window.Height = 860;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        return window;
    }

    static Color PixelAt(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    /// Every 4th pixel of every 4th row: enough to find a box of colour.
    static HashSet<Color> Sample(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var colors = new HashSet<Color>();
        for (var y = 0; y < bitmap.PixelHeight; y += 4)
        for (var x = 0; x < bitmap.PixelWidth; x += 4)
        {
            var at = y * stride + x * 4;
            colors.Add(Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]));
        }

        return colors;
    }
}
