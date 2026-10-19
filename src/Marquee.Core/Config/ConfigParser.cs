using Marquee.Core.Devices;
using Marquee.Core.Sensors;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Marquee.Core.Config;

/// config.yaml → MarqueeConfig. Unknown keys are errors, so a typo never silently does nothing. It reads with the
/// theme parser's YAML helpers, so messages are worded the same way, and turns their ThemeException into a
/// ConfigException.
public static class ConfigParser
{
    public const string FileName = "config.yaml";

    /// YamlDotNet's node model doesn't read on and off as booleans the way YAML 1.1 does, so both spellings are listed.
    static readonly (string Name, bool Value)[] SwitchWords =
        [("on", true), ("off", false), ("true", true), ("false", false)];

    static readonly (string Name, HandoverMode Value)[] HandoverWords =
        [("off", HandoverMode.Off), ("ask", HandoverMode.Ask), ("auto", HandoverMode.Auto)];

    static readonly (string Name, bool Value)[] UpdatesWords = [("check", true), ("off", false)];

    static readonly (string Name, string Value)[] SensorSources = SensorSourceNames.All.Select(name => (name, name)).ToArray();

    /// An empty file, or one with only comments, gives MarqueeConfig.Default.
    public static MarqueeConfig Parse(string yaml)
    {
        try
        {
            return Yaml.Load(yaml, FileName) is { } root ? Read(root) : MarqueeConfig.Default;
        }
        catch (ThemeException ex)
        {
            throw new ConfigException(ex.Reason, ex.Line);
        }
    }

    static MarqueeConfig Read(YamlNode root)
    {
        var fields = Yaml.Map(root, FileName,
            "theme", "orientation", "fps", "sensors", "http", "providers", "custom", "ring", "lconnect", "updates");
        var (httpEnabled, httpPort) = fields.Optional("http") is { } http ? Http(http) : (true, null);
        var providers = fields.Optional("providers") is { } p
            ? Yaml.Map(p, "providers", "claude", "claude_usage", "codex", "fps")
            : null;
        var custom = fields.Optional("custom") is { } c ? Yaml.Map(c, "custom", "max_keys") : null;
        var (ring, ringColor) = fields.Optional("ring") is { } r ? Ring(r) : (RingMode.Theme, null);
        var lconnect = fields.Optional("lconnect") is { } l ? Yaml.Map(l, "lconnect", "handover") : null;

        return new MarqueeConfig
        {
            Theme = fields.Optional("theme") is { } theme ? Yaml.Text(theme, "theme") : null,
            Orientation = fields.Optional("orientation") is { } orientation
                ? Yaml.Choice(orientation, "orientation", OrientationNames.All.ToArray())
                : null,
            Fps = fields.Optional("fps") is { } fps ? Yaml.Integer(fps, "fps", 1, CanvasSpec.MaxFps) : null,
            Sensors = fields.Optional("sensors") is { } sensors ? Yaml.Choice(sensors, "sensors", SensorSources) : null,
            HttpEnabled = httpEnabled,
            HttpPort = httpPort,
            ClaudeSessions = Switch(providers, "claude"),
            ClaudeUsage = Switch(providers, "claude_usage"),
            Codex = Switch(providers, "codex"),
            GameFps = Switch(providers, "fps"),
            MaxCustomKeys = custom?.Optional("max_keys") is { } max
                ? Yaml.Integer(max, "custom.max_keys", 1, MarqueeConfig.MaxCustomKeysCeiling)
                : MarqueeConfig.DefaultMaxCustomKeys,
            Ring = ring,
            RingColor = ringColor,
            Handover = lconnect?.Optional("handover") is { } handover
                ? Yaml.Choice(handover, "lconnect.handover", HandoverWords)
                : HandoverMode.Off,
            HandoverSet = lconnect?.Optional("handover") is not null,
            CheckForUpdates = fields.Optional("updates") is not { } updates || Yaml.Choice(updates, "updates", UpdatesWords),
        };
    }

    /// `ring: theme`, `ring: off`, or one quoted color for every theme.
    static (RingMode Mode, SKColor? Color) Ring(YamlNode node)
    {
        if (node is YamlScalarNode { Value: "" })
            throw Yaml.Error(node, "ring is empty. Quote colors, e.g. \"#3A7BFF\": an unquoted # starts a YAML comment");

        var text = Yaml.Text(node, "ring");
        if (text == "theme") return (RingMode.Theme, null);
        if (text == "off") return (RingMode.Off, null);
        // The theme parser's hex forms, so a color copied from a theme works here. Its "transparent" is left out:
        // `off` already says that.
        if (text.StartsWith('#') && PaintParser.TryParseColor(text, out var color)) return (RingMode.Color, color);
        throw Yaml.Error(node, $"ring must be theme, off or a quoted color like \"#3A7BFF\"; not '{text}'");
    }

    /// `http: off`, or a mapping whose port, if given, replaces the server's default.
    static (bool Enabled, int? Port) Http(YamlNode node)
    {
        if (node is YamlScalarNode)
        {
            var text = Yaml.Text(node, "http");
            if (text != "off")
                throw Yaml.Error(node, $"http must be off or a mapping like {{ port: 47913 }}; not '{text}'");
            return (false, null);
        }

        var fields = Yaml.Map(node, "http", "port");
        int? port = fields.Optional("port") is { } p
            ? Yaml.Integer(p, "http.port", MarqueeConfig.MinHttpPort, MarqueeConfig.MaxHttpPort)
            : null;
        return (true, port);
    }

    /// A provider is on unless its section turns it off.
    static bool Switch(Fields? section, string key) =>
        section?.Optional(key) is not { } node || Yaml.Choice(node, $"{section.What}.{key}", SwitchWords);
}
