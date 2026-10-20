using System.Windows.Forms;

namespace Marquee.App;

/// A hidden window that hears Windows say a device was added or removed, so the tray tries the screen again at once
/// instead of at its next timed retry. WM_DEVICECHANGE with DBT_DEVNODES_CHANGED is broadcast to every top-level window
/// without registering for it; a message-only window wouldn't get broadcasts, so this is an ordinary top-level window
/// that is never shown.
sealed class DeviceChangeWindow : NativeWindow, IDisposable
{
    const int WM_DEVICECHANGE = 0x0219;
    const int DBT_DEVNODES_CHANGED = 0x0007;

    readonly Action _changed;

    /// Call on the UI thread; `changed` is called there.
    public DeviceChangeWindow(Action changed)
    {
        _changed = changed;
        CreateHandle(new CreateParams());
    }

    public void Dispose() => DestroyHandle();

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE && m.WParam == DBT_DEVNODES_CHANGED) _changed();
        base.WndProc(ref m);
    }
}
