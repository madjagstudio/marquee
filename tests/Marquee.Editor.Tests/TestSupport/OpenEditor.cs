using System.Windows;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Canvas;
using Marquee.Editor.Model;
using Marquee.Editor.Session;
using Marquee.Editor.Shell;
using SkiaSharp;

namespace Marquee.Editor.Tests.TestSupport;

/// A theme open in the shell as the window has it, without the window: the workspace and the main view-model, made on
/// the STA thread (Sta.Run), and closed cleanly on dispose.
sealed class OpenEditor : IDisposable
{
    public OpenEditor(TestEnvironment environment, string themeFolder)
    {
        var ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Run this inside Sta.Run.");
        Workspace = EditorWorkspace.Open(themeFolder, environment.Environment, ui)
            ?? throw new InvalidOperationException($"{themeFolder} didn't open: {string.Join("; ", environment.Dialogs.Errors)}");
        Model = new MainViewModel(environment.Environment, Workspace, ui);
    }

    public EditorWorkspace Workspace { get; }

    public MainViewModel Model { get; }

    public CanvasViewModel Canvas => Model.Canvas;

    public EditorSession Session => Workspace.Session;

    /// Sets the canvas's zoom on a screen at `dpiScale`: by default 100% at 150%, so each theme pixel is two-thirds of
    /// a WPF unit, and the mouse's positions go through both.
    public void Zoom(ZoomMode mode = ZoomMode.Actual, double dpiScale = 1.5)
    {
        Canvas.SetViewport(new Size(1200, 700), dpiScale);
        Canvas.ZoomMode = mode;
    }

    /// Where theme pixel (x, y) is in the canvas view.
    public Point View(float x, float y) => Canvas.Transform.ToView(new SKPoint(x, y));

    public static YamlPath P(string path) => YamlPath.Parse(path);

    public void Dispose()
    {
        Model.TryClose();
        Model.Dispose();
    }
}
