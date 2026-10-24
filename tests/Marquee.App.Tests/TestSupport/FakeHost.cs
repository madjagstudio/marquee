using System.Collections.Concurrent;

namespace Marquee.App.Tests.TestSupport;

/// The window side, recording what the menu asked of it. Its balloons take clicks the way TrayApp's do (BalloonClicks):
/// ClickBalloon clicks the one showing, the last shown.
sealed class FakeHost : ITrayHost
{
    readonly BalloonClicks _clicks = new();

    public ConcurrentQueue<(string Title, string Text, bool IsError)> Balloons { get; } = new();

    public ConcurrentQueue<(string Title, string Text)> Messages { get; } = new();

    public int PreviewsOpened;

    public int Exits;

    /// Runs inside Exit, for tests that check what came before it.
    public Action? OnExit { get; set; }

    public void ShowBalloon(string title, string text, bool isError, Action? onClick = null)
    {
        // TrayApp does both on the UI thread; a lock stands in for it here.
        lock (_clicks)
        {
            _clicks.Showing(onClick);
            Balloons.Enqueue((title, text, isError));
        }
    }

    /// The user clicks the balloon showing, as TrayApp hears it from NotifyIcon.BalloonTipClicked.
    public void ClickBalloon()
    {
        Action? onClick;
        lock (_clicks) onClick = _clicks.Clicked();
        onClick?.Invoke();
    }

    public void ShowMessage(string title, string text) => Messages.Enqueue((title, text));

    public void OpenPreview() => Interlocked.Increment(ref PreviewsOpened);

    public void Exit()
    {
        OnExit?.Invoke();
        Interlocked.Increment(ref Exits);
    }
}

/// Explorer, recording what it was asked to open.
sealed class FakeShell : IShell
{
    public ConcurrentQueue<string> Opened { get; } = new();

    public void Open(string path) => Opened.Enqueue(path);
    public ConcurrentQueue<(string Path, string Arguments)> Commands { get; } = new();
    public void Open(string path, string arguments) => Commands.Enqueue((path, arguments));
}
