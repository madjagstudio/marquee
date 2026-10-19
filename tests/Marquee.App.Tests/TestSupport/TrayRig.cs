using Marquee.Core.Logging;
using Marquee.Host;

namespace Marquee.App.Tests.TestSupport;

/// A TrayModel over fakes: the engine, Explorer, the window side, and updates over a fake GitHub (UpdateRig), with
/// config.yaml, the themes and the data in a temp folder. The engine shows example-landscape.
sealed class TrayRig : IDisposable
{
    public TrayRig(bool placeholderKey = false)
    {
        Paths = new TrayPaths(Dir.File("config.yaml"), new ThemeLocator([Dir.File("user"), Dir.File("installed")], Dir.Path),
            Dir.File("logs"), Dir.File("data"));
        Updates = new UpdateRig(Dir, placeholderKey);
        Engine.Showing(Path.GetDirectoryName(Dir.Write(Path.Combine("installed", "example-landscape", "layout.yaml"), "canvas: {}\n"))!);
        Model = new TrayModel(Engine, Paths, Shell, Host, Worker, Updates.Updates, NullLog.Instance, () => UtcNow);
    }

    public TempDir Dir { get; } = new();

    public FakeTrayEngine Engine { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeHost Host { get; } = new();

    public EngineWorker Worker { get; } = new();

    public UpdateRig Updates { get; }

    public TrayPaths Paths { get; }

    public DateTime UtcNow { get; set; } = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public TrayModel Model { get; }

    /// Completes once everything queued on the worker so far has run.
    public Task Flush() => Worker.Run(() => { }).WaitAsync(Wait.Timeout);

    /// The menu item with `key`, or null when the menu doesn't have it.
    public TrayMenuItem? Item(string key) => Model.BuildMenu().SingleOrDefault(item => item.Key == key);

    public void Dispose()
    {
        Worker.Dispose();
        Updates.Dispose();
        Dir.Dispose();
    }
}
