using Marquee.Host.Tests.TestSupport;

namespace Marquee.Host.Tests;

/// The engine serves the theme editor's sensor feed wherever it runs, the tray and `marquee run` alike.
public class EngineSensorFeedTests : IDisposable
{
    readonly TempDir _dir = new();
    readonly TempTheme _theme = new(TempTheme.Fill("#FF0000"));
    readonly ListLog _log = new();
    readonly FakeServices _services;
    readonly EngineSettings _settings;

    public EngineSensorFeedTests()
    {
        _services = new FakeServices(_dir.File("claude-limits.json"));
        _settings = new EngineSettings { ThemeFolder = _theme.Folder, Sensors = "hwinfo" };
    }

    public void Dispose()
    {
        _theme.Dispose();
        _dir.Dispose();
    }

    MarqueeEngine Engine() => new(_settings, _log, _services);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Start_serves_the_engines_store(bool live)
    {
        using var engine = Engine();

        Assert.True(engine.Start(liveRendering: live));

        var feed = Assert.Single(_services.Servers, s => s.Kind == "sensor feed");
        Assert.Same(engine.Sensors, feed.Store);
        Assert.False(feed.Disposed);
    }

    [Fact]
    public void A_start_that_fails_serves_nothing()
    {
        _services.Device = _ => null;
        using var engine = Engine();

        Assert.False(engine.Start());

        Assert.DoesNotContain(_services.Servers, s => s.Kind == "sensor feed");
    }

    [Fact]
    public void A_settings_change_keeps_the_feed()
    {
        using var engine = Engine();
        engine.Start();
        var feed = Assert.Single(_services.Servers, s => s.Kind == "sensor feed");

        Assert.True(engine.Apply(_settings with { HttpPort = 50125, Sensors = "none", GameFps = false }));

        Assert.Same(feed, Assert.Single(_services.Servers, s => s.Kind == "sensor feed"));
        Assert.False(feed.Disposed);
    }

    [Fact]
    public void Stop_ends_the_feed_and_a_restart_serves_a_new_one()
    {
        using var engine = Engine();
        engine.Start();

        engine.Stop();
        Assert.True(Assert.Single(_services.Servers, s => s.Kind == "sensor feed").Disposed);

        Assert.True(engine.Start());
        var feeds = _services.Servers.Where(s => s.Kind == "sensor feed").ToList();
        Assert.Equal([true, false], feeds.Select(s => s.Disposed));
    }
}
