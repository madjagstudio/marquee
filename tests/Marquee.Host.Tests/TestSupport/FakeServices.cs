using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;

namespace Marquee.Host.Tests.TestSupport;

/// The engine's machine, faked: a RecordingDevice per start, providers that write nothing, servers that don't listen,
/// the Claude limits in a temp file, and a FakeRing when a test asks for one. It records every request so tests can check what the engine asked for.
sealed class FakeServices(string claudeLimitsPath) : IEngineServices
{
    /// What CreateDevice returns for each request; null means no screen is plugged in.
    public Func<EngineSettings, IDisplayDevice?> Device { get; set; } = _ => new RecordingDevice();

    public List<EngineSettings> DeviceRequests { get; } = [];
    public List<IDisplayDevice> Devices { get; } = [];
    public List<string> HardwareRequests { get; } = [];
    public List<AgentSource> AgentRequests { get; } = [];
    public List<FakeProvider> Providers { get; } = [];
    public List<FakeServer> Servers { get; } = [];

    /// What IsElevated says: false unless a test says otherwise, like a developer's unelevated terminal.
    public bool Elevated { get; set; }

    public int FpsRequests { get; private set; }

    public RecordingDevice LastDevice => (RecordingDevice)Devices[^1];

    public IReadOnlyList<FakeServer> HttpServers => Servers.Where(s => s.Kind == "http").ToList();

    public IDisplayDevice? CreateDevice(EngineSettings settings, ILog log)
    {
        DeviceRequests.Add(settings);
        var device = Device(settings);
        if (device is not null) Devices.Add(device);
        return device;
    }

    public ISensorProvider? CreateHardwareProvider(string source, ILog log)
    {
        HardwareRequests.Add(source);
        return source == SensorSourceNames.None ? null : Provider(source);
    }

    public ISensorProvider CreateAgentProvider(AgentSource source, ILog log)
    {
        AgentRequests.Add(source);
        return Provider(source.ToString());
    }

    public bool IsElevated => Elevated;

    public ISensorProvider CreateFpsProvider(ILog log)
    {
        FpsRequests++;
        return Provider("FPS");
    }

    public IDisposable StartStatusLineServer(SensorStore store, ILog log) => Server(new FakeServer("status line"));

    public IDisposable StartCustomValuesServer(SensorStore store, ILog log, int port, int maxKeys) =>
        Server(new FakeServer("http", port, maxKeys));

    public IDisposable StartSensorFeedServer(SensorStore store, ILog log) => Server(new FakeServer("sensor feed", store: store));

    public ClaudeLimitsFile CreateClaudeLimits(ILog log) => new(claudeLimitsPath, log);

    /// The LED ring the engine's ring senders find, or null (the default) for no ring sender at all, as where Marquee
    /// drives no ring.
    public FakeRing? Ring { get; set; }

    /// Whether the senders find Ring when they look, asked at each look; null means always.
    public Func<bool>? RingPresent { get; set; }

    public List<EngineSettings> RingRequests { get; } = [];
    public List<RingSender> RingSenders { get; } = [];

    public RingSender? CreateRing(EngineSettings settings, SensorStore store, ILog log)
    {
        RingRequests.Add(settings);
        if (Ring is not { } ring) return null;
        var sender = new RingSender(() => RingPresent?.Invoke() == false ? null : ring, store, log);
        RingSenders.Add(sender);
        return sender;
    }

    /// L-Connect's services for the handover. LConnect reads them too, so a test's busy screen can follow them.
    public FakeServiceControl LConnectServices { get; } = new();

    public LConnectDetector LConnect => LConnectServices.Detector;

    public IServiceControl CreateServiceControl() => LConnectServices;

    /// A name of this instance's own, so a Marquee running on the same PC never sees the test's signal.
    public string ScreenSignalName { get; } = $@"Local\Marquee.Tests.ScreenOpen.{Guid.NewGuid():N}";

    FakeProvider Provider(string name)
    {
        var provider = new FakeProvider(name);
        Providers.Add(provider);
        return provider;
    }

    FakeServer Server(FakeServer server)
    {
        Servers.Add(server);
        return server;
    }
}
