using SkiaSharp;

namespace Marquee.Preview;

/// The preview window on its own UI thread, so the render loop never waits for it. Every method is thread-safe.
/// Frames that arrive while the window is still taking the previous one are dropped.
public sealed class PreviewHost : IDisposable
{
    readonly Thread _thread;
    readonly ManualResetEventSlim _ready = new();
    PreviewWindow? _window;
    int _framePending;

    PreviewHost(string title, SKSizeI canvas)
    {
        _thread = new Thread(() => RunWindow(title, canvas)) { IsBackground = true, Name = "Marquee preview" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// Raised on the preview's thread when the user closes the window. The handler may call Dispose.
    public event Action? Closed;

    public static PreviewHost Open(string title, SKSizeI canvas)
    {
        var host = new PreviewHost(title, canvas);
        host._thread.Start();
        host._ready.Wait();
        return host;
    }

    /// Copies the frame, so the caller may dispose it as soon as this returns.
    public void ShowFrame(SKImage frame)
    {
        if (_window is not { IsHandleCreated: true, IsDisposed: false } window) return;
        if (Interlocked.Exchange(ref _framePending, 1) == 1) return;

        Bitmap bitmap;
        try
        {
            bitmap = FrameBitmap.ToBitmap(frame);
        }
        catch
        {
            Volatile.Write(ref _framePending, 0);
            throw;
        }

        try
        {
            window.BeginInvoke(new Action(() =>
            {
                try
                {
                    window.ShowFrame(bitmap);
                }
                finally
                {
                    Volatile.Write(ref _framePending, 0);
                }
            }));
        }
        catch (InvalidOperationException)
        {
            bitmap.Dispose(); // The window closed in the meantime.
            Volatile.Write(ref _framePending, 0);
        }
    }

    /// Null clears the banner.
    public void ShowError(string? message) => Post(window => window.ShowError(message));

    public void ShowStatus(string text) => Post(window => window.ShowStatus(text));

    public void Dispose()
    {
        Post(window => window.Close());
        if (Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2)); // Joining from the preview thread would wait on itself.
    }

    void RunWindow(string title, SKSizeI canvas)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var window = new PreviewWindow(title, canvas);
        window.HandleCreated += (_, _) => _ready.Set();
        window.FormClosed += (_, _) => Closed?.Invoke();
        _window = window;
        Application.Run(window);
        _ready.Set();
    }

    void Post(Action<PreviewWindow> action)
    {
        if (_window is not { IsHandleCreated: true, IsDisposed: false } window) return;
        try
        {
            window.BeginInvoke(new Action(() => action(window)));
        }
        catch (InvalidOperationException)
        {
            // The window closed in the meantime.
        }
    }
}
