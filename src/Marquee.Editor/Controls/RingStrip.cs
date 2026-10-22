using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Marquee.Editor.Properties;
using SkiaSharp;

namespace Marquee.Editor.Controls;

/// Sixty cells drawn from the runtime renderer, ticking only while this section can be seen.
public sealed class RingStrip : FrameworkElement
{
    readonly DispatcherTimer _timer;
    readonly Stopwatch _elapsed = Stopwatch.StartNew();
    Window? _window;
    public RingStrip()
    {
        Height = 18;
        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => RenderAt(_elapsed.Elapsed);
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null) _window.StateChanged += OnWindowState;
            UpdateTimer();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            if (_window is not null) _window.StateChanged -= OnWindowState;
            _window = null;
        };
        IsVisibleChanged += (_, _) => UpdateTimer();
        DataContextChanged += (_, _) => UpdateTimer();
    }

    public IReadOnlyList<SKColor> Colors { get; private set; } = [];
    public bool IsAnimating => _timer.IsEnabled;

    void OnWindowState(object? sender, EventArgs args) => UpdateTimer();

    void UpdateTimer()
    {
        if (IsLoaded && IsVisible && _window?.WindowState != WindowState.Minimized && DataContext is RingViewModel)
        {
            RenderAt(_elapsed.Elapsed);
            _timer.Start();
        }
        else _timer.Stop();
    }

    public void RenderAt(TimeSpan elapsed)
    {
        if (DataContext is not RingViewModel ring) return;
        Colors = ring.Colors(elapsed);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Colors.Count == 0) return;
        var width = ActualWidth / Colors.Count;
        for (var i = 0; i < Colors.Count; i++)
        {
            var color = Colors[i];
            var brush = new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
            brush.Freeze();
            drawingContext.DrawRectangle(brush, null, new Rect(i * width, 0, Math.Max(0, width - 1), ActualHeight));
        }
    }
}
