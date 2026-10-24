using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Marquee.Editor.Theme;

/// Windows draws a window's title bar and border itself, light unless the window asks for the dark frame. The theme's
/// Window style sets IsEnabled, so every window's frame matches its dark content.
public static class DarkFrame
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DarkFrame), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(Window window) => (bool)window.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(Window window, bool value) => window.SetValue(IsEnabledProperty, value);

    static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window || e.NewValue is not true) return;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window);
        else window.SourceInitialized += (_, _) => Apply(window);
    }

    // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 from Windows 10 20H1, 19 before it. Older Windows refuses both and keeps the
    // light frame.
    static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var on = 1;
        if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
