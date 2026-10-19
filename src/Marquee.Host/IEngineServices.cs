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
    /// screen is plugged in.
    IDisplayDevice? CreateDevice(EngineSettings settings);

    /// The hardware provider for a SensorSourceNames source, or null for "none".
    ISensorProvider? CreateHardwareProvider(string source, ILog log);

    /// One agent provider. Throwing is fine: AgentSources logs it and starts the others.
    ISensorProvider CreateAgentProvider(AgentSource source, ILog log);

    /// The status-line pipe server, already started. Disposing it stops it.
    IDisposable StartStatusLineServer(SensorStore store, ILog log);

    /// The custom-values HTTP server on 127.0.0.1:`port`, already started. Disposing it stops it.
    IDisposable StartCustomValuesServer(SensorStore store, ILog log, int port, int maxKeys);

    /// Where the last Claude Code limits are kept between runs.
    ClaudeLimitsFile CreateClaudeLimits(ILog log);
}
