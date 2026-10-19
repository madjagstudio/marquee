using Marquee.Core.Config;
using Marquee.Core.Devices;
using Marquee.Core.Sensors;
using Marquee.Providers.Custom;

namespace Marquee.Host;

/// Everything MarqueeEngine runs with, after SettingsResolver has settled the command line against config.yaml. The
/// initial values are the defaults. A record, so the engine can tell whether a config.yaml edit changed anything.
public sealed record EngineSettings
{
    /// The theme folder, as a full path.
    public required string ThemeFolder { get; init; }

    public Orientation Orientation { get; init; } = Orientation.Landscape;

    /// Frames per second for every theme, or null for each theme's canvas.fps.
    public int? FpsOverride { get; init; }

    /// The hardware sensor source, one of SensorSourceNames.
    public string Sensors { get; init; } = SensorSourceNames.Default;

    /// The custom-values HTTP server's port, or null to leave the server off.
    public int? HttpPort { get; init; } = CustomValuesServer.DefaultPort;

    /// The Claude Code sessions provider.
    public bool ClaudeSessions { get; init; } = true;

    /// The Claude Code `/usage` limits poll.
    public bool ClaudeUsage { get; init; } = true;

    /// Both Codex providers.
    public bool Codex { get; init; } = true;

    /// The FPS provider (`providers.fps`, `--no-fps`). It runs only when Marquee is elevated as well.
    public bool GameFps { get; init; } = true;

    /// How many distinct custom.* keys the HTTP server takes.
    public int MaxCustomKeys { get; init; } = MarqueeConfig.DefaultMaxCustomKeys;

    /// A folder to write each frame to as a PNG (FileSinkDevice) instead of the screen, or null for the screen.
    public string? Sink { get; init; }
}
