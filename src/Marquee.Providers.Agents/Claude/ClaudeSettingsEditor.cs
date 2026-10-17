using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marquee.Providers.Agents.Claude;

/// Something setup-claude won't do, with the reason in words for the user. Nothing was changed.
public sealed class ClaudeSetupException(string message) : Exception(message);

/// What setup-claude did (or, for a dry run, would do). NewSettings is the settings.json text it wrote or would write.
public sealed record SettingsChange(bool Changed, string Message, string? NewSettings);

/// Points Claude Code's `statusLine` at `marquee statusline` in ~/.claude/settings.json, and back. Only `statusLine` is
/// touched: every other key keeps its value and order. Each write keeps a timestamped backup of the old file and is
/// atomic. Files Marquee can't round-trip exactly (comments, trailing commas, not an object) are refused, not rewritten.
public static class ClaudeSettingsEditor
{
    static readonly JsonDocumentOptions Strict = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    /// The status-line command for this marquee.exe, quoted for the shell Claude Code will run it in (see ClaudeShell).
    /// Git Bash takes forward slashes; PowerShell needs the call operator to run a quoted path.
    public static string BridgeCommand(string exePath, bool gitBash) => gitBash
        ? $"\"{exePath.Replace('\\', '/').Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`")}\" statusline"
        : $"& '{exePath.Replace("'", "''")}' statusline";

    /// True for a statusLine that runs `marquee statusline` (from any install location).
    public static bool IsBridge(JsonNode? statusLine) =>
        SavedStatusLine.Command(statusLine as JsonObject) is { } command
        && command.Contains("marquee", StringComparison.OrdinalIgnoreCase)
        && command.TrimEnd().EndsWith(" statusline", StringComparison.Ordinal);

    public static SettingsChange Install(string settingsPath, string savedPath, string bridgeCommand, DateTimeOffset now, bool dryRun = false)
    {
        var (root, original) = Load(settingsPath);
        var current = root["statusLine"];

        if (IsBridge(current))
        {
            if (SavedStatusLine.Command((JsonObject)current!) == bridgeCommand)
                return new SettingsChange(false, "Claude Code's status line already goes through Marquee. Nothing to change.", null);

            // Set up before from another install location: point it here, and keep the status line saved back then.
            current!["command"] = bridgeCommand;
            return Finish(settingsPath, root, original, now, dryRun, "Claude Code's status line now runs this copy of Marquee.");
        }

        if (current is not null and not JsonObject)
            throw new ClaudeSetupException($"statusLine in {settingsPath} is not an object, so Marquee can't tell what it does. Nothing was changed.");

        var previous = (JsonObject?)current;
        var statusLine = (JsonObject?)previous?.DeepClone() ?? [];
        statusLine["type"] = "command";
        statusLine["command"] = bridgeCommand;
        root["statusLine"] = statusLine;

        var message = SavedStatusLine.Command(previous) is { } before
            ? $"Claude Code's status line now goes through Marquee. It still shows what it showed before: marquee statusline runs `{before}` and prints its output."
            : "Claude Code's status line now goes through Marquee. There was no status line before, so it stays blank.";
        if (!dryRun) SavedStatusLine.Save(savedPath, previous);
        return Finish(settingsPath, root, original, now, dryRun, message);
    }

    public static SettingsChange Undo(string settingsPath, string savedPath, DateTimeOffset now, bool dryRun = false)
    {
        if (!File.Exists(savedPath))
            return new SettingsChange(false, "Nothing to undo: marquee setup-claude hasn't changed Claude Code's settings.", null);

        var saved = SavedStatusLine.LoadStrict(savedPath);
        var (root, original) = Load(settingsPath);
        if (!IsBridge(root["statusLine"]))
            throw new ClaudeSetupException($"statusLine in {settingsPath} no longer runs marquee statusline, so it was changed since setup; " +
                $"leaving it alone. The status line from before setup is saved in {savedPath}.");

        if (saved is null) root.Remove("statusLine");
        else root["statusLine"] = saved.DeepClone();

        var change = Finish(settingsPath, root, original, now, dryRun,
            saved is null ? "Removed Marquee's status line; Claude Code has no status line again." : "Restored Claude Code's previous status line.");
        if (!dryRun) File.Delete(savedPath);
        return change;
    }

    static (JsonObject Root, byte[]? Original) Load(string path)
    {
        if (!File.Exists(path)) return ([], null);

        var original = File.ReadAllBytes(path);
        string text;
        using (var reader = new StreamReader(new MemoryStream(original), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();

        try
        {
            return JsonNode.Parse(text, documentOptions: Strict) is JsonObject root
                ? (root, original)
                : throw new ClaudeSetupException($"{path} is not a JSON object. Nothing was changed.");
        }
        catch (JsonException ex)
        {
            throw new ClaudeSetupException($"{path} isn't plain JSON that Marquee can rewrite exactly ({ex.Message}). Nothing was changed; " +
                "add the statusLine setting by hand, or fix the file and run the command again.");
        }
    }

    static SettingsChange Finish(string path, JsonObject root, byte[]? original, DateTimeOffset now, bool dryRun, string message)
    {
        var text = Serialize(root, original);
        if (dryRun) return new SettingsChange(true, message, text);

        if (original is null)
        {
            AtomicFile.WriteAllText(path, text);
            return new SettingsChange(true, $"{message} Created {path}.", text);
        }

        if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
            throw new ClaudeSetupException($"{path} changed while Marquee was editing it. Nothing was changed; run the command again.");

        var backup = $"{path}.marquee-backup-{now.ToLocalTime():yyyyMMdd-HHmmss}";
        for (var n = 2; File.Exists(backup); n++)
            backup = $"{path}.marquee-backup-{now.ToLocalTime():yyyyMMdd-HHmmss}-{n}";
        AtomicFile.Replace(path, text, backup);
        return new SettingsChange(true, $"{message} The old settings are in {backup}.", text);
    }

    /// Two-space indents; line endings follow the original file (LF for a new one), with a final newline.
    static string Serialize(JsonObject root, byte[]? original)
    {
        var text = root.ToJsonString(SavedStatusLine.Format).ReplaceLineEndings("\n") + "\n";
        var crlf = original is not null && Encoding.UTF8.GetString(original).Contains("\r\n", StringComparison.Ordinal);
        return crlf ? text.ReplaceLineEndings("\r\n") : text;
    }
}
