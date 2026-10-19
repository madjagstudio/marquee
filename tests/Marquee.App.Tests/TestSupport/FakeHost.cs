using System.Collections.Concurrent;

namespace Marquee.App.Tests.TestSupport;

/// The window side, recording what the menu asked of it.
sealed class FakeHost : ITrayHost
{
    public ConcurrentQueue<(string Title, string Text, bool IsError)> Balloons { get; } = new();

    public ConcurrentQueue<(string Title, string Text)> Messages { get; } = new();

    public int PreviewsOpened;

    public int Exits;

    public void ShowBalloon(string title, string text, bool isError) => Balloons.Enqueue((title, text, isError));

    public void ShowMessage(string title, string text) => Messages.Enqueue((title, text));

    public void OpenPreview() => Interlocked.Increment(ref PreviewsOpened);

    public void Exit() => Interlocked.Increment(ref Exits);
}

/// Explorer, recording what it was asked to open.
sealed class FakeShell : IShell
{
    public ConcurrentQueue<string> Opened { get; } = new();

    public void Open(string path) => Opened.Enqueue(path);
}
