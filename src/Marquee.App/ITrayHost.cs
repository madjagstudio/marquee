namespace Marquee.App;

/// What the tray menu asks of the window side (TrayApp; a fake in tests). Every method may be called from any thread;
/// the window side moves the work to the UI thread.
interface ITrayHost
{
    /// A notification-area balloon. `isError` shows the error icon instead of the information one. `onClick` is what a
    /// click on this balloon does, on the UI thread; a click on a balloon with none, or on a later balloon, opens the
    /// menu instead.
    void ShowBalloon(string title, string text, bool isError, Action? onClick = null);

    /// A message box, for About.
    void ShowMessage(string title, string text);

    /// Opens the preview window, or leaves the open one as it is.
    void OpenPreview();

    /// Removes the icon and ends the tray. The engine has stopped by then.
    void Exit();
}
