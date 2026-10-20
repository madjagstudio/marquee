using Marquee.Core.Config;
using Marquee.Core.Rendering;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Host.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Host.Tests;

/// The engine and the LED ring: which ring shows, and that settings changes, Pause and Stop reach it.
public class EngineRingTests : IDisposable
{
    const string BlueRing = "ring: { color: \"#0000FF\", brightness: 100 }\n";

    readonly TempDir _dir = new();
    readonly TempTheme _theme = new(TempTheme.Fill("#FF0000"));
    readonly ListLog _log = new();
    readonly FakeRing _ring = new();
    readonly FakeServices _services;
    readonly EngineSettings _settings;

    public EngineRingTests()
    {
        _services = new FakeServices(_dir.File("claude-limits.json")) { Ring = _ring };
        _settings = new EngineSettings { ThemeFolder = _theme.Folder, Sensors = "hwinfo" };
    }

    public void Dispose()
    {
        _theme.Dispose();
        _dir.Dispose();
    }

    MarqueeEngine Engine(EngineSettings? settings = null) => new(settings ?? _settings, _log, _services);

    RingSender Sender => Assert.Single(_services.RingSenders);

    /// Whether every LED of the last frame the ring got is `color`.
    bool Shows(SKColor color) => _ring.LastFrame is { } frame && frame.All(c => c == color);

    /// The soft white of RingSpec.Default as the LEDs show it.
    static SKColor DefaultLit()
    {
        var colors = new SKColor[1];
        RingRenderer.Render(RingSpec.Default, new SensorStore().Snapshot(), TimeSpan.Zero, colors);
        return colors[0];
    }

    [Fact]
    public void A_theme_without_a_ring_block_shows_the_soft_white_default()
    {
        using var engine = Engine();

        Assert.True(engine.Start());

        Assert.Same(RingSpec.Default, Sender.Spec);
        Assert.True(Wait.Until(() => Shows(DefaultLit())), "The ring never showed the default.");
    }

    [Fact]
    public void Saving_the_theme_with_a_ring_block_lights_the_ring_its_way()
    {
        using var engine = Engine();
        engine.Start();

        _theme.Write(TempTheme.Fill("#FF0000") + BlueRing);

        Assert.True(Wait.Until(() => Sender.Spec?.Color == SKColors.Blue), "The reload did not reach the ring.");
        Assert.True(Wait.Until(() => Shows(SKColors.Blue)), "The ring never turned blue.");
    }

    [Fact]
    public void SwitchTheme_shows_the_new_themes_ring_on_the_same_sender()
    {
        using var blue = new TempTheme(TempTheme.Fill("#0000FF") + BlueRing);
        using var engine = Engine();
        engine.Start();

        Assert.True(engine.SwitchTheme(blue.Folder));

        Assert.Equal(SKColors.Blue, Sender.Spec!.Color);
        Assert.True(Wait.Until(() => Shows(SKColors.Blue)), "The ring never turned blue.");
    }

    [Fact]
    public void A_ring_change_in_config_yaml_reaches_the_ring_without_restarting_the_screen_or_providers()
    {
        using var engine = Engine();
        engine.Start();
        var screen = _services.LastDevice;
        Assert.True(Wait.Until(() => screen.FramesPushed > 0));
        var providers = _services.Providers.ToList();
        var sender = Sender;

        Assert.True(engine.Apply(_settings with { Ring = RingMode.Color, RingColor = SKColors.Lime }));

        var lime = sender.Spec!;
        Assert.Equal((SKColors.Lime, RingEffect.Static, 1f), (lime.Color, lime.Effect, lime.Brightness));
        Assert.True(Wait.Until(() => Shows(SKColors.Lime)), "The ring never turned green.");

        Assert.True(engine.Apply(_settings with { Ring = RingMode.Off }));

        Assert.Null(sender.Spec);
        Assert.True(Wait.Until(() => FakeRing.IsBlack(_ring.LastFrame) && !_ring.IsOpen), "The ring did not go dark.");
        Assert.Same(sender, Sender);
        Assert.Same(screen, Assert.Single(_services.Devices));
        Assert.Equal((1, 0, false), (screen.Opens, screen.Closes, screen.Disposed));
        Assert.Equal(providers, _services.Providers);
        Assert.All(providers, p => Assert.False(p.Disposed));
        Assert.All(_services.Servers, s => Assert.False(s.Disposed));
    }

