using Marquee.Core.Config;
using Marquee.Core.Sensors;
using Marquee.Host;

namespace Marquee.Editor.Model.Values;

/// What the editor may read itself, from config.yaml as the tray reads it: Hardware, the source `sensors:` names (lhm
/// when unset; null for `none`, which a user sets after a driver or anti-cheat problem), and Agents, the providers its
/// `providers:` switches leave on (`claude` and `codex` are opt-outs of reading their session files), never Claude's
/// `/usage`, which starts `claude.exe` and the tray already polls.
public sealed record LocalSources(string? Hardware, IReadOnlyList<AgentSource> Agents)
{
    public static LocalSources Of(MarqueeConfig config)
    {
        var hardware = config.Sensors ?? SensorSourceNames.Default;
        return new LocalSources(hardware == SensorSourceNames.None ? null : hardware,
            AgentSources.Enabled(config.ClaudeSessions, claudeUsage: false, config.Codex));
    }
}
