using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marquee.Providers.Agents.Claude;

/// The `statusLine` setting Claude Code had before `marquee setup-claude` replaced it, kept in
/// %LOCALAPPDATA%\Marquee\claude-statusline.json as `{ "statusLine": … }` (null when there was none).
public static class SavedStatusLine
{
    /// True if a saved file exists; `statusLine` is the saved setting (null if there was none or the file is unreadable).
    public static bool TryLoad(string path, out JsonObject? statusLine)
    {
        statusLine = null;
        if (!File.Exists(path)) return false;
        try
        {
            statusLine = JsonNode.Parse(File.ReadAllText(path))?["statusLine"] as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Unreadable counts as "no previous status line": the bridge then prints nothing, as before setup.
        }

        return true;
    }

    /// The command line of a `{ "type": "command", "command": "…" }` setting (type may be left out), or null.
    public static string? Command(JsonObject? statusLine)
    {
        if (statusLine is null) return null;
        var type = statusLine["type"];
        if (type is not null && (type.GetValueKind() != JsonValueKind.String || (string?)type != "command")) return null;
        var command = statusLine["command"];
        return command?.GetValueKind() == JsonValueKind.String && (string?)command is { Length: > 0 } text ? text : null;
    }
}