    [Fact]
    public void Config_yaml_overrides_the_themes_ring_and_theme_hands_it_back()
    {
        _theme.Write(TempTheme.Fill("#FF0000") + BlueRing);
        using var engine = Engine(_settings with { Ring = RingMode.Color, RingColor = SKColors.Lime });
        engine.Start();
        Assert.Equal(SKColors.Lime, Sender.Spec!.Color);

        Assert.True(engine.Apply(_settings));

        Assert.Equal(SKColors.Blue, Sender.Spec!.Color);
    }

    [Fact]
    public void Pause_darkens_and_closes_the_ring_and_Resume_lights_it_again()
    {
        using var engine = Engine();
        engine.Start();
        Assert.True(Wait.Until(() => Shows(DefaultLit())), "The ring never lit.");

        engine.Pause();

        Assert.True(FakeRing.IsBlack(_ring.LastFrame));
        Assert.False(_ring.IsOpen);

        engine.Resume();

        Assert.True(Wait.Until(() => Shows(DefaultLit())), "The ring did not light again.");
        Assert.Equal(2, _ring.Opens);
    }

    [Fact]
    public void A_paused_start_leaves_the_ring_dark_until_Resume()
    {
        using var engine = Engine();
        engine.Pause();
        engine.Start();

        Thread.Sleep(300); // many ticks, had the sender been running
        Assert.Equal(0, _ring.FrameCount);

        engine.Resume();
        Assert.True(Wait.Until(() => Shows(DefaultLit())), "The ring did not light after Resume.");
    }

    [Fact]
    public void Stop_darkens_and_closes_the_ring()
    {
        using var engine = Engine();
        engine.Start();
        Assert.True(Wait.Until(() => _ring.FrameCount > 0), "The ring never lit.");

        engine.Stop();

        Assert.True(FakeRing.IsBlack(_ring.LastFrame));
        Assert.False(_ring.IsOpen);
        var frames = _ring.FrameCount;
        Thread.Sleep(200);
        Assert.Equal(frames, _ring.FrameCount);
    }

    [Fact]
    public void RenderFrames_leaves_the_ring_alone()
    {
        using var engine = Engine();

        Assert.True(engine.Start(liveRendering: false));
        engine.RenderFrames(2);

        Assert.Empty(_services.RingRequests);
        Assert.Equal(0, _ring.Opens);
    }

    [Fact]
    public void A_new_sink_asks_for_a_new_ring_and_lets_the_old_one_go()
    {
        using var engine = Engine();
        engine.Start();
        var first = Sender;
        Assert.True(Wait.Until(() => _ring.FrameCount > 0), "The ring never lit.");

        var sink = _dir.Folder("frames");
        Assert.True(engine.Apply(_settings with { Sink = sink }));

        Assert.Equal([null, sink], _services.RingRequests.Select(s => s.Sink));
        Assert.Equal(2, _services.RingSenders.Count);
        Assert.NotSame(first, _services.RingSenders[^1]);
        Assert.Contains(_ring.Frames, FakeRing.IsBlack); // the first sender darkened the ring as it went
    }

    [Fact]
    public void A_sink_folder_never_drives_a_real_ring()
    {
        var settings = _settings with { Sink = _dir.Folder("frames") };

        Assert.Null(MachineServices.Instance.CreateRing(settings, new SensorStore(), _log));
    }
}
