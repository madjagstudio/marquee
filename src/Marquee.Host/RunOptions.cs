using Marquee.Core.Devices;

namespace Marquee.Host;

/// What `marquee run`'s command line says. Null means the flag wasn't given, so config.yaml, then the default, decides.
/// The tray has no command line and passes an empty RunOptions.
public sealed record RunOptions
{
    /// The theme folder argument: a path, or a name looked up like config.yaml's `theme:` (ThemeLocator).
    public string? Theme { get; init; }

    public Orientation? Orientation { get; init; }

    public int? Fps { get; init; }

    public string? Sensors { get; init; }

    /// True for `--http-port`, false for `--no-http`.
    public bool? HttpEnabled { get; init; }

    public int? HttpPort { get; init; }

    /// False for `--no-claude-usage`.
    public bool? ClaudeUsage { get; init; }

    /// False for `--no-fps`.
    public bool? GameFps { get; init; }

    public string? Sink { get; init; }

    /// `--config <file>`: read this file instead of %LOCALAPPDATA%\Marquee\config.yaml.
    public string? ConfigFile { get; init; }

    /// `--frames N`: render N frames and exit. Command line only; config.yaml has no counterpart.
    public int? Frames { get; init; }

    /// `--preview`: show the frames in a window too. Command line only; config.yaml has no counterpart.
    public bool Preview { get; init; }
}
