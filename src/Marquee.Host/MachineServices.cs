using Marquee.Core.Devices;
using Marquee.Core.Devices.LedRing;
using Marquee.Core.Logging;
using Marquee.Core.Processes;
using Marquee.Core.Sensors;
using Marquee.Host.SensorFeed;
using Marquee.Providers.Agents.Claude;
using Marquee.Providers.Custom;
using Marquee.Providers.Fps;

namespace Marquee.Host;

/// The real screen, providers and servers, for `marquee run` and the tray.
public sealed class MachineServices : IEngineServices
{
    public static MachineServices Instance { get; } = new();

    MachineServices()
    {
    }

    public IDisplayDevice? CreateDevice(EngineSettings settings, ILog log)
    {
        var size = settings.Orientation.CanvasSize();
        return settings.Sink is not null
            ? new FileSinkDevice(settings.Sink, size.Width, size.Height)
            : Screens.Find(settings.Orientation, log);
    }

    public ISensorProvider? CreateHardwareProvider(string source, ILog log) => SensorSources.Create(source, log);

    public ISensorProvider CreateAgentProvider(AgentSource source, ILog log) => AgentSources.Build(source, log);

    public bool IsElevated => ProcessElevation.IsCurrentProcessElevated();

    public ISensorProvider CreateFpsProvider(ILog log) => new FpsProvider();

    /// A pipe that can't be created is logged by the server and leaves Claude Code limits to the other sources.
    public IDisposable StartStatusLineServer(SensorStore store, ILog log)
    {
        var server = new StatusLineServer(store, log);
        server.Start();
        return server;
    }

    /// A port that can't be used is logged by the server; the rest of Marquee runs on.
    public IDisposable StartCustomValuesServer(SensorStore store, ILog log, int port, int maxKeys)
    {
        var server = new CustomValuesServer(store, log, port, maxKeys);
        server.Start();
        return server;
    }

    /// A pipe that can't be made is logged by the server and tried again; the rest of Marquee runs on.
    public IDisposable StartSensorFeedServer(SensorStore store, ILog log)
    {
        var server = new SensorFeedServer(store, log, TimeProvider.System);
        server.Start();
        return server;
    }

    public ClaudeLimitsFile CreateClaudeLimits(ILog log) => new(log);

    /// The screen's LED ring, which the sender looks for itself, so one that enumerates after the screen still lights.
    /// A sink folder gets none: a run that writes its frames to files must not light a real ring.
    public RingSender? CreateRing(EngineSettings settings, SensorStore store, ILog log) =>
        settings.Sink is null ? new RingSender(LedRings.Find, store, log) : null;

    public LConnectDetector LConnect => LConnectDetector.Default;

    public IServiceControl CreateServiceControl() => new WindowsServiceControl();

    public string ScreenSignalName => ScreenSignal.DefaultName;
}
