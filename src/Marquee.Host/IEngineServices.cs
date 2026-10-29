using Marquee.Core.Devices;
using Marquee.Core.Logging;
using Marquee.Core.Sensors;
using Marquee.Providers.Agents.Claude;

namespace Marquee.Host;

/// Everything MarqueeEngine needs that touches the machine: the screen, the providers, the pipe and HTTP servers, and
/// the Claude limits file. MachineServices makes the real ones; tests pass fakes, so the engine runs with no screen,
/// no pipe, no HTTP and no `claude`.
public interface IEngineServices
{
    /// The screen the settings ask for (a FileSinkDevice for a sink folder), not opened yet, or null when no supported
    /// screen is plugged in. `log` gets what the screen reports while it runs.
    IDisplayDevice? CreateDevice(EngineSettings settings, ILog log);

    /// The hardware provider for a SensorSourceNames source, or null for "none".
    ISensorProvider? CreateHardwareProvider(string source, ILog log);

    /// One agent provider. Throwing is fine: AgentSources logs it and starts the others.
    ISensorProvider CreateAgentProvider(AgentSource source, ILog log);

    /// Whether Marquee runs elevated. The FPS provider needs it: PresentMon reads ETW, which takes administrator rights.
    bool IsElevated { get; }

    /// The FPS provider. The engine asks for it only when IsElevated.
    ISensorProvider CreateFpsProvider(ILog log);

    /// The status-line pipe server, already started. Disposing it stops it.
    IDisposable StartStatusLineServer(SensorStore store, ILog log);

    /// The custom-values HTTP server on 127.0.0.1:`port`, already started. Disposing it stops it.
    IDisposable StartCustomValuesServer(SensorStore store, ILog log, int port, int maxKeys);

    /// The theme editor's sensor feed (SensorFeedServer) for `store`, already started. Disposing it stops it.
    IDisposable StartSensorFeedServer(SensorStore store, ILog log);

    /// Where the last Claude Code limits are kept between runs. An engine made for a sink folder doesn't ask.
    ClaudeLimitsFile CreateClaudeLimits(ILog log);

    /// The LED ring's sender for these settings, not started yet, or null where Marquee drives no ring (a sink folder,
    /// tests that don't ask for one). The sender finds the ring itself, so a ring that isn't plugged in yet is fine.
    RingSender? CreateRing(EngineSettings settings, SensorStore store, ILog log);

    // The L-Connect handover. Tests fake these too, so no test touches L-Connect's real services.

    /// Says who holds a busy screen. The render loop asks it at each busy retry, every 2 s.
    LConnectDetector LConnect { get; }

    /// Stops and starts L-Connect's services. The engine asks for it only when IsElevated: stopping them takes
    /// administrator rights.
    IServiceControl CreateServiceControl();

    /// The name of the named event the engine sets while it has the screen (ScreenSignal), for `marquee claim-screen`.
    string ScreenSignalName { get; }
}
