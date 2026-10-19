namespace Marquee.App;

/// Which balloon a click belongs to. NotifyIcon says only that "the balloon" was clicked, not which, so the tray
/// remembers what the balloon it showed last does when clicked: Ask's balloon takes over the screen, an update's
/// installs it, and a click on any later balloon must not do either. A new balloon replaces the one showing, and a
/// closed one takes no clicks. UI thread only (TrayApp).
sealed class BalloonClicks
{
    Action? _current;

    /// A balloon is being shown, with what its click does (null: nothing of its own).
    public void Showing(Action? onClick) => _current = onClick;

    /// The balloon showing was clicked: what it does, once. A click closes it.
    public Action? Clicked()
    {
        var onClick = _current;
        _current = null;
        return onClick;
    }

    /// The balloon timed out or the user closed it.
    public void Closed() => _current = null;
}
