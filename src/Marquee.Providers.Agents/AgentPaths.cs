using Marquee.Core;

namespace Marquee.Providers.Agents;

/// Where Claude Code, Codex and Marquee keep their files. Claude Code honors CLAUDE_CONFIG_DIR and Codex honors
/// CODEX_HOME, so Marquee does too.
public static class AgentPaths
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ClaudeConfig =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Home, ".claude");

    public static string ClaudeSettings => Path.Combine(ClaudeConfig, "settings.json");

    public static string ClaudeSessions => Path.Combine(ClaudeConfig, "sessions");

    public static string CodexSessions =>
        Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } dir ? dir : Path.Combine(Home, ".codex"), "sessions");

    /// %LOCALAPPDATA%\Marquee: Marquee's own state (MarqueePaths.DataRoot).
    public static string MarqueeData => MarqueePaths.DataRoot;

    /// The status line Claude Code ran before `marquee setup-claude`, which `marquee statusline` runs in turn. Both run
    /// unelevated, so it stays in %LOCALAPPDATA%\Marquee.
    public static string SavedStatusLine => Path.Combine(MarqueeData, "claude-statusline.json");

    /// The last Claude Code limits Marquee saw, which it shows again at its next start (ClaudeLimitsFile). Written by
    /// `marquee run` and the tray, so it is in MarqueePaths.ProcessDataFolder: %LOCALAPPDATA%\Marquee unelevated, the
    /// admin-only %ProgramData%\Marquee\users\<SID> elevated (and throws there if that folder can't be made safe).
    public static string ClaudeLimits => Path.Combine(MarqueePaths.ProcessDataFolder, "claude-limits.json");
}
