using Marquee.Core.Devices;

namespace Marquee.Core.Config;

/// The user's settings from config.yaml. A nullable property is null when the file doesn't set it, so the precedence
/// step (command-line flag, then config.yaml, then the theme, then the default) can fall through to the next source.
/// The rest have no theme or command-line counterpart to fall through to, so they hold their default here.
public sealed record MarqueeConfig
{
    public const int DefaultMaxCustomKeys = 1000;

    /// The highest custom.max_keys accepts. Every distinct key costs memory in the sensor store and the HTTP server.
    public const int MaxCustomKeysCeiling = 10000;

    /// The ports http.port accepts, the same range `--http-port` and `marquee push --port` accept.
    public const int MinHttpPort = 1024;
    public const int MaxHttpPort = 65535;

    /// What an empty config.yaml, or one with only comments, means.
    public static MarqueeConfig Default { get; } = new();

    /// The theme to show: a folder name under the themes folders, or a full path. Null when not set, so `marquee run`
    /// needs a folder on its command line.
    public string? Theme { get; init; }

    /// How the screen is mounted. Null when not set, which falls through to the default, landscape.
    public Orientation? Orientation { get; init; }

    /// Frames per second, 1 to 60. Null when not set, which falls through to the theme's canvas.fps.
    public int? Fps { get; init; }

    /// Where hardware sensors come from: "lhm", "hwinfo" or "none". Null when not set, which falls through to "lhm".
    public string? Sensors { get; init; }

    /// Whether the local HTTP server for custom values runs. `http: off` turns it off.
    public bool HttpEnabled { get; init; } = true;

    /// The HTTP server's port. Null when not set, which falls through to the server's default port.
    public int? HttpPort { get; init; }

    /// The Claude Code sessions provider (`providers.claude`).
    public bool ClaudeSessions { get; init; } = true;

    /// The Claude Code `/usage` limits poll (`providers.claude_usage`).
    public bool ClaudeUsage { get; init; } = true;

    /// Both Codex providers, sessions and limits (`providers.codex`).
    public bool Codex { get; init; } = true;

    /// The FPS provider, which measures the foreground app's frame rate (`providers.fps`).
    public bool GameFps { get; init; } = true;

    /// How many distinct custom.* keys may be pushed (`custom.max_keys`), 1 to MaxCustomKeysCeiling.
    public int MaxCustomKeys { get; init; } = DefaultMaxCustomKeys;
}
